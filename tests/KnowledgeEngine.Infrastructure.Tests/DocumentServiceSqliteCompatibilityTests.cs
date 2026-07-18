using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Application.Services;
using KnowledgeEngine.Domain.Entities;
using KnowledgeEngine.Infrastructure.Db;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace KnowledgeEngine.Infrastructure.Tests;

public class DocumentServiceSqliteCompatibilityTests
{
    [Fact]
    public async Task GetByIdAsync_FindsLowerCaseUuidTextImportedByFetcher()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var now = DateTime.UtcNow;
        var userId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var sourceId = Guid.Parse("3f032aa8-9984-4f7b-bd08-874281e2cab9");
        var documentId = Guid.Parse("5a6bffc0-40b9-5b4e-bd1b-65e1e030c61f");

        db.Users.Add(new User
        {
            Id = userId,
            Email = "local@test.invalid",
            PasswordHash = "test",
            CreatedAt = now,
            UpdatedAt = now
        });
        db.Sources.Add(new Source
        {
            Id = sourceId,
            UserId = userId,
            SourceType = "url",
            ImportedAt = now,
            CreatedAt = now,
            UpdatedAt = now
        });
        db.Documents.Add(new Document
        {
            Id = documentId,
            SourceId = sourceId,
            UserId = userId,
            Title = "Fetcher imported document",
            CreatedAt = now,
            UpdatedAt = now
        });
        await db.SaveChangesAsync();

        // Microsoft.Data.Sqlite writes Guid values as upper-case text by default,
        // while the Python fetcher writes canonical lower-case UUID strings.
        await db.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = OFF");
        await db.Database.ExecuteSqlRawAsync("UPDATE documents SET Id = lower(Id), SourceId = lower(SourceId), UserId = lower(UserId)");
        await db.Database.ExecuteSqlRawAsync("UPDATE sources SET Id = lower(Id), UserId = lower(UserId)");
        await db.Database.ExecuteSqlRawAsync("UPDATE users SET Id = lower(Id)");
        await db.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = ON");
        db.ChangeTracker.Clear();

        var service = new DocumentService(
            db,
            new TestCurrentUserContext(userId),
            NullLogger<DocumentService>.Instance);

        var result = await service.GetByIdAsync(documentId);

        Assert.True(result.Success);
        Assert.Equal(documentId, result.Data?.Id);
        Assert.Equal("Fetcher imported document", result.Data?.Title);
    }

    private sealed class TestCurrentUserContext(Guid userId) : ICurrentUserContext
    {
        public Guid? UserId { get; } = userId;
        public string? Email => "local@test.invalid";
        public bool IsAuthenticated => true;
    }
}
