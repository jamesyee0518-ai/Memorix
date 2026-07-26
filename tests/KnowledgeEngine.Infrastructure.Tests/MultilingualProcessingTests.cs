using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Domain.Entities;
using KnowledgeEngine.Infrastructure.Processing;
using KnowledgeEngine.Infrastructure.Runtime;
using KnowledgeEngine.Infrastructure.Search;
using KnowledgeEngine.Infrastructure.Db;
using KnowledgeEngine.Application.DTOs;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace KnowledgeEngine.Infrastructure.Tests;

public class MultilingualProcessingTests
{
    private readonly LanguageDetectionService _detector = new();
    private readonly ContentClassificationService _classifier = new();

    [Theory]
    [InlineData("这是一个中文资料库，用于检索人工智能文档。", "zh-Hans")]
    [InlineData("這是一個繁體中文資料庫，用於檢索人工智慧文件。", "zh-Hant")]
    [InlineData("This knowledge base contains English research documents.", "en")]
    [InlineData("これは日本語の資料です。人工知能を研究します。", "ja")]
    [InlineData("이 문서는 한국어로 작성된 인공지능 자료입니다.", "ko")]
    public void Detect_ReturnsExpectedPrimaryLanguage(string text, string expected)
    {
        var result = _detector.Detect(text);
        Assert.Equal(expected, result.PrimaryLanguage);
        Assert.True(result.Confidence > 0.5);
    }

    [Fact]
    public void Detect_MixedChineseAndEnglish_MarksMultilingual()
    {
        var result = _detector.Detect("中文知识图谱用于 research memory retrieval and semantic search");
        Assert.True(result.IsMultilingual);
        Assert.True(result.Distribution.ContainsKey("zh"));
        Assert.True(result.Distribution.ContainsKey("en"));
    }

    [Fact]
    public void Classify_CodeAndForeignText_UsesExpectedRoutes()
    {
        var code = _classifier.Classify("```csharp\npublic class Demo {}\n```", _detector.Detect("public class Demo"));
        var english = _classifier.Classify("An English paragraph about retrieval.", _detector.Detect("An English paragraph about retrieval."));
        Assert.Equal("code", code.ContentType);
        Assert.Equal("keep_original", code.ProcessingRoute);
        Assert.Equal("translate", english.ProcessingRoute);
        Assert.True(english.LocalizationRequired);
    }

    [Fact]
    public void Normalize_UsesCompatibleUnicodeAndStableWhitespace()
    {
        var service = new ChineseNormalizationService();
        Assert.Equal("ABC 中文\n\n下一段", service.Normalize("ＡＢＣ\t中文\r\n\r\n\r\n下一段"));
    }

    [Fact]
    public void ChineseTokenizer_EmitsCjkNgramsAndPreservesTechnicalTerms()
    {
        var tokens = new ChineseTokenizer().Tokenize("使用 RAG 构建知识检索", new[] { "知识检索" }).Split(' ');
        Assert.Contains("rag", tokens);
        Assert.Contains("知识", tokens);
        Assert.Contains("知识检索", tokens);
    }

    [Fact]
    public void RetrievalFusion_RanksMultiChannelEvidenceFirstAndNormalizesScore()
    {
        var shared = Guid.NewGuid();
        SearchResultItem Item(Guid id, double score, double keyword = 0, double vector = 0) => new()
        {
            DocumentId = id, ChunkId = Guid.Empty, Score = score,
            ScoreDetail = new ScoreDetail { KeywordScore = keyword, VectorScore = vector }
        };
        var result = new RetrievalFusionService().Fuse(new Dictionary<string, IReadOnlyList<SearchResultItem>>
        {
            ["keyword"] = new[] { Item(shared, 0.6, keyword: 0.6), Item(Guid.NewGuid(), 0.9, keyword: 0.9) },
            ["vector_original"] = new[] { Item(shared, 0.7, vector: 0.7) },
            ["vector_localized"] = new[] { Item(shared, 0.75) },
            ["vector_summary"] = new[] { Item(shared, 0.72) },
            ["vector_hypothetical_question"] = new[] { Item(shared, 0.71) },
            ["fts_zh"] = new[] { Item(shared, 0.8) }
        }, 10);
        Assert.Equal(shared, result[0].DocumentId);
        Assert.Equal(1d, result[0].Score, 6);
        Assert.Equal(6, result[0].MatchChannels.Count);
        Assert.True(result[0].FusionScore > 0);
        var scoreDetail = Assert.IsType<ScoreDetail>(result[0].ScoreDetail);
        Assert.Equal(0.6, scoreDetail.KeywordScore, 6);
        Assert.Equal(0.7, scoreDetail.VectorScore, 6);
    }

    [Fact]
    public void LocalizationQuality_DetectsChangedNumberUnitNegationAndGlossary()
    {
        var service = new LocalizationQualityService();
        var result = service.Validate(
            "GPT-5 does not support 128 GB in this configuration.",
            "该配置支持 GPT-5。",
            new[] { new Terminology { SourceTerm = "configuration", TargetTerm = "配置方案" } });
        Assert.True(result.RequiresReview);
        Assert.True(result.Score < 85);
        Assert.Contains(result.Issues, issue => issue.Contains("128"));
        Assert.Contains(result.Issues, issue => issue.Contains("否定"));
    }

    [Fact]
    public void LocalizationQuality_UsesApprovedAliasesAndIgnoresRejectedTerms()
    {
        var service = new LocalizationQualityService();
        var result = service.Validate(
            "The LLM gateway is enabled.",
            "网关已启用。",
            new[]
            {
                new Terminology
                {
                    SourceTerm = "Large Language Model", TargetTerm = "大语言模型",
                    Aliases = "[\"LLM\"]", ReviewStatus = "approved"
                },
                new Terminology
                {
                    SourceTerm = "gateway", TargetTerm = "错误译法", ReviewStatus = "rejected"
                }
            });

        Assert.Contains(result.Issues, issue => issue.Contains("Large Language Model"));
        Assert.DoesNotContain(result.Issues, issue => issue.Contains("gateway"));
    }

    [Fact]
    public async Task TerminologyService_IsolatesWorkspaceReviewLanguageAndExpandsAliases()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        await using var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var service = new TerminologyService(db);
        var userId = Guid.NewGuid();
        var workspaceA = Guid.NewGuid();
        var workspaceB = Guid.NewGuid();

        await service.UpsertAsync(userId, workspaceA, new Terminology
        {
            SourceLanguage = "en", SourceTerm = "Large Language Model", TargetLanguage = "zh-CN",
            TargetTerm = "大语言模型", Aliases = "LLM,大模型", ReviewStatus = "approved", Priority = 10
        }, false);
        await service.UpsertAsync(userId, workspaceA, new Terminology
        {
            SourceLanguage = "en", SourceTerm = "draft term", TargetLanguage = "zh-CN",
            TargetTerm = "草稿术语", ReviewStatus = "pending"
        }, false);
        await service.UpsertAsync(userId, workspaceB, new Terminology
        {
            SourceLanguage = "en", SourceTerm = "Large Language Model", TargetLanguage = "zh-CN",
            TargetTerm = "另一个工作区译法", ReviewStatus = "approved"
        }, false);

        var selected = await service.SelectForContentAsync(
            userId, workspaceA, "en", "zh-CN", null, "An LLM application");
        var term = Assert.Single(selected);
        Assert.Equal("大语言模型", term.TargetTerm);

        var expanded = await service.ExpandQueryAsync(userId, workspaceA, "LLM 应用", "en", "zh-CN");
        Assert.Contains("Large Language Model", expanded);
        Assert.Contains("大语言模型", expanded);
        Assert.DoesNotContain("另一个工作区译法", expanded);
    }

    [Fact]
    public async Task HeuristicReranker_DeduplicatesChunkGroupsAndLimitsPerDocument()
    {
        var documentId = Guid.NewGuid();
        var group = Guid.NewGuid();
        var candidates = new List<SearchResultItem>
        {
            new() { DocumentId = documentId, ChunkId = Guid.NewGuid(), ChunkGroupId = group, Score = .9, OriginalSnippet = "vector search" },
            new() { DocumentId = documentId, ChunkId = Guid.NewGuid(), ChunkGroupId = group, Score = .8, LocalizedSnippet = "向量检索" },
            new() { DocumentId = documentId, ChunkId = Guid.NewGuid(), ChunkGroupId = Guid.NewGuid(), Score = .7, OriginalSnippet = "other" }
        };
        var result = await new HeuristicRerankerService().RerankAsync("向量检索", candidates, 8, 3);
        Assert.Equal(2, result.Count);
        Assert.Single(result, item => item.ChunkGroupId == group);
    }

    [Fact]
    public void ChunkDocument_PersistsLanguageAndRouteMetadata()
    {
        var service = new ChunkingService(
            NullLogger<ChunkingService>.Instance,
            _detector,
            _classifier,
            new ChineseNormalizationService());
        var document = new Document
        {
            Id = Guid.NewGuid(), SourceId = Guid.NewGuid(), UserId = Guid.NewGuid(),
            Title = "测试", ContentMarkdown = "# 标题\n\n这是中文内容。",
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        };

        var chunks = service.ChunkDocument(document);

        var chunk = Assert.Single(chunks);
        Assert.Equal("zh-Hans", chunk.DetectedLanguage);
        Assert.Equal("zh_direct", chunk.ProcessingRoute);
        Assert.Equal(chunk.Content, chunk.ContentOriginal);
        Assert.NotNull(chunk.ContentNormalized);
        Assert.NotNull(chunk.ChunkGroupId);
    }

    [Fact]
    public async Task SqliteInitializer_CreatesMultilingualColumnsAndBackfillSafeSchema()
    {
        var path = Path.Combine(Path.GetTempPath(), $"memorix-multilingual-{Guid.NewGuid():N}.db");
        try
        {
            var initializer = new SqliteInitializer(NullLogger<SqliteInitializer>.Instance);
            await initializer.InitializeAsync(path);
            await using var connection = new SqliteConnection($"Data Source={path}");
            await connection.OpenAsync();

            var documentColumns = await ReadColumnsAsync(connection, "documents");
            var chunkColumns = await ReadColumnsAsync(connection, "document_chunks");
            Assert.Contains("primary_language", documentColumns);
            Assert.Contains("summary_zh", documentColumns);
            Assert.Contains("localization_model", documentColumns);
            Assert.Contains("localization_quality_score", documentColumns);
            Assert.Contains("language_distribution", documentColumns);
            Assert.Contains("content_original", chunkColumns);
            Assert.Contains("processing_route", chunkColumns);
            Assert.NotEmpty(await ReadColumnsAsync(connection, "terminology"));
            Assert.NotEmpty(await ReadColumnsAsync(connection, "chunk_localizations"));
            Assert.NotEmpty(await ReadColumnsAsync(connection, "chunk_enrichments"));
            Assert.Contains("idx_chunk_localizations_idempotency", await ReadUniqueIndexesAsync(connection, "chunk_localizations"));
            Assert.Contains("idx_chunk_enrichments_chunk_language", await ReadUniqueIndexesAsync(connection, "chunk_enrichments"));
            var embeddingColumns = await ReadColumnsAsync(connection, "chunk_embeddings");
            Assert.Contains("embedding_type", embeddingColumns);
            Assert.Contains("language_code", embeddingColumns);
            Assert.Contains("source_content_hash", embeddingColumns);
            Assert.NotEmpty(await ReadColumnsAsync(connection, "multilingual_batch_jobs"));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
            if (File.Exists(path + "-wal")) File.Delete(path + "-wal");
            if (File.Exists(path + "-shm")) File.Delete(path + "-shm");
        }
    }

    private static async Task<HashSet<string>> ReadColumnsAsync(SqliteConnection connection, string table)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({table})";
        await using var reader = await command.ExecuteReaderAsync();
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (await reader.ReadAsync()) columns.Add(reader.GetString(1));
        return columns;
    }

    private static async Task<HashSet<string>> ReadUniqueIndexesAsync(SqliteConnection connection, string table)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA index_list({table})";
        await using var reader = await command.ExecuteReaderAsync();
        var indexes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (await reader.ReadAsync())
        {
            if (reader.GetInt32(2) == 1) indexes.Add(reader.GetString(1));
        }
        return indexes;
    }
}
