using KnowledgeEngine.Application.DTOs;
using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Domain.Entities;
using KnowledgeEngine.Domain.Enums;
using KnowledgeEngine.Infrastructure.Agent;
using KnowledgeEngine.Infrastructure.AgentMemory;
using KnowledgeEngine.Infrastructure.Db;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace KnowledgeEngine.Infrastructure.Tests;

/// <summary>
/// Integration tests for Agent Memory infrastructure services using SQLite in-memory database.
/// Tests MemorySanitizer, MemoryAdmissionService, MemoryRetriever, and AgentMemoryService.
/// </summary>
public class AgentMemoryIntegrationTests
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

    private static AgentProfile CreateProfile(Guid userId, bool writeEnabled = true, bool readEnabled = true)
    {
        var now = DateTime.UtcNow;
        return new AgentProfile
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Name = "Test Agent",
            Status = "active",
            MemoryReadEnabled = readEnabled,
            MemoryWriteEnabled = writeEnabled,
            MemoryMaxContextTokens = 2000,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    private static AgentMemoryItem CreateMemoryItem(
        Guid userId,
        Guid workspaceId,
        Guid? sessionId = null,
        Guid? agentProfileId = null,
        AdmissionState state = AdmissionState.Ephemeral,
        MemoryKind kind = MemoryKind.Fact,
        string title = "Test item",
        string content = "Test content",
        MemoryStatus status = MemoryStatus.Active)
    {
        var now = DateTime.UtcNow;
        return new AgentMemoryItem
        {
            Id = Guid.NewGuid(),
            SessionId = sessionId,
            WorkspaceId = workspaceId,
            OwnerUserId = userId,
            AgentProfileId = agentProfileId,
            Kind = kind,
            Title = title,
            Content = content,
            AdmissionState = state,
            Confidence = 0.8m,
            Visibility = Visibility.Agent,
            Importance = 7,
            FreshnessAt = now,
            Status = status,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    private static AgentMemoryEvidence CreateEvidence(Guid itemId, string referenceId = "ref-001")
    {
        return new AgentMemoryEvidence
        {
            Id = Guid.NewGuid(),
            MemoryItemId = itemId,
            EvidenceKind = EvidenceKind.UserInput,
            ReferenceId = referenceId,
            CapturedAt = DateTime.UtcNow
        };
    }

    // -----------------------------------------------------------------------
    // MemorySanitizer - SanitizeOnWriteAsync
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Sanitizer_SanitizeOnWriteAsync_WithOpenAIKey_RedactsAndFlagsModified()
    {
        var sanitizer = new MemorySanitizer(NullLogger<MemorySanitizer>.Instance);
        var content = "The API key is sk-proj-abcdefghijklmnopqrstuvwxyz1234567890";

        var (sanitized, wasModified) = await sanitizer.SanitizeOnWriteAsync(content);

        Assert.True(wasModified);
        Assert.Contains("[REDACTED:API_KEY]", sanitized);
        Assert.DoesNotContain("sk-proj-abcdef", sanitized);
    }

    [Fact]
    public async Task Sanitizer_SanitizeOnWriteAsync_WithBearerToken_RedactsAndFlagsModified()
    {
        var sanitizer = new MemorySanitizer(NullLogger<MemorySanitizer>.Instance);
        var content = "Authorization: Bearer abcdefghijklmnopqrstuvwxyz1234567890";

        var (sanitized, wasModified) = await sanitizer.SanitizeOnWriteAsync(content);

        Assert.True(wasModified);
        Assert.Contains("[REDACTED:BEARER_TOKEN]", sanitized);
    }

    [Fact]
    public async Task Sanitizer_SanitizeOnWriteAsync_WithJWT_RedactsAndFlagsModified()
    {
        var sanitizer = new MemorySanitizer(NullLogger<MemorySanitizer>.Instance);
        var content = "Token: eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxMjM0NTY3ODkwIn0.SflKxwRJSMeKKF2QT4fwpMeJf36POk6yJV_adQssw5c";

        var (sanitized, wasModified) = await sanitizer.SanitizeOnWriteAsync(content);

        Assert.True(wasModified);
        Assert.Contains("[REDACTED:JWT]", sanitized);
    }

    [Fact]
    public async Task Sanitizer_SanitizeOnWriteAsync_WithGitHubToken_RedactsAndFlagsModified()
    {
        var sanitizer = new MemorySanitizer(NullLogger<MemorySanitizer>.Instance);
        var content = "GH token: ghp_1234567890abcdefghijklmnopqrstuvwxyz1234567890";

        var (sanitized, wasModified) = await sanitizer.SanitizeOnWriteAsync(content);

        Assert.True(wasModified);
        Assert.Contains("[REDACTED:GITHUB_TOKEN]", sanitized);
    }

    [Fact]
    public async Task Sanitizer_SanitizeOnWriteAsync_WithPrivateKey_RedactsAndFlagsModified()
    {
        var sanitizer = new MemorySanitizer(NullLogger<MemorySanitizer>.Instance);
        var content = "-----BEGIN RSA PRIVATE KEY-----\nMIIEpAIBAAKCAQEA1234567890\n-----END RSA PRIVATE KEY-----";

        var (sanitized, wasModified) = await sanitizer.SanitizeOnWriteAsync(content);

        Assert.True(wasModified);
        Assert.Contains("[REDACTED:PRIVATE_KEY]", sanitized);
    }

    [Fact]
    public async Task Sanitizer_SanitizeOnWriteAsync_WithAWSAccessKey_RedactsAndFlagsModified()
    {
        var sanitizer = new MemorySanitizer(NullLogger<MemorySanitizer>.Instance);
        var content = "AWS key: AKIAIOSFODNN7EXAMPLE";

        var (sanitized, wasModified) = await sanitizer.SanitizeOnWriteAsync(content);

        Assert.True(wasModified);
        Assert.Contains("[REDACTED:AWS_ACCESS_KEY]", sanitized);
    }

    [Fact]
    public async Task Sanitizer_SanitizeOnWriteAsync_WithCleanContent_ReturnsUnmodified()
    {
        var sanitizer = new MemorySanitizer(NullLogger<MemorySanitizer>.Instance);
        var content = "This is a normal memory about the project architecture.";

        var (sanitized, wasModified) = await sanitizer.SanitizeOnWriteAsync(content);

        Assert.False(wasModified);
        Assert.Equal(content, sanitized);
    }

    [Fact]
    public async Task Sanitizer_SanitizeOnWriteAsync_WithEmptyContent_ReturnsUnmodified()
    {
        var sanitizer = new MemorySanitizer(NullLogger<MemorySanitizer>.Instance);

        var (sanitized, wasModified) = await sanitizer.SanitizeOnWriteAsync("");

        Assert.False(wasModified);
        Assert.Equal("", sanitized);
    }

    // -----------------------------------------------------------------------
    // MemorySanitizer - SanitizeOnReadAsync
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Sanitizer_SanitizeOnReadAsync_WithOpenAIKey_Redacts()
    {
        var sanitizer = new MemorySanitizer(NullLogger<MemorySanitizer>.Instance);
        var content = "The API key is sk-abcdefghijklmnopqrstuvwxyz1234567890";

        var sanitized = await sanitizer.SanitizeOnReadAsync(content);

        Assert.Contains("[REDACTED:API_KEY]", sanitized);
        Assert.DoesNotContain("sk-abcdef", sanitized);
    }

    [Fact]
    public async Task Sanitizer_SanitizeOnReadAsync_WithBearerToken_Redacts()
    {
        var sanitizer = new MemorySanitizer(NullLogger<MemorySanitizer>.Instance);
        var content = "Bearer abcdefghijklmnopqrstuvwxyz1234567890=";

        var sanitized = await sanitizer.SanitizeOnReadAsync(content);

        Assert.Contains("[REDACTED:BEARER_TOKEN]", sanitized);
    }

    [Fact]
    public async Task Sanitizer_SanitizeOnReadAsync_WithCleanContent_ReturnsUnchanged()
    {
        var sanitizer = new MemorySanitizer(NullLogger<MemorySanitizer>.Instance);
        var content = "Normal memory content with no secrets.";

        var sanitized = await sanitizer.SanitizeOnReadAsync(content);

        Assert.Equal(content, sanitized);
    }

    [Fact]
    public async Task Sanitizer_SanitizeOnReadAsync_WithEmptyContent_ReturnsEmpty()
    {
        var sanitizer = new MemorySanitizer(NullLogger<MemorySanitizer>.Instance);

        var sanitized = await sanitizer.SanitizeOnReadAsync("");

        Assert.Equal("", sanitized);
    }

    // -----------------------------------------------------------------------
    // MemoryAdmissionService
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Admission_EvaluateAdmissionAsync_WithEvidence_PromotesToQualified()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await using var db = await CreateDbAsync(connection);
        var service = new MemoryAdmissionService(db, NullLogger<MemoryAdmissionService>.Instance);

        var item = CreateMemoryItem(Guid.NewGuid(), Guid.NewGuid(), state: AdmissionState.Ephemeral);
        var evidence = new List<AgentMemoryEvidence>
        {
            CreateEvidence(item.Id, "ref-001")
        };

        var result = await service.EvaluateAdmissionAsync(item, evidence);

        Assert.Equal(AdmissionState.Qualified, result);
        Assert.Equal(AdmissionState.Qualified, item.AdmissionState);
    }

    [Fact]
    public async Task Admission_EvaluateAdmissionAsync_WithoutEvidence_StaysCandidate()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await using var db = await CreateDbAsync(connection);
        var service = new MemoryAdmissionService(db, NullLogger<MemoryAdmissionService>.Instance);

        var item = CreateMemoryItem(Guid.NewGuid(), Guid.NewGuid(), state: AdmissionState.Ephemeral);

        var result = await service.EvaluateAdmissionAsync(item, new List<AgentMemoryEvidence>());

        Assert.Equal(AdmissionState.Candidate, result);
        Assert.Equal(AdmissionState.Candidate, item.AdmissionState);
    }

    [Fact]
    public async Task Admission_EvaluateAdmissionAsync_WithNullEvidence_StaysCandidate()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await using var db = await CreateDbAsync(connection);
        var service = new MemoryAdmissionService(db, NullLogger<MemoryAdmissionService>.Instance);

        var item = CreateMemoryItem(Guid.NewGuid(), Guid.NewGuid(), state: AdmissionState.Ephemeral);

        var result = await service.EvaluateAdmissionAsync(item, null!);

        Assert.Equal(AdmissionState.Candidate, result);
        Assert.Equal(AdmissionState.Candidate, item.AdmissionState);
    }

    [Fact]
    public async Task Admission_EvaluateAdmissionAsync_WithInaccessibleEvidence_StaysCandidate()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await using var db = await CreateDbAsync(connection);
        var service = new MemoryAdmissionService(db, NullLogger<MemoryAdmissionService>.Instance);

        var item = CreateMemoryItem(Guid.NewGuid(), Guid.NewGuid(), state: AdmissionState.Ephemeral);
        // Evidence with empty ReferenceId is considered inaccessible
        var evidence = new List<AgentMemoryEvidence>
        {
            new AgentMemoryEvidence
            {
                Id = Guid.NewGuid(),
                MemoryItemId = item.Id,
                EvidenceKind = EvidenceKind.UserInput,
                ReferenceId = "",
                CapturedAt = DateTime.UtcNow
            }
        };

        var result = await service.EvaluateAdmissionAsync(item, evidence);

        Assert.Equal(AdmissionState.Candidate, result);
    }

    [Fact]
    public async Task Admission_ConfirmMemoryAsync_OnQualified_TransitionsToConfirmed()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await using var db = await CreateDbAsync(connection);
        var service = new MemoryAdmissionService(db, NullLogger<MemoryAdmissionService>.Instance);

        var item = CreateMemoryItem(Guid.NewGuid(), Guid.NewGuid(), state: AdmissionState.Ephemeral);
        var evidence = new List<AgentMemoryEvidence> { CreateEvidence(item.Id) };
        await service.EvaluateAdmissionAsync(item, evidence);
        Assert.Equal(AdmissionState.Qualified, item.AdmissionState);

        await service.ConfirmMemoryAsync(item);

        Assert.Equal(AdmissionState.Confirmed, item.AdmissionState);
    }

    [Fact]
    public async Task Admission_ConfirmMemoryAsync_OnCandidate_DoesNotConfirm()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await using var db = await CreateDbAsync(connection);
        var service = new MemoryAdmissionService(db, NullLogger<MemoryAdmissionService>.Instance);

        var item = CreateMemoryItem(Guid.NewGuid(), Guid.NewGuid(), state: AdmissionState.Ephemeral);
        await service.EvaluateAdmissionAsync(item, new List<AgentMemoryEvidence>());
        Assert.Equal(AdmissionState.Candidate, item.AdmissionState);

        await service.ConfirmMemoryAsync(item);

        // Should remain Candidate since it was not Qualified
        Assert.Equal(AdmissionState.Candidate, item.AdmissionState);
    }

    [Fact]
    public async Task Admission_RejectMemoryAsync_OnCandidate_TransitionsToRejected()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await using var db = await CreateDbAsync(connection);
        var service = new MemoryAdmissionService(db, NullLogger<MemoryAdmissionService>.Instance);

        var item = CreateMemoryItem(Guid.NewGuid(), Guid.NewGuid(), state: AdmissionState.Ephemeral);
        await service.EvaluateAdmissionAsync(item, new List<AgentMemoryEvidence>());
        Assert.Equal(AdmissionState.Candidate, item.AdmissionState);

        await service.RejectMemoryAsync(item);

        Assert.Equal(AdmissionState.Rejected, item.AdmissionState);
        Assert.Equal(MemoryStatus.Archived, item.Status);
    }

    [Fact]
    public async Task Admission_RejectMemoryAsync_OnQualified_TransitionsToRejected()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await using var db = await CreateDbAsync(connection);
        var service = new MemoryAdmissionService(db, NullLogger<MemoryAdmissionService>.Instance);

        var item = CreateMemoryItem(Guid.NewGuid(), Guid.NewGuid(), state: AdmissionState.Ephemeral);
        var evidence = new List<AgentMemoryEvidence> { CreateEvidence(item.Id) };
        await service.EvaluateAdmissionAsync(item, evidence);
        Assert.Equal(AdmissionState.Qualified, item.AdmissionState);

        await service.RejectMemoryAsync(item);

        Assert.Equal(AdmissionState.Rejected, item.AdmissionState);
        Assert.Equal(MemoryStatus.Archived, item.Status);
    }

    // -----------------------------------------------------------------------
    // MemoryRetriever
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Retriever_SearchAsync_WithMatchingQuery_ReturnsResults()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await using var db = await CreateDbAsync(connection);
        var retriever = new MemoryRetriever(db, NullLogger<MemoryRetriever>.Instance);

        var userId = Guid.NewGuid();
        var workspaceId = Guid.NewGuid();

        db.AgentMemoryItems.Add(CreateMemoryItem(userId, workspaceId,
            title: "Architecture decision", content: "We chose PostgreSQL for persistence"));
        db.AgentMemoryItems.Add(CreateMemoryItem(userId, workspaceId,
            title: "Meeting notes", content: "Discussed the architecture review"));
        await db.SaveChangesAsync();

        var input = new SearchMemoryInput { Query = "architecture", Limit = 10 };
        var results = await retriever.SearchAsync(userId, workspaceId, input);

        Assert.NotEmpty(results);
        Assert.All(results, r => Assert.True(
            r.Title.ToLowerInvariant().Contains("architecture") ||
            (r.Content?.ToLowerInvariant().Contains("architecture") ?? false)));
    }

    [Fact]
    public async Task Retriever_SearchAsync_FiltersByWorkspace()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await using var db = await CreateDbAsync(connection);
        var retriever = new MemoryRetriever(db, NullLogger<MemoryRetriever>.Instance);

        var userId = Guid.NewGuid();
        var workspace1 = Guid.NewGuid();
        var workspace2 = Guid.NewGuid();

        db.AgentMemoryItems.Add(CreateMemoryItem(userId, workspace1,
            title: "WS1 item", content: "workspace one memory"));
        db.AgentMemoryItems.Add(CreateMemoryItem(userId, workspace2,
            title: "WS2 item", content: "workspace two memory"));
        await db.SaveChangesAsync();

        var input = new SearchMemoryInput { Query = "workspace", Limit = 10 };
        var results = await retriever.SearchAsync(userId, workspace1, input);

        Assert.All(results, r => Assert.Equal(workspace1, r.WorkspaceId));
        Assert.DoesNotContain(results, r => r.Title == "WS2 item");
    }

    [Fact]
    public async Task Retriever_SearchAsync_FiltersByUser()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await using var db = await CreateDbAsync(connection);
        var retriever = new MemoryRetriever(db, NullLogger<MemoryRetriever>.Instance);

        var user1 = Guid.NewGuid();
        var user2 = Guid.NewGuid();
        var workspaceId = Guid.NewGuid();

        db.AgentMemoryItems.Add(CreateMemoryItem(user1, workspaceId,
            title: "User1 memory", content: "shared workspace item"));
        db.AgentMemoryItems.Add(CreateMemoryItem(user2, workspaceId,
            title: "User2 memory", content: "shared workspace item"));
        await db.SaveChangesAsync();

        var input = new SearchMemoryInput { Query = "shared", Limit = 10 };
        var results = await retriever.SearchAsync(user1, workspaceId, input);

        Assert.All(results, r => Assert.Equal(user1, r.OwnerUserId));
        Assert.DoesNotContain(results, r => r.Title == "User2 memory");
    }

    [Fact]
    public async Task Retriever_SearchAsync_WithNoQuery_ReturnsRecentItems()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await using var db = await CreateDbAsync(connection);
        var retriever = new MemoryRetriever(db, NullLogger<MemoryRetriever>.Instance);

        var userId = Guid.NewGuid();
        var workspaceId = Guid.NewGuid();

        for (var i = 0; i < 3; i++)
        {
            db.AgentMemoryItems.Add(CreateMemoryItem(userId, workspaceId,
                title: $"Item {i}", content: $"Content {i}"));
        }
        await db.SaveChangesAsync();

        var input = new SearchMemoryInput { Query = "", Limit = 10 };
        var results = await retriever.SearchAsync(userId, workspaceId, input);

        Assert.Equal(3, results.Count);
    }

    [Fact]
    public async Task Retriever_SearchAsync_ExcludesNonActiveItems()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await using var db = await CreateDbAsync(connection);
        var retriever = new MemoryRetriever(db, NullLogger<MemoryRetriever>.Instance);

        var userId = Guid.NewGuid();
        var workspaceId = Guid.NewGuid();

        db.AgentMemoryItems.Add(CreateMemoryItem(userId, workspaceId,
            title: "Active item", content: "active content", status: MemoryStatus.Active));
        db.AgentMemoryItems.Add(CreateMemoryItem(userId, workspaceId,
            title: "Archived item", content: "archived content", status: MemoryStatus.Archived));
        await db.SaveChangesAsync();

        var input = new SearchMemoryInput { Query = "item", Limit = 10 };
        var results = await retriever.SearchAsync(userId, workspaceId, input);

        Assert.Single(results);
        Assert.Equal("Active item", results[0].Title);
    }

    [Fact]
    public async Task Retriever_SearchAsync_WithKindFilter_ReturnsOnlyMatchingKind()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await using var db = await CreateDbAsync(connection);
        var retriever = new MemoryRetriever(db, NullLogger<MemoryRetriever>.Instance);

        var userId = Guid.NewGuid();
        var workspaceId = Guid.NewGuid();

        db.AgentMemoryItems.Add(CreateMemoryItem(userId, workspaceId,
            title: "Decision item", content: "decided to use X", kind: MemoryKind.Decision));
        db.AgentMemoryItems.Add(CreateMemoryItem(userId, workspaceId,
            title: "Fact item", content: "the fact is X", kind: MemoryKind.Fact));
        await db.SaveChangesAsync();

        var input = new SearchMemoryInput { Query = "item", Kind = "Decision", Limit = 10 };
        var results = await retriever.SearchAsync(userId, workspaceId, input);

        Assert.All(results, r => Assert.Equal("decision", r.Kind));
    }

    // -----------------------------------------------------------------------
    // AgentMemoryService
    // -----------------------------------------------------------------------

    [Fact]
    public async Task MemoryService_StartSessionAsync_CreatesSessionInDb()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await using var db = await CreateDbAsync(connection);

        var userId = Guid.NewGuid();
        var workspaceId = Guid.NewGuid();
        var profile = CreateProfile(userId);
        db.AgentProfiles.Add(profile);
        await db.SaveChangesAsync();

        var service = CreateMemoryService(db);
        var session = await service.StartSessionAsync(
            userId, workspaceId, profile.Id, "ext-key-001", "Test task", null);

        Assert.NotEqual(Guid.Empty, session.Id);
        Assert.Equal(userId, session.UserId);
        Assert.Equal(workspaceId, session.WorkspaceId);
        Assert.Equal("active", session.Status);
        Assert.Equal("Test task", session.TaskTitle);

        // Verify it was persisted
        var dbSession = await db.AgentMemorySessions.FirstOrDefaultAsync(s => s.Id == session.Id);
        Assert.NotNull(dbSession);
        Assert.Equal("Test task", dbSession!.TaskTitle);
    }

    [Fact]
    public async Task MemoryService_CaptureMemoryAsync_CreatesItemWithSanitizationAndAdmission()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await using var db = await CreateDbAsync(connection);

        var userId = Guid.NewGuid();
        var workspaceId = Guid.NewGuid();
        var profile = CreateProfile(userId);
        db.AgentProfiles.Add(profile);
        await db.SaveChangesAsync();

        var service = CreateMemoryService(db);

        // Start a session
        var session = await service.StartSessionAsync(
            userId, workspaceId, profile.Id, "ext-key-001", "Test task", null);

        // Capture memory with sensitive content and evidence
        var input = new CaptureMemoryInput
        {
            SessionId = session.Id,
            Kind = "decision",
            Title = "API key decision",
            Content = "Use key sk-abcdefghijklmnopqrstuvwxyz1234567890 for auth",
            Confidence = 0.9m,
            Importance = 8,
            Evidence = new List<EvidenceInput>
            {
                new EvidenceInput
                {
                    EvidenceKind = "user_input",
                    ReferenceId = "msg-001"
                }
            }
        };

        var item = await service.CaptureMemoryAsync(userId, workspaceId, input);

        Assert.NotEqual(Guid.Empty, item.Id);
        Assert.Equal("decision", item.Kind);
        // Content should be sanitized
        Assert.Contains("[REDACTED:API_KEY]", item.Content);
        Assert.DoesNotContain("sk-abcdef", item.Content);
        // With evidence, should be promoted to Qualified
        Assert.Equal("qualified", item.AdmissionState);
        Assert.Single(item.Evidence);

        // Verify it was persisted
        var dbItem = await db.AgentMemoryItems.FirstOrDefaultAsync(i => i.Id == item.Id);
        Assert.NotNull(dbItem);
        Assert.Contains("[REDACTED:API_KEY]", dbItem!.Content);
    }

    [Fact]
    public async Task MemoryService_CaptureMemoryAsync_WithoutEvidence_StaysCandidate()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await using var db = await CreateDbAsync(connection);

        var userId = Guid.NewGuid();
        var workspaceId = Guid.NewGuid();
        var profile = CreateProfile(userId);
        db.AgentProfiles.Add(profile);
        await db.SaveChangesAsync();

        var service = CreateMemoryService(db);

        var session = await service.StartSessionAsync(
            userId, workspaceId, profile.Id, "ext-key-001", "Test task", null);

        var input = new CaptureMemoryInput
        {
            SessionId = session.Id,
            Kind = "fact",
            Title = "A fact without evidence",
            Content = "The sky is blue",
            Importance = 5
        };

        var item = await service.CaptureMemoryAsync(userId, workspaceId, input);

        Assert.Equal("candidate", item.AdmissionState);
    }

    [Fact]
    public async Task MemoryService_SearchMemoryAsync_ReturnsResults()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await using var db = await CreateDbAsync(connection);

        var userId = Guid.NewGuid();
        var workspaceId = Guid.NewGuid();
        var profile = CreateProfile(userId);
        db.AgentProfiles.Add(profile);
        await db.SaveChangesAsync();

        var service = CreateMemoryService(db);

        var session = await service.StartSessionAsync(
            userId, workspaceId, profile.Id, "ext-key-001", "Test task", null);

        await service.CaptureMemoryAsync(userId, workspaceId, new CaptureMemoryInput
        {
            SessionId = session.Id,
            Kind = "decision",
            Title = "Use SQLite for testing",
            Content = "We decided to use SQLite for unit tests",
            Evidence = new List<EvidenceInput> { new() { ReferenceId = "ref-1" } }
        });

        var results = await service.SearchMemoryAsync(userId, workspaceId, new SearchMemoryInput
        {
            Query = "SQLite",
            Limit = 10
        });

        Assert.NotEmpty(results);
        Assert.Contains(results, r => r.Title.Contains("SQLite"));
    }

    [Fact]
    public async Task MemoryService_GetContextAsync_ReturnsContextPack()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await using var db = await CreateDbAsync(connection);

        var userId = Guid.NewGuid();
        var workspaceId = Guid.NewGuid();
        var profile = CreateProfile(userId);
        db.AgentProfiles.Add(profile);
        await db.SaveChangesAsync();

        var service = CreateMemoryService(db);

        var session = await service.StartSessionAsync(
            userId, workspaceId, profile.Id, "ext-key-001", "Test task", null);

        // Capture a confirmed decision for L2
        var item = await service.CaptureMemoryAsync(userId, workspaceId, new CaptureMemoryInput
        {
            SessionId = session.Id,
            Kind = "decision",
            Title = "Architecture decision",
            Content = "Use microservices architecture",
            Evidence = new List<EvidenceInput> { new() { ReferenceId = "ref-1" } }
        });

        // Manually confirm the item for L2 inclusion
        var dbItem = await db.AgentMemoryItems.FirstAsync(i => i.Id == item.Id);
        dbItem.Confirm();
        await db.SaveChangesAsync();

        var context = await service.GetContextAsync(session.Id, maxTokens: 2000);

        Assert.Equal(session.Id, context.SessionId);
        Assert.Equal(2000, context.TokenBudget);
        Assert.True(context.TokenUsed <= context.TokenBudget);
    }

    [Fact]
    public async Task MemoryService_CloseSessionAsync_SetsStatusToClosed()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await using var db = await CreateDbAsync(connection);

        var userId = Guid.NewGuid();
        var workspaceId = Guid.NewGuid();
        var profile = CreateProfile(userId);
        db.AgentProfiles.Add(profile);
        await db.SaveChangesAsync();

        var service = CreateMemoryService(db);

        var session = await service.StartSessionAsync(
            userId, workspaceId, profile.Id, "ext-key-001", "Test task", null);

        await service.CloseSessionAsync(session.Id);

        var dbSession = await db.AgentMemorySessions.FirstAsync(s => s.Id == session.Id);
        Assert.Equal("closed", dbSession.Status);
        Assert.NotNull(dbSession.ClosedAt);
    }

    [Fact]
    public async Task MemoryService_GetMemoryItemAsync_ReturnsItemWithSanitizedContent()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await using var db = await CreateDbAsync(connection);

        var userId = Guid.NewGuid();
        var workspaceId = Guid.NewGuid();
        var profile = CreateProfile(userId);
        db.AgentProfiles.Add(profile);
        await db.SaveChangesAsync();

        var service = CreateMemoryService(db);

        var session = await service.StartSessionAsync(
            userId, workspaceId, profile.Id, "ext-key-001", "Test task", null);

        var captured = await service.CaptureMemoryAsync(userId, workspaceId, new CaptureMemoryInput
        {
            SessionId = session.Id,
            Kind = "fact",
            Title = "Secret fact",
            Content = "Key is sk-abcdefghijklmnopqrstuvwxyz1234567890",
            Evidence = new List<EvidenceInput> { new() { ReferenceId = "ref-1" } }
        });

        // Manually insert unsanitized content to test read sanitization
        var dbItem = await db.AgentMemoryItems.FirstAsync(i => i.Id == captured.Id);
        dbItem.Content = "Raw key sk-abcdefghijklmnopqrstuvwxyz1234567890 here";
        await db.SaveChangesAsync();

        var item = await service.GetMemoryItemAsync(captured.Id);

        Assert.NotNull(item);
        Assert.Contains("[REDACTED:API_KEY]", item!.Content);
    }

    [Fact]
    public async Task MemoryService_ListSessionsAsync_ReturnsUserSessions()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await using var db = await CreateDbAsync(connection);

        var userId = Guid.NewGuid();
        var workspaceId = Guid.NewGuid();
        var profile = CreateProfile(userId);
        db.AgentProfiles.Add(profile);
        await db.SaveChangesAsync();

        var service = CreateMemoryService(db);

        await service.StartSessionAsync(userId, workspaceId, profile.Id, "key-1", "Task 1", null);
        await service.StartSessionAsync(userId, workspaceId, profile.Id, "key-2", "Task 2", null);

        var sessions = await service.ListSessionsAsync(userId, workspaceId);

        Assert.Equal(2, sessions.Count);
    }

    [Fact]
    public async Task MemoryService_GetSessionAsync_ReturnsNullForNonExistent()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await using var db = await CreateDbAsync(connection);

        var service = CreateMemoryService(db);

        var session = await service.GetSessionAsync(Guid.NewGuid());

        Assert.Null(session);
    }

    // -----------------------------------------------------------------------
    // Private helper: create AgentMemoryService with real dependencies
    // -----------------------------------------------------------------------

    private static AgentMemoryService CreateMemoryService(AppDbContext db)
    {
        var sanitizer = new MemorySanitizer(NullLogger<MemorySanitizer>.Instance);
        var admissionService = new MemoryAdmissionService(db, NullLogger<MemoryAdmissionService>.Instance);
        var permissionGuard = new AgentPermissionGuard(db, NullLogger<AgentPermissionGuard>.Instance);
        var retriever = new MemoryRetriever(db, NullLogger<MemoryRetriever>.Instance);
        var contextService = new ContextComposer(db, retriever, NullLogger<ContextComposer>.Instance);

        return new AgentMemoryService(
            db,
            sanitizer,
            admissionService,
            permissionGuard,
            retriever,
            contextService,
            NullLogger<AgentMemoryService>.Instance);
    }
}
