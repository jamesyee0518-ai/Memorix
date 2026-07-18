using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Application.Services;
using KnowledgeEngine.Domain.Entities;
using KnowledgeEngine.Infrastructure.Db;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace KnowledgeEngine.Infrastructure.Tests;

public class SourceServiceRetryTests
{
    [Fact]
    public async Task TriggerProcessing_FindsLowerCaseFetcherSourceAndRetriesFailedDocument()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        await using var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var now = DateTime.UtcNow;
        var userId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var sourceId = Guid.Parse("d0b6dd3f-9641-5fbc-a6b5-be8d46b40efa");
        db.Sources.Add(new Source
        {
            Id = sourceId,
            UserId = userId,
            SourceType = "url",
            Url = "https://example.com/article",
            Status = "done",
            ImportedAt = now,
            CreatedAt = now,
            UpdatedAt = now
        });
        db.Documents.Add(new Document
        {
            Id = Guid.NewGuid(),
            SourceId = sourceId,
            UserId = userId,
            Title = "Fetcher imported document",
            SourceUrl = "https://example.com/article",
            AiStatus = "failed",
            CreatedAt = now,
            UpdatedAt = now
        });
        await db.SaveChangesAsync();

        await db.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = OFF");
        await db.Database.ExecuteSqlRawAsync("UPDATE sources SET Id = lower(Id), UserId = lower(UserId)");
        await db.Database.ExecuteSqlRawAsync("UPDATE documents SET Id = lower(Id), SourceId = lower(SourceId), UserId = lower(UserId)");
        await db.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = ON");
        db.ChangeTracker.Clear();

        var service = new SourceService(
            db,
            new TestCurrentUserContext(userId),
            null!,
            NullLogger<SourceService>.Instance);

        var result = await service.TriggerProcessingAsync(sourceId);
        var normalizedSourceId = sourceId.ToString("D");
        var source = await db.Sources.SingleAsync(item =>
            item.Id.ToString().ToLower() == normalizedSourceId);

        Assert.True(result.Success);
        Assert.Equal("queued", source.Status);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TriggerProcessing_RestoresMissingSourceFromExistingDocument(bool hasSourceUrl)
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
        db.Documents.Add(new Document
        {
            Id = documentId,
            SourceId = sourceId,
            UserId = userId,
            Title = "Imported document",
            TitleOriginal = "Original imported title",
            ContentText = "Existing document content used when the original source is unavailable.",
            SourceType = hasSourceUrl ? "url" : "markdown",
            SourceUrl = hasSourceUrl ? "https://example.com/article" : null,
            SourceDomain = hasSourceUrl ? "example.com" : null,
            AiStatus = "failed",
            CreatedAt = now,
            UpdatedAt = now
        });
        await db.SaveChangesAsync();

        var service = new SourceService(
            db,
            new TestCurrentUserContext(userId),
            null!,
            NullLogger<SourceService>.Instance);

        var result = await service.TriggerProcessingAsync(sourceId);
        var restored = await db.Sources.SingleAsync(item => item.Id == sourceId);

        Assert.True(result.Success);
        Assert.Equal("queued", restored.Status);
        Assert.Equal(userId, restored.UserId);
        Assert.Equal("Original imported title", restored.Title);
        Assert.Equal(hasSourceUrl ? "url" : "text", restored.SourceType);
        Assert.Equal(hasSourceUrl ? "https://example.com/article" : null, restored.Url);
        Assert.Equal(hasSourceUrl ? null : "Existing document content used when the original source is unavailable.", restored.RawText);
    }

    private sealed class TestCurrentUserContext(Guid userId) : ICurrentUserContext
    {
        public Guid? UserId { get; } = userId;
        public string? Email => "local@test.invalid";
        public bool IsAuthenticated => true;
    }
}
