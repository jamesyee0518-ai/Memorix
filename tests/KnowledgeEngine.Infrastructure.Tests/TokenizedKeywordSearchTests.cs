using KnowledgeEngine.Application.DTOs;
using KnowledgeEngine.Domain.Entities;
using KnowledgeEngine.Infrastructure.Db;
using KnowledgeEngine.Infrastructure.Processing;
using KnowledgeEngine.Infrastructure.Search;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using KnowledgeEngine.Application.Settings;
using Xunit;

namespace KnowledgeEngine.Infrastructure.Tests;

public class TokenizedKeywordSearchTests
{
    [Fact]
    public async Task KeywordSearch_MatchesMeaningfulTermsInsteadOfRequiringWholeQuestion()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        await using var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var userId = Guid.NewGuid();
        var sourceId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        db.Sources.Add(new Source
        {
            Id = sourceId, UserId = userId, SourceType = "url", Status = "done",
            ImportedAt = now, CreatedAt = now, UpdatedAt = now
        });
        db.Documents.Add(new Document
        {
            Id = documentId, SourceId = sourceId, UserId = userId,
            Title = "GPT-5.6 发布信息", AiStatus = "done", CreatedAt = now, UpdatedAt = now
        });
        db.DocumentChunks.Add(new DocumentChunk
        {
            Id = Guid.NewGuid(), DocumentId = documentId, SourceId = sourceId, UserId = userId,
            ChunkIndex = 0, Content = "OpenAI 已经正式发布 GPT-5.6 大型语言模型。",
            ContentOriginal = "OpenAI 已经正式发布 GPT-5.6 大型语言模型。",
            CreatedAt = now, UpdatedAt = now
        });
        await db.SaveChangesAsync();

        var service = new SearchService(
            db, null!, null!, new RetrievalFusionService(), new ChineseTokenizer(),
            new EntityQueryExpansionService(
                db, new EntityRedirectResolver(db), new ChineseTokenizer(),
                Options.Create(new EntityResolutionSettings())),
            NullLogger<SearchService>.Instance);
        var result = await service.SearchAsync(userId, new SearchRequest
        {
            Query = "请问 GPT-5.6 是在什么时候发布的？",
            SearchType = "keyword",
            Limit = 20
        });

        Assert.True(result.Success);
        Assert.Contains(result.Data!.Items, item => item.DocumentId == documentId);
        Assert.Contains(result.Data.Items, item => item.ScoreDetail!.KeywordScore > 0);

        var entityId = Guid.NewGuid();
        var workspaceId = Guid.NewGuid().ToString();
        db.Entities.Add(new Entity
        {
            Id = entityId,
            UserId = userId,
            WorkspaceId = workspaceId,
            Name = "大型语言模型",
            CanonicalName = "大型语言模型",
            PreferredNameZh = "大型语言模型",
            PreferredNameEn = "Large Language Model",
            Abbreviation = "LLM",
            NormalizedName = "大型语言模型",
            NormalizedKey = "大型语言模型",
            EntityType = "TECHNOLOGY",
            Status = "active",
            CreatedAt = now,
            UpdatedAt = now
        });
        db.EntityAliases.Add(new EntityAlias
        {
            Id = Guid.NewGuid(),
            EntityId = entityId,
            UserId = userId,
            WorkspaceId = workspaceId,
            Alias = "LLM",
            NormalizedAlias = "llm",
            AliasType = "ABBREVIATION",
            SourceType = "manual",
            IsVerified = true,
            CreatedAt = now,
            UpdatedAt = now
        });
        db.DocumentEntities.Add(new DocumentEntity
        {
            DocumentId = documentId,
            EntityId = entityId,
            MentionCount = 1,
            CreatedAt = now
        });
        await db.SaveChangesAsync();

        var aliasResult = await service.SearchAsync(userId, new SearchRequest
        {
            Query = "LLM 有哪些最新进展？",
            SearchType = "keyword",
            Limit = 20
        });
        Assert.True(aliasResult.Success);
        Assert.Contains(aliasResult.Data!.Items, item => item.DocumentId == documentId);
        Assert.Contains(entityId, aliasResult.Data.DebugInfo!.RecognizedEntityIds);
    }
}
