using System.Globalization;
using System.Text.Json;
using KnowledgeEngine.Application.DTOs;
using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Domain.Entities;
using KnowledgeEngine.Infrastructure.Db;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;

namespace KnowledgeEngine.Infrastructure.Search;

public class SearchService : ISearchService
{
    private const double MinVectorSimilarity = 0.25;
    private const int MaxResults = 20;

    private readonly AppDbContext _db;
    private readonly IEmbeddingService _embeddingService;
    private readonly IChineseFullTextIndexService _fullTextIndex;
    private readonly IRetrievalFusionService _fusion;
    private readonly IChineseTokenizer _tokenizer;
    private readonly ILogger<SearchService> _logger;

    public SearchService(
        AppDbContext db,
        IEmbeddingService embeddingService,
        IChineseFullTextIndexService fullTextIndex,
        IRetrievalFusionService fusion,
        IChineseTokenizer tokenizer,
        ILogger<SearchService> logger)
    {
        _db = db;
        _embeddingService = embeddingService;
        _fullTextIndex = fullTextIndex;
        _fusion = fusion;
        _tokenizer = tokenizer;
        _logger = logger;
    }

    public async Task<ApiResponse<SearchResult>> SearchAsync(
        Guid userId,
        SearchRequest request,
        CancellationToken ct = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            if (string.IsNullOrWhiteSpace(request.Query))
            {
                return ApiResponse<SearchResult>.Fail("invalid_query", "Query cannot be empty");
            }

            var searchType = string.IsNullOrEmpty(request.SearchType) ? "hybrid" : request.SearchType.ToLowerInvariant();
            var limit = request.Limit > 0 && request.Limit <= MaxResults ? request.Limit : MaxResults;

            List<SearchResultItem> items;

            switch (searchType)
            {
                case "keyword":
                    items = await KeywordSearchAsync(userId, request, limit, ct);
                    break;
                case "vector":
                    items = await VectorSearchAsync(userId, request, limit, ct);
                    break;
                case "hybrid":
                default:
                    items = await HybridSearchAsync(userId, request, limit, ct);
                    break;
            }

            sw.Stop();

            // Record search log
            await RecordSearchLogAsync(userId, request, searchType, items.Count, sw.ElapsedMilliseconds, ct);

            var result = new SearchResult
            {
                Query = request.Query,
                SearchType = searchType,
                Total = items.Count,
                Items = items,
                DebugInfo = new SearchDebugInfo
                {
                    SearchMode = searchType,
                    LatencyMs = sw.ElapsedMilliseconds,
                    KeywordMatchCount = searchType == "keyword" || searchType == "hybrid" ? items.Count : 0,
                    VectorMatchCount = searchType == "vector" || searchType == "hybrid" ? items.Count : 0,
                    FusionMode = searchType == "hybrid" ? request.FusionMode : null
                }
            };

            return ApiResponse<SearchResult>.Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Search failed for query: {Query}", request.Query);
            return ApiResponse<SearchResult>.Fail("search_error", $"Search failed: {ex.Message}");
        }
    }

    // ===== Keyword Search =====

    private static string NormalizeKeywordQuery(string rawQuery)
    {
        var normalized = rawQuery.Trim().ToLowerInvariant();
        var technicalTerms = System.Text.RegularExpressions.Regex.Matches(
                normalized,
                @"[a-z0-9][a-z0-9._+-]*",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase)
            .Select(match => match.Value.Trim('.', '-', '_', '+'))
            .Where(term => term.Length >= 2)
            .OrderByDescending(term => term.Any(char.IsDigit))
            .ThenByDescending(term => term.Length)
            .ToList();

        if (technicalTerms.Count > 0)
        {
            return technicalTerms[0];
        }

        var questionWords = new[]
        {
            "请问", "麻烦", "告诉我", "什么时候", "是什么", "怎么样", "为什么",
            "如何", "哪些", "多少", "是否", "能否", "可以", "相关", "资料", "发布"
        };
        foreach (var word in questionWords)
        {
            normalized = normalized.Replace(word, string.Empty, StringComparison.Ordinal);
        }

        normalized = System.Text.RegularExpressions.Regex.Replace(normalized, @"[^\p{L}\p{N}]+", string.Empty);
        return normalized.Length >= 2 ? normalized : rawQuery.Trim().ToLowerInvariant();
    }

    private async Task<List<SearchResultItem>> KeywordSearchAsync(
        Guid userId,
        SearchRequest request,
        int limit,
        CancellationToken ct)
    {
        var query = NormalizeKeywordQuery(request.Query);
        var terms = BuildKeywordTerms(query);
        var filters = request.Filters;

        // Pre-compute tag/entity filter doc id sets once (async) to apply to both queries
        List<Guid>? tagFilterDocIds = null;
        List<Guid>? entityFilterDocIds = null;
        if (filters?.TagIds != null && filters.TagIds.Count > 0)
        {
            tagFilterDocIds = await _db.DocumentTags
                .Where(dt => filters.TagIds.Contains(dt.TagId))
                .Select(dt => dt.DocumentId)
                .Distinct()
                .ToListAsync(ct);
        }
        if (filters?.EntityIds != null && filters.EntityIds.Count > 0)
        {
            entityFilterDocIds = await _db.DocumentEntities
                .Where(de => filters.EntityIds.Contains(de.EntityId))
                .Select(de => de.DocumentId)
                .Distinct()
                .ToListAsync(ct);
        }

        // Search in document_chunks content using ILIKE
        var chunkQuery = from c in _db.DocumentChunks
                         join d in _db.Documents on c.DocumentId equals d.Id
                         join s in _db.Sources on d.SourceId equals s.Id
                         where c.UserId == userId
                             && terms.Any(term => c.Content.ToLower().Contains(term))
                         select new { c, d, s };

        // Apply filters inline
        if (filters != null)
        {
            if (!string.IsNullOrEmpty(filters.SourceType))
            {
                chunkQuery = chunkQuery.Where(x => x.s.SourceType == filters.SourceType);
            }
            if (filters.DateFrom.HasValue)
            {
                chunkQuery = chunkQuery.Where(x => x.s.PublishedAt >= filters.DateFrom || x.d.CreatedAt >= filters.DateFrom);
            }
            if (filters.DateTo.HasValue)
            {
                chunkQuery = chunkQuery.Where(x => x.s.PublishedAt <= filters.DateTo || x.d.CreatedAt <= filters.DateTo);
            }
            if (filters.MinValueScore.HasValue)
            {
                chunkQuery = chunkQuery.Where(x => x.d.ValueScore >= filters.MinValueScore);
            }
            if (tagFilterDocIds != null)
            {
                chunkQuery = chunkQuery.Where(x => tagFilterDocIds.Contains(x.d.Id));
            }
            if (entityFilterDocIds != null)
            {
                chunkQuery = chunkQuery.Where(x => entityFilterDocIds.Contains(x.d.Id));
            }
            if (!string.IsNullOrEmpty(filters.Domain))
            {
                chunkQuery = chunkQuery.Where(x => x.s.Domain == filters.Domain);
            }
        }

        var chunkResults = await chunkQuery
            .Select(x => new SearchResultItem
            {
                DocumentId = x.d.Id,
                ChunkId = x.c.Id,
                Title = x.d.Title,
                Snippet = x.c.Content.Length > 300 ? x.c.Content.Substring(0, 300) + "..." : x.c.Content,
                SourceType = x.s.SourceType,
                SourceUrl = x.s.Url,
                SourceDomain = x.s.Domain,
                PublishedAt = x.s.PublishedAt,
                ValueScore = x.d.ValueScore,
                Score = 1.0,
                ScoreDetail = new ScoreDetail
                {
                    KeywordScore = 1.0,
                    VectorScore = 0,
                    FreshnessScore = 0,
                    ValueScore = x.d.ValueScore.HasValue ? x.d.ValueScore.Value / 100.0 : 0
                }
            })
            .Take(limit)
            .ToListAsync(ct);

        // Also search in documents title/content_text
        var docQuery = from d in _db.Documents
                       join s in _db.Sources on d.SourceId equals s.Id
                       where d.UserId == userId
                           && (terms.Any(term => d.Title.ToLower().Contains(term)) ||
                               (d.ContentText != null && terms.Any(term => d.ContentText.ToLower().Contains(term))) ||
                               (d.Summary != null && terms.Any(term => d.Summary.ToLower().Contains(term))))
                       select new { d, s };

        // Apply filters inline
        if (filters != null)
        {
            if (!string.IsNullOrEmpty(filters.SourceType))
            {
                docQuery = docQuery.Where(x => x.s.SourceType == filters.SourceType);
            }
            if (filters.DateFrom.HasValue)
            {
                docQuery = docQuery.Where(x => x.s.PublishedAt >= filters.DateFrom || x.d.CreatedAt >= filters.DateFrom);
            }
            if (filters.DateTo.HasValue)
            {
                docQuery = docQuery.Where(x => x.s.PublishedAt <= filters.DateTo || x.d.CreatedAt <= filters.DateTo);
            }
            if (filters.MinValueScore.HasValue)
            {
                docQuery = docQuery.Where(x => x.d.ValueScore >= filters.MinValueScore);
            }
            if (tagFilterDocIds != null)
            {
                docQuery = docQuery.Where(x => tagFilterDocIds.Contains(x.d.Id));
            }
            if (entityFilterDocIds != null)
            {
                docQuery = docQuery.Where(x => entityFilterDocIds.Contains(x.d.Id));
            }
            if (!string.IsNullOrEmpty(filters.Domain))
            {
                docQuery = docQuery.Where(x => x.s.Domain == filters.Domain);
            }
        }

        var docResults = await docQuery
            .Select(x => new SearchResultItem
            {
                DocumentId = x.d.Id,
                ChunkId = Guid.Empty,
                Title = x.d.Title,
                Snippet = x.d.Summary != null && x.d.Summary.Length > 300
                    ? x.d.Summary.Substring(0, 300) + "..."
                    : (x.d.Summary ?? x.d.Title),
                SourceType = x.s.SourceType,
                SourceUrl = x.s.Url,
                SourceDomain = x.s.Domain,
                PublishedAt = x.s.PublishedAt,
                ValueScore = x.d.ValueScore,
                Score = 0.8,
                ScoreDetail = new ScoreDetail
                {
                    KeywordScore = 0.8,
                    VectorScore = 0,
                    FreshnessScore = 0,
                    ValueScore = x.d.ValueScore.HasValue ? x.d.ValueScore.Value / 100.0 : 0
                }
            })
            .Take(limit)
            .ToListAsync(ct);

        foreach (var item in chunkResults)
        {
            var score = CalculateTokenMatchScore(terms, $"{item.Title} {item.Snippet}");
            item.Score = score;
            item.ScoreDetail!.KeywordScore = score;
            item.MatchChannels = new List<string> { "keyword" };
        }
        foreach (var item in docResults)
        {
            var score = CalculateTokenMatchScore(terms, $"{item.Title} {item.Snippet}");
            item.Score = score;
            item.ScoreDetail!.KeywordScore = score;
            item.MatchChannels = new List<string> { "keyword" };
        }

        // Merge and deduplicate (by document_id + chunk_id)
        var merged = new List<SearchResultItem>();
        var seen = new HashSet<string>();

        foreach (var item in chunkResults.Concat(docResults))
        {
            var key = $"{item.DocumentId}:{item.ChunkId}";
            if (seen.Add(key))
            {
                merged.Add(item);
            }
        }

        return merged.Take(limit).ToList();
    }

    private List<string> BuildKeywordTerms(string query)
    {
        var tokenized = _tokenizer.Tokenize(query);
        var terms = tokenized.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(term => term.Length >= 2)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(term => term.Length)
            .Take(16)
            .ToList();
        if (terms.Count == 0 && !string.IsNullOrWhiteSpace(query)) terms.Add(query);
        return terms;
    }

    private static double CalculateTokenMatchScore(IReadOnlyCollection<string> terms, string text)
    {
        if (terms.Count == 0 || string.IsNullOrWhiteSpace(text)) return 0;
        var normalized = text.ToLowerInvariant();
        var matched = terms.Count(term => normalized.Contains(term, StringComparison.Ordinal));
        return Math.Clamp(Math.Max(1d / terms.Count, (double)matched / terms.Count), 0, 1);
    }

    // ===== Vector Search =====

    private async Task<List<SearchResultItem>> VectorSearchAsync(
        Guid userId,
        SearchRequest request,
        int limit,
        CancellationToken ct)
    {
        // Generate query embedding
        var queryEmbedding = await _embeddingService.EmbedAsync(request.Query, ct);

        var hasMultiVectorRows = await _db.ChunkEmbeddings.AsNoTracking()
            .AnyAsync(x => x.Status == "done" && x.EmbeddingType != "original", ct);
        if (_db.Database.IsSqlite() || hasMultiVectorRows)
        {
            return await VectorSearchSqliteAsync(userId, request, queryEmbedding, limit, ct);
        }

        // Build vector string for raw SQL
        var vectorStr = "[" + string.Join(",",
            queryEmbedding.Select(v => v.ToString("G8", CultureInfo.InvariantCulture))) + "]";

        var filters = request.Filters;
        var sql = BuildVectorSearchSql(request.TopicId, filters);
        var parameters = BuildVectorSearchParameters(userId, request.TopicId, filters, vectorStr);

        var connection = (NpgsqlConnection)_db.Database.GetDbConnection();
        var shouldClose = false;

        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(ct);
            shouldClose = true;
        }

        try
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = sql;
            foreach (var p in parameters)
            {
                cmd.Parameters.Add(p);
            }

            using var reader = await cmd.ExecuteReaderAsync(ct);
            var results = new List<SearchResultItem>();

            while (await reader.ReadAsync(ct))
            {
                var similarity = reader.GetDouble(reader.GetOrdinal("similarity"));
                if (similarity < MinVectorSimilarity) continue;

                var snippetIdx = reader.GetOrdinal("content");
                var snippet = reader.IsDBNull(snippetIdx) ? "" : reader.GetString(snippetIdx);
                if (snippet.Length > 300) snippet = snippet.Substring(0, 300) + "...";

                var valueScoreIdx = reader.GetOrdinal("value_score");
                int? valueScore = reader.IsDBNull(valueScoreIdx) ? null : reader.GetInt32(valueScoreIdx);

                var publishedAtIdx = reader.GetOrdinal("published_at");
                DateTime? publishedAt = reader.IsDBNull(publishedAtIdx) ? null : reader.GetDateTime(publishedAtIdx);

                var createdAtIdx = reader.GetOrdinal("doc_created_at");
                var createdAt = reader.GetDateTime(createdAtIdx);

                var freshnessScore = CalculateFreshnessScore(publishedAt ?? createdAt);

                results.Add(new SearchResultItem
                {
                    DocumentId = ReadGuid(reader, "document_id"),
                    ChunkId = ReadGuid(reader, "chunk_id"),
                    Title = reader.GetString(reader.GetOrdinal("title")),
                    Snippet = snippet,
                    SourceType = reader.IsDBNull(reader.GetOrdinal("source_type")) ? null : reader.GetString(reader.GetOrdinal("source_type")),
                    SourceUrl = reader.IsDBNull(reader.GetOrdinal("source_url")) ? null : reader.GetString(reader.GetOrdinal("source_url")),
                    SourceDomain = reader.IsDBNull(reader.GetOrdinal("source_domain")) ? null : reader.GetString(reader.GetOrdinal("source_domain")),
                    PublishedAt = publishedAt,
                    ValueScore = valueScore,
                    Score = similarity,
                    ScoreDetail = new ScoreDetail
                    {
                        KeywordScore = 0,
                        VectorScore = similarity,
                        FreshnessScore = freshnessScore,
                        ValueScore = valueScore.HasValue ? valueScore.Value / 100.0 : 0
                    }
                });
            }

            return results.Take(limit).ToList();
        }
        finally
        {
            if (shouldClose)
            {
                await connection.CloseAsync();
            }
        }
    }

    private async Task<List<SearchResultItem>> VectorSearchSqliteAsync(
        Guid userId,
        SearchRequest request,
        float[] queryEmbedding,
        int limit,
        CancellationToken ct)
    {
        var candidateQuery =
            from embedding in _db.ChunkEmbeddings
            join chunk in _db.DocumentChunks on embedding.ChunkId equals chunk.Id
            join document in _db.Documents on chunk.DocumentId equals document.Id
            join source in _db.Sources on document.SourceId equals source.Id
            where chunk.UserId == userId
                && embedding.Status == "done"
                && embedding.EmbeddingJson != null
                && (!request.TopicId.HasValue || document.TopicId == request.TopicId)
            select new { embedding, chunk, document, source };

        var filters = request.Filters;
        if (filters != null)
        {
            if (!string.IsNullOrEmpty(filters.SourceType))
                candidateQuery = candidateQuery.Where(x => x.source.SourceType == filters.SourceType);
            if (!string.IsNullOrEmpty(filters.Domain))
                candidateQuery = candidateQuery.Where(x => x.source.Domain == filters.Domain);
            if (filters.DateFrom.HasValue)
                candidateQuery = candidateQuery.Where(x => x.source.PublishedAt >= filters.DateFrom || x.document.CreatedAt >= filters.DateFrom);
            if (filters.DateTo.HasValue)
                candidateQuery = candidateQuery.Where(x => x.source.PublishedAt <= filters.DateTo || x.document.CreatedAt <= filters.DateTo);
            if (filters.MinValueScore.HasValue)
                candidateQuery = candidateQuery.Where(x => x.document.ValueScore >= filters.MinValueScore);
            if (filters.TagIds is { Count: > 0 })
            {
                var documentIds = await _db.DocumentTags
                    .Where(item => filters.TagIds.Contains(item.TagId))
                    .Select(item => item.DocumentId)
                    .Distinct()
                    .ToListAsync(ct);
                candidateQuery = candidateQuery.Where(x => documentIds.Contains(x.document.Id));
            }
            if (filters.EntityIds is { Count: > 0 })
            {
                var documentIds = await _db.DocumentEntities
                    .Where(item => filters.EntityIds.Contains(item.EntityId))
                    .Select(item => item.DocumentId)
                    .Distinct()
                    .ToListAsync(ct);
                candidateQuery = candidateQuery.Where(x => documentIds.Contains(x.document.Id));
            }
        }

        var candidates = await candidateQuery.ToListAsync(ct);
        var results = new List<SearchResultItem>();

        foreach (var candidate in candidates)
        {
            float[]? embedding;
            try
            {
                embedding = JsonSerializer.Deserialize<float[]>(candidate.embedding.EmbeddingJson!);
            }
            catch (JsonException)
            {
                continue;
            }

            if (embedding == null || embedding.Length != queryEmbedding.Length) continue;
            var similarity = CosineSimilarity(queryEmbedding, embedding);
            if (similarity < MinVectorSimilarity) continue;

            var snippet = candidate.chunk.Content.Length > 300
                ? candidate.chunk.Content[..300] + "..."
                : candidate.chunk.Content;
            var freshnessScore = CalculateFreshnessScore(candidate.source.PublishedAt ?? candidate.document.CreatedAt);

            results.Add(new SearchResultItem
            {
                DocumentId = candidate.document.Id,
                ChunkId = candidate.chunk.Id,
                Title = candidate.document.Title,
                Snippet = snippet,
                SourceType = candidate.source.SourceType,
                SourceUrl = candidate.source.Url,
                SourceDomain = candidate.source.Domain,
                PublishedAt = candidate.source.PublishedAt,
                ValueScore = candidate.document.ValueScore,
                Score = similarity,
                MatchChannels = new List<string> { $"vector_{candidate.embedding.EmbeddingType}" },
                ScoreDetail = new ScoreDetail
                {
                    KeywordScore = 0,
                    VectorScore = similarity,
                    FreshnessScore = freshnessScore,
                    ValueScore = candidate.document.ValueScore.HasValue
                        ? candidate.document.ValueScore.Value / 100.0
                        : 0
                }
            });
        }

        return results
            .OrderByDescending(item => item.Score)
            .Take(limit)
            .ToList();
    }

    private static double CosineSimilarity(float[] left, float[] right)
    {
        double dot = 0;
        double leftNorm = 0;
        double rightNorm = 0;
        for (var index = 0; index < left.Length; index++)
        {
            dot += left[index] * right[index];
            leftNorm += left[index] * left[index];
            rightNorm += right[index] * right[index];
        }

        if (leftNorm == 0 || rightNorm == 0) return 0;
        return dot / (Math.Sqrt(leftNorm) * Math.Sqrt(rightNorm));
    }

    private static string BuildVectorSearchSql(Guid? topicId, SearchFilters? filters)
    {
        var whereClauses = new List<string>
        {
            "c.\"UserId\" = @userId",
            "c.\"EmbeddingStatus\" = 'done'"
        };

        if (topicId.HasValue)
        {
            whereClauses.Add("c.\"TopicId\" = @topicId");
        }

        if (filters != null)
        {
            if (!string.IsNullOrEmpty(filters.SourceType))
            {
                whereClauses.Add("s.\"SourceType\" = @sourceType");
            }
            if (filters.DateFrom.HasValue)
            {
                whereClauses.Add("(s.\"PublishedAt\" >= @dateFrom OR d.\"CreatedAt\" >= @dateFrom)");
            }
            if (filters.DateTo.HasValue)
            {
                whereClauses.Add("(s.\"PublishedAt\" <= @dateTo OR d.\"CreatedAt\" <= @dateTo)");
            }
            if (filters.MinValueScore.HasValue)
            {
                whereClauses.Add("d.\"ValueScore\" >= @minValueScore");
            }
            if (!string.IsNullOrEmpty(filters.Domain))
            {
                whereClauses.Add("s.\"Domain\" = @domain");
            }
            if (filters.TagIds != null && filters.TagIds.Count > 0)
            {
                whereClauses.Add("d.\"Id\" IN (SELECT \"DocumentId\" FROM document_tags WHERE \"TagId\" = ANY(@tagIds))");
            }
            if (filters.EntityIds != null && filters.EntityIds.Count > 0)
            {
                whereClauses.Add("d.\"Id\" IN (SELECT \"DocumentId\" FROM document_entities WHERE \"EntityId\" = ANY(@entityIds))");
            }
        }

        var whereSql = string.Join(" AND ", whereClauses);

        return $@"
            SELECT c.id AS chunk_id,
                   c.document_id AS document_id,
                   c.content AS content,
                   d.title AS title,
                   d.value_score AS value_score,
                   d.created_at AS doc_created_at,
                   s.url AS source_url,
                   s.source_type AS source_type,
                   s.domain AS source_domain,
                   s.published_at AS published_at,
                   1 - (c.embedding <=> @queryEmbedding::vector) AS similarity
            FROM document_chunks c
            JOIN documents d ON c.document_id = d.id
            JOIN sources s ON d.source_id = s.id
            WHERE {whereSql}
            ORDER BY c.embedding <=> @queryEmbedding::vector
            LIMIT 50";
    }

    private static Guid ReadGuid(NpgsqlDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        var value = reader.GetValue(ordinal);

        return value switch
        {
            Guid guid => guid,
            string text when Guid.TryParse(text, out var guid) => guid,
            _ => throw new InvalidOperationException($"Column '{name}' did not contain a valid UUID value.")
        };
    }

    private static NpgsqlParameter[] BuildVectorSearchParameters(
        Guid userId,
        Guid? topicId,
        SearchFilters? filters,
        string vectorStr)
    {
        var parameters = new List<NpgsqlParameter>
        {
            new("@userId", NpgsqlDbType.Uuid) { Value = userId },
            new("@queryEmbedding", NpgsqlDbType.Text) { Value = vectorStr }
        };

        if (topicId.HasValue)
        {
            parameters.Add(new NpgsqlParameter("@topicId", NpgsqlDbType.Uuid) { Value = topicId.Value });
        }

        if (filters != null)
        {
            if (!string.IsNullOrEmpty(filters.SourceType))
            {
                parameters.Add(new NpgsqlParameter("@sourceType", NpgsqlDbType.Text) { Value = filters.SourceType });
            }
            if (filters.DateFrom.HasValue)
            {
                parameters.Add(new NpgsqlParameter("@dateFrom", NpgsqlDbType.TimestampTz) { Value = filters.DateFrom.Value });
            }
            if (filters.DateTo.HasValue)
            {
                parameters.Add(new NpgsqlParameter("@dateTo", NpgsqlDbType.TimestampTz) { Value = filters.DateTo.Value });
            }
            if (filters.MinValueScore.HasValue)
            {
                parameters.Add(new NpgsqlParameter("@minValueScore", NpgsqlDbType.Integer) { Value = filters.MinValueScore.Value });
            }
            if (!string.IsNullOrEmpty(filters.Domain))
            {
                parameters.Add(new NpgsqlParameter("@domain", NpgsqlDbType.Text) { Value = filters.Domain });
            }
            if (filters.TagIds != null && filters.TagIds.Count > 0)
            {
                parameters.Add(new NpgsqlParameter("@tagIds", NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = filters.TagIds.ToArray() });
            }
            if (filters.EntityIds != null && filters.EntityIds.Count > 0)
            {
                parameters.Add(new NpgsqlParameter("@entityIds", NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = filters.EntityIds.ToArray() });
            }
        }

        return parameters.ToArray();
    }

    // ===== Hybrid Search =====

    private async Task<List<SearchResultItem>> HybridSearchAsync(
        Guid userId,
        SearchRequest request,
        int limit,
        CancellationToken ct)
    {
        // Execute keyword and vector search sequentially (DbContext is not thread-safe)
        var keywordResults = await KeywordSearchAsync(userId, request, MaxResults, ct);
        List<SearchResultItem> vectorResults;
        try
        {
            vectorResults = await VectorSearchAsync(userId, request, MaxResults, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Vector search unavailable; continuing with keyword results");
            vectorResults = new List<SearchResultItem>();
        }

        List<SearchResultItem> fullTextResults;
        try
        {
            fullTextResults = await FullTextSearchAsync(userId, request, MaxResults, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Chinese FTS5 unavailable; continuing with other retrieval channels");
            fullTextResults = new List<SearchResultItem>();
        }

        // Resolve bilingual metadata and ChunkGroupId before fusion so translated/original
        // representations of the same evidence compete as one logical candidate.
        await EnrichMultilingualMetadataAsync(keywordResults.Concat(vectorResults).Concat(fullTextResults).ToList(), ct);

        if (!string.Equals(request.FusionMode, "linear", StringComparison.OrdinalIgnoreCase))
        {
            var channels = new Dictionary<string, IReadOnlyList<SearchResultItem>>
            {
                ["keyword"] = keywordResults,
                ["fts_zh"] = fullTextResults
            };
            foreach (var group in vectorResults.GroupBy(item =>
                         item.MatchChannels?.FirstOrDefault(x => x.StartsWith("vector_", StringComparison.Ordinal))
                         ?? "vector_original"))
                channels[group.Key] = group.OrderByDescending(x => x.Score).ToList();
            var fused = _fusion.Fuse(channels, limit);
            await EnrichMultilingualMetadataAsync(fused, ct);
            return fused;
        }

        // Merge and deduplicate (by document_id + chunk_id)
        var merged = new Dictionary<string, SearchResultItem>();

        foreach (var item in keywordResults)
        {
            var key = $"{item.DocumentId}:{item.ChunkId}";
            if (!merged.ContainsKey(key))
            {
                merged[key] = item;
            }
        }

        foreach (var item in vectorResults)
        {
            var key = $"{item.DocumentId}:{item.ChunkId}";
            if (merged.TryGetValue(key, out var existing))
            {
                // Merge scores: take the higher keyword score and vector score
                existing.ScoreDetail ??= new ScoreDetail();
                existing.ScoreDetail.VectorScore = Math.Max(existing.ScoreDetail.VectorScore, item.ScoreDetail?.VectorScore ?? 0);
                existing.ScoreDetail.KeywordScore = Math.Max(existing.ScoreDetail.KeywordScore, item.ScoreDetail?.KeywordScore ?? 0);
            }
            else
            {
                merged[key] = item;
            }
        }

        // Calculate final hybrid score
        foreach (var item in merged.Values)
        {
            item.ScoreDetail ??= new ScoreDetail();
            item.Score = CalculateHybridScore(item.ScoreDetail);
        }

        // Batch calculate metadata scores
        var docIds = merged.Values.Select(x => x.DocumentId).Distinct().ToList();
        var docsInfo = await _db.Documents
            .Where(d => docIds.Contains(d.Id))
            .Select(d => new { d.Id, d.TopicId })
            .ToDictionaryAsync(d => d.Id, ct);

        var docsWithTags = await _db.DocumentTags
            .Where(dt => docIds.Contains(dt.DocumentId))
            .Select(dt => dt.DocumentId)
            .Distinct()
            .ToHashSetAsync(ct);

        var docsWithEntities = await _db.DocumentEntities
            .Where(de => docIds.Contains(de.DocumentId))
            .Select(de => de.DocumentId)
            .Distinct()
            .ToHashSetAsync(ct);

        foreach (var item in merged.Values)
        {
            item.ScoreDetail ??= new ScoreDetail();
            double metaScore = 0;
            if (docsInfo.TryGetValue(item.DocumentId, out var docInfo))
            {
                if (docInfo.TopicId.HasValue) metaScore += 0.3;
            }
            if (docsWithTags.Contains(item.DocumentId)) metaScore += 0.4;
            if (docsWithEntities.Contains(item.DocumentId)) metaScore += 0.3;
            item.ScoreDetail.MetadataScore = metaScore;
            // Recalculate final score with metadata
            item.Score = CalculateHybridScore(item.ScoreDetail);
        }

        // Sort by final score descending
        var linearResults = merged.Values
            .OrderByDescending(x => x.Score)
            .Take(limit)
            .ToList();
        await EnrichMultilingualMetadataAsync(linearResults, ct);
        return linearResults;
    }

    private async Task<List<SearchResultItem>> FullTextSearchAsync(Guid userId, SearchRequest request, int limit, CancellationToken ct)
    {
        var hits = await _fullTextIndex.SearchAsync(userId, request.Query, limit * 2, ct);
        if (hits.Count == 0) return new List<SearchResultItem>();
        var docIds = hits.Select(h => h.DocumentId).Distinct().ToList();
        var docs = await _db.Documents.AsNoTracking().Where(d => docIds.Contains(d.Id)).ToDictionaryAsync(d => d.Id, ct);
        var chunkIds = hits.Where(h => h.ChunkId != Guid.Empty).Select(h => h.ChunkId).Distinct().ToList();
        var chunks = await _db.DocumentChunks.AsNoTracking().Where(c => chunkIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, ct);
        var sourceIds = docs.Values.Select(d => d.SourceId).Distinct().ToList();
        var sources = await _db.Sources.AsNoTracking().Where(s => sourceIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, ct);
        var results = new List<SearchResultItem>();
        foreach (var hit in hits)
        {
            if (!docs.TryGetValue(hit.DocumentId, out var doc)) continue;
            if (request.TopicId.HasValue && doc.TopicId != request.TopicId) continue;
            sources.TryGetValue(doc.SourceId, out var source);
            chunks.TryGetValue(hit.ChunkId, out var chunk);
            var original = chunk?.ContentOriginal ?? chunk?.Content ?? doc.Summary ?? doc.ContentText ?? doc.Title;
            results.Add(new SearchResultItem
            {
                DocumentId = doc.Id, ChunkId = chunk?.Id ?? Guid.Empty,
                Title = doc.TitleZh ?? doc.Title, Snippet = TruncateSnippet(doc.SummaryZh ?? original),
                SourceType = source?.SourceType ?? doc.SourceType, SourceUrl = source?.Url ?? doc.SourceUrl,
                SourceDomain = source?.Domain ?? doc.SourceDomain, PublishedAt = source?.PublishedAt ?? doc.PublishedAt,
                ValueScore = doc.ValueScore, Score = hit.Rank,
                ScoreDetail = new ScoreDetail { KeywordScore = hit.Rank, ValueScore = (doc.ValueScore ?? 0) / 100d }
            });
            if (results.Count >= limit) break;
        }
        return results;
    }

    private async Task EnrichMultilingualMetadataAsync(List<SearchResultItem> items, CancellationToken ct)
    {
        if (items.Count == 0) return;
        var docIds = items.Select(i => i.DocumentId).Distinct().ToList();
        var docs = await _db.Documents.AsNoTracking().Where(d => docIds.Contains(d.Id)).ToDictionaryAsync(d => d.Id, ct);
        var chunkIds = items.Where(i => i.ChunkId != Guid.Empty).Select(i => i.ChunkId).Distinct().ToList();
        var chunks = await _db.DocumentChunks.AsNoTracking().Where(c => chunkIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, ct);
        var localizations = await _db.ChunkLocalizations.AsNoTracking()
            .Where(x => chunkIds.Contains(x.ChunkId) && (x.Status == "done" || x.Status == "review_required"))
            .ToDictionaryAsync(x => x.ChunkId, ct);
        foreach (var item in items)
        {
            if (!docs.TryGetValue(item.DocumentId, out var doc)) continue;
            chunks.TryGetValue(item.ChunkId, out var chunk);
            localizations.TryGetValue(item.ChunkId, out var localization);
            var original = chunk?.ContentOriginal ?? chunk?.Content ?? item.Snippet;
            item.TitleOriginal = doc.TitleOriginal ?? doc.Title;
            item.TitleZh = doc.TitleZh;
            item.Title = doc.TitleZh ?? doc.Title;
            item.OriginalSnippet = TruncateSnippet(original);
            item.LocalizedSnippet = !string.IsNullOrWhiteSpace(localization?.ContentLocalized)
                ? TruncateSnippet(localization.ContentLocalized)
                : string.IsNullOrWhiteSpace(doc.SummaryZh) ? null : TruncateSnippet(doc.SummaryZh);
            item.Snippet = item.LocalizedSnippet ?? item.OriginalSnippet;
            item.ContentLanguage = chunk?.DetectedLanguage ?? doc.PrimaryLanguage ?? doc.Language;
            item.DisplayContentSource = localization == null
                ? item.LocalizedSnippet == null ? "original" : "summary"
                : localization.ReviewStatus == "approved" ? "human_reviewed" : "machine_translated";
            item.ChunkGroupId = chunk?.ChunkGroupId;
            item.Section = chunk?.HeadingPath ?? chunk?.ChunkTitle;
            item.PageStart = chunk?.PageStart;
            item.PageEnd = chunk?.PageEnd;
            item.LocalizationId = localization?.Id;
            item.TranslationType = localization?.TranslationType;
            item.ReviewStatus = localization?.ReviewStatus;
        }
    }

    private static string TruncateSnippet(string? value)
    {
        value ??= string.Empty;
        return value.Length > 300 ? value[..300] + "..." : value;
    }

    private static double CalculateHybridScore(ScoreDetail detail)
    {
        // final_score = keyword*0.35 + vector*0.40 + freshness*0.10 + value*0.10 + metadata*0.05
        return detail.KeywordScore * 0.35
             + detail.VectorScore * 0.40
             + detail.FreshnessScore * 0.10
             + detail.ValueScore * 0.10
             + detail.MetadataScore * 0.05;
    }

    private static double CalculateFreshnessScore(DateTime? date)
    {
        if (!date.HasValue) return 0.3;

        var daysOld = (DateTime.UtcNow - date.Value).TotalDays;
        if (daysOld <= 0) return 1.0;
        if (daysOld <= 7) return 0.9;
        if (daysOld <= 30) return 0.7;
        if (daysOld <= 90) return 0.5;
        if (daysOld <= 180) return 0.3;
        return 0.1;
    }

    private async Task<double> CalculateMetadataScoreAsync(Guid documentId, Guid? topicId, CancellationToken ct)
    {
        double score = 0;
        // Has topic: +0.3
        if (topicId.HasValue)
        {
            var hasTopic = await _db.Documents.AnyAsync(d => d.Id == documentId && d.TopicId == topicId, ct);
            if (hasTopic) score += 0.3;
        }
        // Has tags: +0.4
        var hasTags = await _db.DocumentTags.AnyAsync(dt => dt.DocumentId == documentId, ct);
        if (hasTags) score += 0.4;
        // Has entities: +0.3
        var hasEntities = await _db.DocumentEntities.AnyAsync(de => de.DocumentId == documentId, ct);
        if (hasEntities) score += 0.3;
        return score;
    }

    // ===== Search Log =====

    private async Task RecordSearchLogAsync(
        Guid userId,
        SearchRequest request,
        string searchType,
        int resultCount,
        long latencyMs,
        CancellationToken ct)
    {
        try
        {
            var log = new SearchLog
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                TopicId = request.TopicId,
                Query = request.Query,
                SearchType = searchType,
                Filters = request.Filters != null ? JsonSerializer.Serialize(request.Filters) : null,
                ResultCount = resultCount,
                LatencyMs = (int)latencyMs,
                CreatedAt = DateTime.UtcNow
            };

            _db.SearchLogs.Add(log);
            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to record search log");
        }
    }
}
