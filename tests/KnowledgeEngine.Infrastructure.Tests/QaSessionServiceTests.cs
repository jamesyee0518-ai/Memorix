using KnowledgeEngine.Application.Settings;
using KnowledgeEngine.Domain.Entities;
using KnowledgeEngine.Infrastructure.Db;
using KnowledgeEngine.Infrastructure.Search;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace KnowledgeEngine.Infrastructure.Tests;

public class QaSessionServiceTests
{
    [Fact]
    public async Task DeleteSessionAsync_RemovesOwnedSessionMessagesAndRetrievalLogs()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        await using var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var userId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var messageId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        db.QaSessions.Add(new QaSession
        {
            Id = sessionId, UserId = userId, Title = "Test conversation", Status = "active",
            CreatedAt = now, UpdatedAt = now
        });
        db.QaMessages.Add(new QaMessage
        {
            Id = messageId, SessionId = sessionId, UserId = userId, Role = "user",
            Content = "Test question", CreatedAt = now
        });
        db.RetrievalLogs.Add(new RetrievalLog
        {
            Id = Guid.NewGuid(), UserId = userId, QaMessageId = messageId,
            Query = "Test question", RetrievalType = "hybrid", CreatedAt = now
        });
        await db.SaveChangesAsync();

        var service = new QaService(
            db,
            null!,
            null!,
            Options.Create(new LlmSettings()),
            null!,
            NullLogger<QaService>.Instance);

        var result = await service.DeleteSessionAsync(userId, sessionId);

        Assert.True(result.Success);
        Assert.Empty(await db.QaSessions.Where(x => x.Id == sessionId).ToListAsync());
        Assert.Empty(await db.QaMessages.Where(x => x.SessionId == sessionId).ToListAsync());
        Assert.Empty(await db.RetrievalLogs.Where(x => x.QaMessageId == messageId).ToListAsync());
    }
}
