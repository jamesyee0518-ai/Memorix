using KnowledgeEngine.Api.Controllers;
using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Domain.Entities;
using KnowledgeEngine.Infrastructure.Db;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace KnowledgeEngine.Infrastructure.Tests;

public class DocumentOwnershipTests
{
    [Fact]
    public async Task ChunkReadAndReprocess_AreNotFoundForAnotherUser()
    {
        await using var db = await CreateDbAsync();
        var owner = Guid.NewGuid();
        var stranger = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var sourceId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        var chunkId = Guid.NewGuid();
        db.Sources.Add(new Source
        {
            Id = sourceId,
            UserId = owner,
            SourceType = "text",
            ImportedAt = now,
            CreatedAt = now,
            UpdatedAt = now
        });
        db.Documents.Add(new Document
        {
            Id = documentId,
            SourceId = sourceId,
            UserId = owner,
            Title = "private",
            CreatedAt = now,
            UpdatedAt = now
        });
        db.DocumentChunks.Add(new DocumentChunk
        {
            Id = chunkId,
            DocumentId = documentId,
            SourceId = sourceId,
            UserId = owner,
            Content = "secret transcript",
            ContentOriginal = "secret transcript",
            EmbeddingStatus = "done",
            CreatedAt = now,
            UpdatedAt = now
        });
        await db.SaveChangesAsync();

        var controller = Controller(db, stranger);

        var chunks = await controller.GetChunks(documentId, CancellationToken.None);
        var chunk = await controller.GetChunk(chunkId, CancellationToken.None);
        var reembed = await controller.Reembed(null, documentId, CancellationToken.None);
        var retag = await controller.RegenerateTags(null, documentId, CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(chunks);
        Assert.IsType<NotFoundObjectResult>(chunk);
        Assert.IsType<NotFoundObjectResult>(reembed);
        Assert.IsType<NotFoundObjectResult>(retag);
        Assert.Equal("done", (await db.DocumentChunks.SingleAsync()).EmbeddingStatus);
    }

    [Fact]
    public async Task RebuildIndex_ForbiddenForAnotherUsersWorkspace()
    {
        await using var db = await CreateDbAsync();
        var owner = Guid.NewGuid();
        var stranger = Guid.NewGuid();
        var workspaceId = Guid.NewGuid();
        db.Workspaces.Add(new Workspace
        {
            Id = workspaceId,
            Name = "owner",
            UserId = owner,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        var vectors = new RecordingVectorStore();
        var controller = Controller(db, stranger, vectors);

        var result = await controller.RebuildIndex(workspaceId, CancellationToken.None);

        var forbidden = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, forbidden.StatusCode);
        Assert.Equal(0, vectors.Rebuilds);
    }

    [Fact]
    public async Task RebuildIndex_NotFoundWhenWorkspaceDoesNotExist()
    {
        await using var db = await CreateDbAsync();
        var vectors = new RecordingVectorStore();
        var controller = Controller(db, Guid.NewGuid(), vectors);

        var result = await controller.RebuildIndex(Guid.NewGuid(), CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result);
        Assert.Equal(0, vectors.Rebuilds);
    }

    private static DocumentsController Controller(AppDbContext db, Guid userId, IVectorStore? vectors = null) =>
        new(
            null!,
            db,
            vectors ?? new RecordingVectorStore(),
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            new TestUser(userId),
            null!,
            null!,
            null!)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

    private static async Task<AppDbContext> CreateDbAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        return db;
    }

    private sealed class TestUser(Guid userId) : ICurrentUserContext
    {
        public Guid? UserId => userId;
        public string? Email => "user@example.com";
        public bool IsAuthenticated => true;
    }

    private sealed class RecordingVectorStore : IVectorStore
    {
        public int Rebuilds { get; private set; }
        public Task UpsertAsync(string workspaceId, string chunkId, float[] embedding, Dictionary<string, string> metadata, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteAsync(string workspaceId, string chunkId, CancellationToken ct = default) => Task.CompletedTask;
        public Task<List<VectorSearchResult>> SearchAsync(string workspaceId, float[] queryVector, int topK = 10, CancellationToken ct = default) =>
            Task.FromResult(new List<VectorSearchResult>());
        public Task RebuildAsync(string workspaceId, CancellationToken ct = default)
        {
            Rebuilds++;
            return Task.CompletedTask;
        }
        public Task<VectorIndexStats> GetStatsAsync(string workspaceId, CancellationToken ct = default) =>
            Task.FromResult(new VectorIndexStats());
    }
}
