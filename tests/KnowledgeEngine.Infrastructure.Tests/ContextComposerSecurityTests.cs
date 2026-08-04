using KnowledgeEngine.Application.DTOs;
using KnowledgeEngine.Domain.Entities;
using KnowledgeEngine.Domain.Enums;
using KnowledgeEngine.Infrastructure.AgentMemory;
using KnowledgeEngine.Infrastructure.Db;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace KnowledgeEngine.Infrastructure.Tests;

/// <summary>
/// Security tests for ContextComposer: sanitization on read and visibility filtering.
/// Verifies that sensitive data is redacted before injection into context packs,
/// and that Private/Agent visibility items are filtered by session ownership.
/// </summary>
public class ContextComposerSecurityTests
{
    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static async Task<AppDbContext> CreateDbAsync(SqliteConnection connection)
    {
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();
        return db;
    }

    private static ContextComposer CreateComposer(AppDbContext db)
    {
        var retriever = new MemoryRetriever(db, NullLogger<MemoryRetriever>.Instance);
        var sanitizer = new MemorySanitizer(NullLogger<MemorySanitizer>.Instance);
        return new ContextComposer(db, retriever, sanitizer, NullLogger<ContextComposer>.Instance);
    }

    private static AgentMemorySession CreateSession(Guid sessionId, Guid userId, Guid workspaceId, Guid? agentProfileId = null)
    {
        var now = DateTime.UtcNow;
        return new AgentMemorySession
        {
            Id = sessionId,
            WorkspaceId = workspaceId,
            UserId = userId,
            AgentProfileId = agentProfileId,
            Status = "active",
            StartedAt = now,
            LastActiveAt = now
        };
    }

    private static AgentMemoryItem CreateItem(
        Guid sessionId,
        Guid workspaceId,
        Guid ownerUserId,
        MemoryKind kind,
        string title,
        string content,
        AdmissionState admissionState = AdmissionState.Confirmed,
        Visibility visibility = Visibility.Workspace,
        Guid? agentProfileId = null)
    {
        var now = DateTime.UtcNow;
        return new AgentMemoryItem
        {
            Id = Guid.NewGuid(),
            SessionId = sessionId,
            WorkspaceId = workspaceId,
            OwnerUserId = ownerUserId,
            AgentProfileId = agentProfileId,
            Kind = kind,
            Title = title,
            Content = content,
            AdmissionState = admissionState,
            Confidence = 0.8m,
            Visibility = visibility,
            Importance = 5,
            Status = MemoryStatus.Active,
            FreshnessAt = now,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    private static AgentMemoryEvidence CreateEvidence(Guid memoryItemId, string referenceId, string? locator = null)
    {
        return new AgentMemoryEvidence
        {
            Id = Guid.NewGuid(),
            MemoryItemId = memoryItemId,
            EvidenceKind = EvidenceKind.DocumentChunk,
            ReferenceId = referenceId,
            Locator = locator,
            CapturedAt = DateTime.UtcNow
        };
    }

    // -----------------------------------------------------------------------
    // Test: Sensitive content in L1 is redacted
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Sanitizer_RedactsApiKeys_InL1Layer()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await using var db = await CreateDbAsync(connection);
        var composer = CreateComposer(db);

        var sessionId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var workspaceId = Guid.NewGuid();

        db.AgentMemorySessions.Add(CreateSession(sessionId, userId, workspaceId));
        db.AgentMemoryItems.Add(CreateItem(
            sessionId, workspaceId, userId,
            MemoryKind.Todo,
            "Todo with secret key",
            "API key is sk-proj-abcdefghijklmnopqrstuvwxyz1234567890ABCD in content",
            admissionState: AdmissionState.Confirmed));
        await db.SaveChangesAsync();

        var context = await composer.BuildContextPackAsync(sessionId, 5000, CancellationToken.None);

        Assert.NotEmpty(context.L1);
        Assert.All(context.L1, layer =>
        {
            Assert.DoesNotContain("sk-proj-abcdef", layer.Content ?? "");
            Assert.DoesNotContain("sk-proj-abcdef", layer.Title ?? "");
        });
        Assert.Contains(context.L1, l => (l.Content ?? "").Contains("[REDACTED:API_KEY]"));
    }

    // -----------------------------------------------------------------------
    // Test: Sensitive content in L2 is redacted
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Sanitizer_RedactsBearerTokens_InL2Layer()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await using var db = await CreateDbAsync(connection);
        var composer = CreateComposer(db);

        var sessionId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var workspaceId = Guid.NewGuid();

        db.AgentMemorySessions.Add(CreateSession(sessionId, userId, workspaceId));
        db.AgentMemoryItems.Add(CreateItem(
            sessionId, workspaceId, userId,
            MemoryKind.Decision,
            "Decision with bearer token",
            "Auth: Bearer abcdefghijklmnopqrstuvwxyz1234567890",
            admissionState: AdmissionState.Confirmed));
        await db.SaveChangesAsync();

        var context = await composer.BuildContextPackAsync(sessionId, 5000, CancellationToken.None);

        Assert.NotEmpty(context.L2);
        Assert.All(context.L2, layer =>
        {
            Assert.DoesNotContain("Bearer abcdef", layer.Content ?? "");
        });
        Assert.Contains(context.L2, l => (l.Content ?? "").Contains("[REDACTED:BEARER_TOKEN]"));
    }

    // -----------------------------------------------------------------------
    // Test: Sensitive content in L3 evidence is redacted
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Sanitizer_RedactsSensitiveData_InL3EvidenceLayer()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await using var db = await CreateDbAsync(connection);
        var composer = CreateComposer(db);

        var sessionId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var workspaceId = Guid.NewGuid();

        db.AgentMemorySessions.Add(CreateSession(sessionId, userId, workspaceId));

        var item = CreateItem(
            sessionId, workspaceId, userId,
            MemoryKind.Fact,
            "Fact with sensitive evidence",
            "Normal fact content",
            admissionState: AdmissionState.Confirmed);
        db.AgentMemoryItems.Add(item);
        await db.SaveChangesAsync();

        // Add evidence with a JWT in the reference ID
        db.AgentMemoryEvidences.Add(CreateEvidence(
            item.Id,
            "eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxMjM0NTY3ODkwIn0.SflKxwRJSMeKKF2QT4fwpMeJf36POk6yJV_adQssw5c"));
        await db.SaveChangesAsync();

        var context = await composer.BuildContextPackAsync(sessionId, 5000, CancellationToken.None);

        Assert.NotEmpty(context.L3);
        Assert.All(context.L3, layer =>
        {
            Assert.DoesNotContain("eyJhbGci", layer.Content ?? "");
            Assert.DoesNotContain("eyJhbGci", layer.EvidenceRef ?? "");
        });
    }

    // -----------------------------------------------------------------------
    // Test: Sensitive content in Title is also redacted
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Sanitizer_RedactsSensitiveData_InTitle()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await using var db = await CreateDbAsync(connection);
        var composer = CreateComposer(db);

        var sessionId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var workspaceId = Guid.NewGuid();

        db.AgentMemorySessions.Add(CreateSession(sessionId, userId, workspaceId));
        db.AgentMemoryItems.Add(CreateItem(
            sessionId, workspaceId, userId,
            MemoryKind.Todo,
            "Token: ghp_1234567890abcdefghijklmnopqrstuvwxyz1234567890",
            "Normal content without secrets",
            admissionState: AdmissionState.Confirmed));
        await db.SaveChangesAsync();

        var context = await composer.BuildContextPackAsync(sessionId, 5000, CancellationToken.None);

        Assert.NotEmpty(context.L1);
        Assert.All(context.L1, layer =>
        {
            Assert.DoesNotContain("ghp_", layer.Title ?? "");
            Assert.Contains("[REDACTED:GITHUB_TOKEN]", layer.Title ?? "");
        });
    }

    // -----------------------------------------------------------------------
    // Test: Normal (non-sensitive) content passes through unchanged
    // -----------------------------------------------------------------------

    [Fact]
    public async Task NormalContent_PassesThrough_Unchanged()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await using var db = await CreateDbAsync(connection);
        var composer = CreateComposer(db);

        var sessionId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var workspaceId = Guid.NewGuid();
        var normalContent = "This is a normal decision about using PostgreSQL for the database.";
        var normalTitle = "Database decision";

        db.AgentMemorySessions.Add(CreateSession(sessionId, userId, workspaceId));
        db.AgentMemoryItems.Add(CreateItem(
            sessionId, workspaceId, userId,
            MemoryKind.Decision,
            normalTitle,
            normalContent,
            admissionState: AdmissionState.Confirmed));
        await db.SaveChangesAsync();

        var context = await composer.BuildContextPackAsync(sessionId, 5000, CancellationToken.None);

        Assert.NotEmpty(context.L2);
        var l2Item = Assert.Single(context.L2);
        Assert.Equal(normalTitle, l2Item.Title);
        Assert.Equal(normalContent, l2Item.Content);
        Assert.DoesNotContain("[REDACTED", l2Item.Content);
    }

    // -----------------------------------------------------------------------
    // Test: Private items excluded when session user is not the owner
    // -----------------------------------------------------------------------

    [Fact]
    public async Task VisibilityFilter_ExcludesPrivateItems_FromNonOwnerSession()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await using var db = await CreateDbAsync(connection);
        var composer = CreateComposer(db);

        var sessionId = Guid.NewGuid();
        var sessionUserId = Guid.NewGuid(); // The user who owns the session
        var otherUserId = Guid.NewGuid();   // A different user who owns the private memory
        var workspaceId = Guid.NewGuid();

        db.AgentMemorySessions.Add(CreateSession(sessionId, sessionUserId, workspaceId));

        // Private item owned by a different user — should be excluded
        db.AgentMemoryItems.Add(CreateItem(
            sessionId, workspaceId, otherUserId,
            MemoryKind.Decision,
            "Private decision by other user",
            "This is a private decision that should not be visible",
            admissionState: AdmissionState.Confirmed,
            visibility: Visibility.Private));

        // Workspace item owned by the same user — should be included
        db.AgentMemoryItems.Add(CreateItem(
            sessionId, workspaceId, sessionUserId,
            MemoryKind.Decision,
            "Workspace decision",
            "This is a workspace-visible decision",
            admissionState: AdmissionState.Confirmed,
            visibility: Visibility.Workspace));

        await db.SaveChangesAsync();

        var context = await composer.BuildContextPackAsync(sessionId, 5000, CancellationToken.None);

        // L2 should only contain the workspace item
        Assert.Single(context.L2);
        Assert.DoesNotContain(context.L2, l => l.Title == "Private decision by other user");
        Assert.Contains(context.L2, l => l.Title == "Workspace decision");
    }

    // -----------------------------------------------------------------------
    // Test: Private items included when session user is the owner
    // -----------------------------------------------------------------------

    [Fact]
    public async Task VisibilityFilter_IncludesPrivateItems_ForOwnerSession()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await using var db = await CreateDbAsync(connection);
        var composer = CreateComposer(db);

        var sessionId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var workspaceId = Guid.NewGuid();

        db.AgentMemorySessions.Add(CreateSession(sessionId, userId, workspaceId));

        // Private item owned by the session user — should be included
        db.AgentMemoryItems.Add(CreateItem(
            sessionId, workspaceId, userId,
            MemoryKind.Decision,
            "My private decision",
            "This is my private decision",
            admissionState: AdmissionState.Confirmed,
            visibility: Visibility.Private));

        await db.SaveChangesAsync();

        var context = await composer.BuildContextPackAsync(sessionId, 5000, CancellationToken.None);

        Assert.NotEmpty(context.L2);
        Assert.Contains(context.L2, l => l.Title == "My private decision");
    }

    // -----------------------------------------------------------------------
    // Test: Agent items excluded when session agent profile doesn't match
    // -----------------------------------------------------------------------

    [Fact]
    public async Task VisibilityFilter_ExcludesAgentItems_FromDifferentAgentProfile()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await using var db = await CreateDbAsync(connection);
        var composer = CreateComposer(db);

        var sessionId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var workspaceId = Guid.NewGuid();
        var sessionAgentId = Guid.NewGuid();
        var otherAgentId = Guid.NewGuid();

        db.AgentMemorySessions.Add(CreateSession(sessionId, userId, workspaceId, sessionAgentId));

        // Agent item owned by a different agent profile — should be excluded
        db.AgentMemoryItems.Add(CreateItem(
            sessionId, workspaceId, userId,
            MemoryKind.Todo,
            "Todo from different agent",
            "This todo belongs to a different agent profile",
            admissionState: AdmissionState.Confirmed,
            visibility: Visibility.Agent,
            agentProfileId: otherAgentId));

        // Agent item owned by the same agent profile — should be included
        db.AgentMemoryItems.Add(CreateItem(
            sessionId, workspaceId, userId,
            MemoryKind.Todo,
            "Todo from same agent",
            "This todo belongs to the session's agent profile",
            admissionState: AdmissionState.Confirmed,
            visibility: Visibility.Agent,
            agentProfileId: sessionAgentId));

        await db.SaveChangesAsync();

        var context = await composer.BuildContextPackAsync(sessionId, 5000, CancellationToken.None);

        // L1 should only contain the matching agent item
        Assert.NotEmpty(context.L1);
        Assert.DoesNotContain(context.L1, l => l.Title == "Todo from different agent");
        Assert.Contains(context.L1, l => l.Title == "Todo from same agent");
    }

    // -----------------------------------------------------------------------
    // Test: Agent items included when session user is the owner
    // -----------------------------------------------------------------------

    [Fact]
    public async Task VisibilityFilter_IncludesAgentItems_WhenOwnerIsSessionUser()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await using var db = await CreateDbAsync(connection);
        var composer = CreateComposer(db);

        var sessionId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var workspaceId = Guid.NewGuid();
        var otherAgentId = Guid.NewGuid();

        db.AgentMemorySessions.Add(CreateSession(sessionId, userId, workspaceId, agentProfileId: null));

        // Agent item owned by the session user (even with a different agent profile) — should be included
        db.AgentMemoryItems.Add(CreateItem(
            sessionId, workspaceId, userId,
            MemoryKind.Todo,
            "Agent todo owned by session user",
            "This agent todo is owned by the session user",
            admissionState: AdmissionState.Confirmed,
            visibility: Visibility.Agent,
            agentProfileId: otherAgentId));

        await db.SaveChangesAsync();

        var context = await composer.BuildContextPackAsync(sessionId, 5000, CancellationToken.None);

        Assert.NotEmpty(context.L1);
        Assert.Contains(context.L1, l => l.Title == "Agent todo owned by session user");
    }

    // -----------------------------------------------------------------------
    // Test: Multiple secret types redacted simultaneously
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Sanitizer_RedactsMultipleSecretTypes_Simultaneously()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await using var db = await CreateDbAsync(connection);
        var composer = CreateComposer(db);

        var sessionId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var workspaceId = Guid.NewGuid();

        db.AgentMemorySessions.Add(CreateSession(sessionId, userId, workspaceId));
        db.AgentMemoryItems.Add(CreateItem(
            sessionId, workspaceId, userId,
            MemoryKind.Fact,
            "Fact with multiple secrets",
            "Keys: sk-proj-abcdefghijklmnopqrstuvwxyz1234567890ABCD and " +
            "ghp_1234567890abcdefghijklmnopqrstuvwxyz1234567890 and " +
            "AKIAIOSFODNN7EXAMPLE",
            admissionState: AdmissionState.Confirmed));
        await db.SaveChangesAsync();

        var context = await composer.BuildContextPackAsync(sessionId, 5000, CancellationToken.None);

        Assert.NotEmpty(context.L2);
        var content = context.L2.First().Content ?? "";
        Assert.Contains("[REDACTED:API_KEY]", content);
        Assert.Contains("[REDACTED:GITHUB_TOKEN]", content);
        Assert.Contains("[REDACTED:AWS_ACCESS_KEY]", content);
        Assert.DoesNotContain("sk-proj-abcdef", content);
        Assert.DoesNotContain("ghp_", content);
        Assert.DoesNotContain("AKIA", content);
    }

    // -----------------------------------------------------------------------
    // Test: Private key PEM blocks are redacted
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Sanitizer_RedactsPrivateKeyPem_InContext()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await using var db = await CreateDbAsync(connection);
        var composer = CreateComposer(db);

        var sessionId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var workspaceId = Guid.NewGuid();

        db.AgentMemorySessions.Add(CreateSession(sessionId, userId, workspaceId));
        db.AgentMemoryItems.Add(CreateItem(
            sessionId, workspaceId, userId,
            MemoryKind.Fact,
            "Server configuration fact",
            "Config:\n-----BEGIN RSA PRIVATE KEY-----\nMIIEpAIBAAKCAQEA1234567890\n-----END RSA PRIVATE KEY-----\nend",
            admissionState: AdmissionState.Confirmed));
        await db.SaveChangesAsync();

        var context = await composer.BuildContextPackAsync(sessionId, 5000, CancellationToken.None);

        Assert.NotEmpty(context.L2);
        var content = context.L2.First().Content ?? "";
        Assert.Contains("[REDACTED:PRIVATE_KEY]", content);
        Assert.DoesNotContain("MIIEpAIBAAKCAQEA", content);
    }
}
