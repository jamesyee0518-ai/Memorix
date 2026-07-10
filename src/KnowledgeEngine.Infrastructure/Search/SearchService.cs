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
    private readonly ILogger<SearchService> _logger;

    public SearchService(
        AppDbContext db,
        IEmbeddingService embeddingService,
        ILogger<SearchService> logger)
    {
        _db = db;
        _embeddingService = embeddingService;
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
                    VectorMatchCount = searchType == "vector" || searchType == "hybrid" ? items.Count : 0
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

    private async Task<List<SearchResultItem>> KeywordSearchAsync(
        Guid userId,
        SearchRequest request,
        int limit,
        CancellationToken ct)
    {
        var query = request.Query.Trim().ToLowerInvariant();
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
                             && c.Content.ToLower().Contains(query)
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
                           && (d.Title.ToLower().Contains(query) ||
                               (d.ContentText != null && d.ContentText.ToLower().Contains(query)))
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

    // ===== Vector Search =====

    private async Task<List<SearchResultItem>> VectorSearchAsync(
        Guid userId,
        SearchRequest request,
        int limit,
        CancellationToken ct)
    {
        // Generate query embedding
        var queryEmbedding = await _embeddingService.EmbedAsync(request.Query, ct);

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
            SELECT c.""Id"" AS chunk_id,
                   c.""DocumentId"" AS document_id,
                   c.""Content"" AS content,
                   d.""Title"" AS title,
                   d.""ValueScore"" AS value_score,
                   d.""CreatedAt"" AS doc_created_at,
                   s.""Url"" AS source_url,
                   s.""SourceType"" AS source_type,
                   s.""Domain"" AS source_domain,
                   s.""PublishedAt"" AS published_at,
                   1 - (c.embedding <=> @queryEmbedding::vector) AS similarity
            FROM document_chunks c
            JOIN documents d ON c.""DocumentId"" = d.""Id""
            JOIN sources s ON d.""SourceId"" = s.""Id""
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
        var vectorResults = await VectorSearchAsync(userId, request, MaxResults, ct);

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
        return merged.Values
            .OrderByDescending(x => x.Score)
            .Take(limit)
            .ToList();
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
