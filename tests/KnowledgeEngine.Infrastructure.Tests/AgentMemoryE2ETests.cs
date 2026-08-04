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
/// End-to-end tests for the Agent Memory Phase 2 features:
/// - Session recovery via checkpoints
/// - Handoff via delivered checkpoints
/// - User review (confirm) flow
/// - User delete (forget) flow
/// </summary>
public class AgentMemoryE2ETests
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

    private static (AgentMemoryService memoryService, MemoryAdmissionService admissionService,
        CheckpointService checkpointService, RetentionService retentionService)
        CreateServices(AppDbContext db)
    {
        var sanitizer = new MemorySanitizer(NullLogger<MemorySanitizer>.Instance);
        var admissionService = new MemoryAdmissionService(db, NullLogger<MemoryAdmissionService>.Instance);
        var permissionGuard = new AgentPermissionGuard(db, NullLogger<AgentPermissionGuard>.Instance);
        var retriever = new MemoryRetriever(db, NullLogger<MemoryRetriever>.Instance);
        var contextService = new ContextComposer(db, retriever, sanitizer, NullLogger<ContextComposer>.Instance);
        var memoryService = new AgentMemoryService(
            db,
            sanitizer,
            admissionService,
            permissionGuard,
            retriever,
            contextService,
            NullLogger<AgentMemoryService>.Instance);
        var checkpointService = new CheckpointService(db, NullLogger<CheckpointService>.Instance);
        var retentionService = new RetentionService(db, NullLogger<RetentionService>.Instance);

        return (memoryService, admissionService, checkpointService, retentionService);
    }

    // -----------------------------------------------------------------------
    // Test 1: Session Recovery via Checkpoint
    // -----------------------------------------------------------------------

    [Fact]
    public async Task E2E_SessionRecovery_StartCaptureCheckpointClose_RestoresFromCheckpoint()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await using var db = await CreateDbAsync(connection);

        var userId = Guid.NewGuid();
        var workspaceId = Guid.NewGuid();
        var profile = CreateProfile(userId);
        db.AgentProfiles.Add(profile);
        await db.SaveChangesAsync();

        var (memoryService, _, checkpointService, _) = CreateServices(db);

        // Step 1: Start a session
        var session = await memoryService.StartSessionAsync(
            userId, workspaceId, profile.Id, "ext-key-recovery", "Recovery test task", null);
        Assert.Equal("active", session.Status);

        // Step 2: Capture memories with evidence
        var item1 = await memoryService.CaptureMemoryAsync(userId, workspaceId, new CaptureMemoryInput
        {
            SessionId = session.Id,
            Kind = "decision",
            Title = "Use PostgreSQL for persistence",
            Content = "We decided to use PostgreSQL as the primary database.",
            Confidence = 0.9m,
            Importance = 9,
            Evidence = new List<EvidenceInput> { new() { ReferenceId = "ref-decision-1" } }
        });

        var item2 = await memoryService.CaptureMemoryAsync(userId, workspaceId, new CaptureMemoryInput
        {
            SessionId = session.Id,
            Kind = "blocker",
            Title = "API rate limit issue",
            Content = "The external API has a rate limit of 100 requests per minute.",
            Importance = 7,
            Evidence = new List<EvidenceInput> { new() { ReferenceId = "ref-blocker-1" } }
        });

        Assert.NotEqual(Guid.Empty, item1.Id);
        Assert.NotEqual(Guid.Empty, item2.Id);

        // Step 3: Create a checkpoint
        var checkpoint = await checkpointService.CreateCheckpointAsync(session.Id);
        Assert.NotEqual(Guid.Empty, checkpoint.Id);
        Assert.Equal(session.Id, checkpoint.SessionId);
        Assert.Equal("pending", checkpoint.DeliveryState);
        Assert.NotNull(checkpoint.Summary);
        Assert.Contains("Use PostgreSQL for persistence", checkpoint.Summary);

        // Step 4: Close the session
        await memoryService.CloseSessionAsync(session.Id);
        var closedSession = await memoryService.GetSessionAsync(session.Id);
        Assert.NotNull(closedSession);
        Assert.Equal("closed", closedSession!.Status);

        // Step 5: Mark checkpoint as delivered and restore from it
        await checkpointService.MarkDeliveredAsync(checkpoint.Id);

        var restoredCheckpoint = await checkpointService.RestoreFromCheckpointAsync(session.Id);
        Assert.NotNull(restoredCheckpoint);
        Assert.Equal(checkpoint.Id, restoredCheckpoint!.Id);
        Assert.Equal("delivered", restoredCheckpoint.DeliveryState);
        Assert.NotNull(restoredCheckpoint.Summary);

        // Verify the checkpoint contains the session's memory content
        Assert.Contains("PostgreSQL", restoredCheckpoint.Summary!);
        Assert.Contains("rate limit", restoredCheckpoint.Summary!);
    }

    // -----------------------------------------------------------------------
    // Test 2: Handoff via Delivered Checkpoint
    // -----------------------------------------------------------------------

    [Fact]
    public async Task E2E_Handoff_CreateCheckpointMarkDelivered_RestoresForHandoff()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await using var db = await CreateDbAsync(connection);

        var userId = Guid.NewGuid();
        var workspaceId = Guid.NewGuid();
        var profile = CreateProfile(userId);
        db.AgentProfiles.Add(profile);
        await db.SaveChangesAsync();

        var (memoryService, _, checkpointService, _) = CreateServices(db);

        // Start session and capture memories
        var session = await memoryService.StartSessionAsync(
            userId, workspaceId, profile.Id, "ext-key-handoff", "Handoff test task", null);

        await memoryService.CaptureMemoryAsync(userId, workspaceId, new CaptureMemoryInput
        {
            SessionId = session.Id,
            Kind = "decision",
            Title = "Adopt microservices architecture",
            Content = "We will migrate to a microservices architecture.",
            Confidence = 0.85m,
            Importance = 8,
            Evidence = new List<EvidenceInput> { new() { ReferenceId = "ref-handoff-1" } }
        });

        await memoryService.CaptureMemoryAsync(userId, workspaceId, new CaptureMemoryInput
        {
            SessionId = session.Id,
            Kind = "todo",
            Title = "Set up CI/CD pipeline",
            Content = "Need to configure the CI/CD pipeline for the new architecture.",
            Importance = 6,
            Evidence = new List<EvidenceInput> { new() { ReferenceId = "ref-handoff-2" } }
        });

        // Create checkpoint
        var checkpoint = await checkpointService.CreateCheckpointAsync(session.Id);
        Assert.Equal("pending", checkpoint.DeliveryState);

        // Initially, no delivered checkpoint exists
        var noDelivered = await checkpointService.GetLatestDeliveredCheckpointAsync(session.Id);
        Assert.Null(noDelivered);

        // Mark as delivered
        await checkpointService.MarkDeliveredAsync(checkpoint.Id);

        // Now a delivered checkpoint exists
        var delivered = await checkpointService.GetLatestDeliveredCheckpointAsync(session.Id);
        Assert.NotNull(delivered);
        Assert.Equal(checkpoint.Id, delivered!.Id);
        Assert.Equal("delivered", delivered.DeliveryState);

        // Restore from checkpoint for handoff
        var handoffCheckpoint = await checkpointService.RestoreFromCheckpointAsync(session.Id);
        Assert.NotNull(handoffCheckpoint);
        Assert.NotNull(handoffCheckpoint!.Summary);
        Assert.Contains("microservices", handoffCheckpoint.Summary!);
        Assert.Contains("CI/CD", handoffCheckpoint.Summary!);

        // Verify open loops JSON contains the todo item
        Assert.NotNull(handoffCheckpoint.OpenLoopsJson);
        Assert.Contains("Set up CI/CD pipeline", handoffCheckpoint.OpenLoopsJson!);

        // Verify decisions JSON contains the decision
        Assert.NotNull(handoffCheckpoint.DecisionsJson);
        Assert.Contains("Adopt microservices architecture", handoffCheckpoint.DecisionsJson!);
    }

    // -----------------------------------------------------------------------
    // Test 3: User Review (Confirm) Flow
    // -----------------------------------------------------------------------

    [Fact]
    public async Task E2E_UserReview_CaptureConfirmItem_StateIsConfirmed()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await using var db = await CreateDbAsync(connection);

        var userId = Guid.NewGuid();
        var workspaceId = Guid.NewGuid();
        var profile = CreateProfile(userId);
        db.AgentProfiles.Add(profile);
        await db.SaveChangesAsync();

        var (memoryService, admissionService, _, _) = CreateServices(db);

        // Start session and capture an item with evidence
        var session = await memoryService.StartSessionAsync(
            userId, workspaceId, profile.Id, "ext-key-confirm", "Confirm test task", null);

        var captured = await memoryService.CaptureMemoryAsync(userId, workspaceId, new CaptureMemoryInput
        {
            SessionId = session.Id,
            Kind = "decision",
            Title = "Use Redis for caching",
            Content = "We will use Redis as our caching layer.",
            Confidence = 0.8m,
            Importance = 7,
            Evidence = new List<EvidenceInput> { new() { ReferenceId = "ref-confirm-1" } }
        });

        // With evidence, the item should be Qualified (not Confirmed yet)
        Assert.Equal("qualified", captured.AdmissionState);

        // Create a feedback record for the confirmation
        var feedback = new AgentMemoryFeedback
        {
            Id = Guid.NewGuid(),
            MemoryItemId = captured.Id,
            UserId = userId,
            Action = "confirm",
            Note = "Approved by user",
            CreatedAt = DateTime.UtcNow
        };
        db.AgentMemoryFeedbacks.Add(feedback);

        // Load the item from DB and confirm it
        var dbItem = await db.AgentMemoryItems.FirstAsync(i => i.Id == captured.Id);
        await admissionService.ConfirmMemoryAsync(dbItem);
        await db.SaveChangesAsync();

        // Verify the state is now Confirmed
        Assert.Equal(AdmissionState.Confirmed, dbItem.AdmissionState);

        // Reload via the service and verify
        var reloaded = await memoryService.GetMemoryItemAsync(captured.Id);
        Assert.NotNull(reloaded);
        Assert.Equal("confirmed", reloaded!.AdmissionState);

        // Verify the feedback was persisted
        var dbFeedback = await db.AgentMemoryFeedbacks
            .FirstOrDefaultAsync(f => f.MemoryItemId == captured.Id && f.Action == "confirm");
        Assert.NotNull(dbFeedback);
        Assert.Equal("Approved by user", dbFeedback!.Note);
    }

    // -----------------------------------------------------------------------
    // Test 4: User Delete (Forget) Flow
    // -----------------------------------------------------------------------

    [Fact]
    public async Task E2E_UserDelete_CaptureConfirmForget_StateIsForgotten()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await using var db = await CreateDbAsync(connection);

        var userId = Guid.NewGuid();
        var workspaceId = Guid.NewGuid();
        var profile = CreateProfile(userId);
        db.AgentProfiles.Add(profile);
        await db.SaveChangesAsync();

        var (memoryService, admissionService, _, retentionService) = CreateServices(db);

        // Start session and capture an item with evidence
        var session = await memoryService.StartSessionAsync(
            userId, workspaceId, profile.Id, "ext-key-forget", "Forget test task", null);

        var captured = await memoryService.CaptureMemoryAsync(userId, workspaceId, new CaptureMemoryInput
        {
            SessionId = session.Id,
            Kind = "fact",
            Title = "Server endpoint configuration",
            Content = "The production server is at https://api.example.com",
            Confidence = 0.9m,
            Importance = 5,
            Evidence = new List<EvidenceInput> { new() { ReferenceId = "ref-forget-1" } }
        });

        // With evidence, the item should be Qualified
        Assert.Equal("qualified", captured.AdmissionState);

        // Confirm the item first (Forget requires Confirmed state)
        var dbItem = await db.AgentMemoryItems.FirstAsync(i => i.Id == captured.Id);
        await admissionService.ConfirmMemoryAsync(dbItem);
        await db.SaveChangesAsync();

        // Verify it's Confirmed
        Assert.Equal(AdmissionState.Confirmed, dbItem.AdmissionState);

        // Create feedback for the forget action
        var feedback = new AgentMemoryFeedback
        {
            Id = Guid.NewGuid(),
            MemoryItemId = captured.Id,
            UserId = userId,
            Action = "forget",
            Note = "User requested deletion",
            CreatedAt = DateTime.UtcNow
        };
        db.AgentMemoryFeedbacks.Add(feedback);

        // Forget the item via RetentionService
        await retentionService.ForgetItemAsync(captured.Id);

        // Verify the state is Forgotten
        var forgottenItem = await db.AgentMemoryItems.FirstAsync(i => i.Id == captured.Id);
        Assert.Equal(MemoryStatus.Forgotten, forgottenItem.Status);
        Assert.Equal(AdmissionState.Confirmed, forgottenItem.AdmissionState); // AdmissionState stays Confirmed

        // Verify the item no longer appears in search results (Status != Active)
        var searchResults = await memoryService.SearchMemoryAsync(userId, workspaceId, new SearchMemoryInput
        {
            Query = "Server endpoint",
            Limit = 10
        });
        Assert.DoesNotContain(searchResults, r => r.Id == captured.Id);

        // Verify the feedback was persisted
        var dbFeedback = await db.AgentMemoryFeedbacks
            .FirstOrDefaultAsync(f => f.MemoryItemId == captured.Id && f.Action == "forget");
        Assert.NotNull(dbFeedback);
        Assert.Equal("User requested deletion", dbFeedback!.Note);
    }

    // -----------------------------------------------------------------------
    // Test 5: Archive and Restore Flow
    // -----------------------------------------------------------------------

    [Fact]
    public async Task E2E_ArchiveAndRestore_ConfirmedItem_ArchivedThenRestored()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await using var db = await CreateDbAsync(connection);

        var userId = Guid.NewGuid();
        var workspaceId = Guid.NewGuid();
        var profile = CreateProfile(userId);
        db.AgentProfiles.Add(profile);
        await db.SaveChangesAsync();

        var (memoryService, admissionService, _, retentionService) = CreateServices(db);

        // Start session and capture an item with evidence
        var session = await memoryService.StartSessionAsync(
            userId, workspaceId, profile.Id, "ext-key-archive", "Archive test task", null);

        var captured = await memoryService.CaptureMemoryAsync(userId, workspaceId, new CaptureMemoryInput
        {
            SessionId = session.Id,
            Kind = "constraint",
            Title = "Max file size limit",
            Content = "Files must not exceed 100MB.",
            Confidence = 0.95m,
            Importance = 8,
            Evidence = new List<EvidenceInput> { new() { ReferenceId = "ref-archive-1" } }
        });

        // Confirm the item (Archive requires Confirmed state)
        var dbItem = await db.AgentMemoryItems.FirstAsync(i => i.Id == captured.Id);
        await admissionService.ConfirmMemoryAsync(dbItem);
        await db.SaveChangesAsync();

        // Archive the item
        await retentionService.ArchiveItemAsync(captured.Id);

        var archivedItem = await db.AgentMemoryItems.FirstAsync(i => i.Id == captured.Id);
        Assert.Equal(MemoryStatus.Archived, archivedItem.Status);

        // Verify the archived item does not appear in search
        var searchBeforeRestore = await memoryService.SearchMemoryAsync(userId, workspaceId, new SearchMemoryInput
        {
            Query = "file size",
            Limit = 10
        });
        Assert.DoesNotContain(searchBeforeRestore, r => r.Id == captured.Id);

        // Restore the item
        await retentionService.RestoreItemAsync(captured.Id);

        var restoredItem = await db.AgentMemoryItems.FirstAsync(i => i.Id == captured.Id);
        Assert.Equal(MemoryStatus.Active, restoredItem.Status);
        Assert.Equal(AdmissionState.Confirmed, restoredItem.AdmissionState);

        // Verify the restored item appears in search again
        var searchAfterRestore = await memoryService.SearchMemoryAsync(userId, workspaceId, new SearchMemoryInput
        {
            Query = "file size",
            Limit = 10
        });
        Assert.Contains(searchAfterRestore, r => r.Id == captured.Id);
    }

    // -----------------------------------------------------------------------
    // Test 6: Retention Service Archive Candidates
    // -----------------------------------------------------------------------

    [Fact]
    public async Task E2E_Retention_LowValueOldItems_AreArchiveCandidates()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await using var db = await CreateDbAsync(connection);

        var userId = Guid.NewGuid();
        var workspaceId = Guid.NewGuid();
        var profile = CreateProfile(userId);
        db.AgentProfiles.Add(profile);
        await db.SaveChangesAsync();

        var (_, _, _, retentionService) = CreateServices(db);

        // Create a low-value, old item (no evidence, low importance, old timestamp)
        var oldItem = new AgentMemoryItem
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            OwnerUserId = userId,
            Kind = MemoryKind.Fact,
            Title = "Old low-value fact",
            Content = "This is an old fact with no evidence",
            AdmissionState = AdmissionState.Confirmed,
            Confidence = 0.1m,
            Importance = 1,
            FreshnessAt = DateTime.UtcNow.AddDays(-60),
            Status = MemoryStatus.Active,
            CreatedAt = DateTime.UtcNow.AddDays(-60),
            UpdatedAt = DateTime.UtcNow.AddDays(-60)
        };

        // Create a high-value, recent item (with evidence, high importance, recent)
        var newItem = new AgentMemoryItem
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            OwnerUserId = userId,
            Kind = MemoryKind.Decision,
            Title = "Important recent decision",
            Content = "This is a critical decision with high confidence",
            AdmissionState = AdmissionState.Confirmed,
            Confidence = 0.95m,
            Importance = 10,
            FreshnessAt = DateTime.UtcNow,
            Status = MemoryStatus.Active,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        db.AgentMemoryItems.AddRange(oldItem, newItem);
        await db.SaveChangesAsync();

        // Get archive candidates
        var candidates = await retentionService.GetArchiveCandidatesAsync(workspaceId);

        // The old low-value item should be a candidate
        Assert.Contains(oldItem.Id, candidates);
        // The high-value recent item should NOT be a candidate
        Assert.DoesNotContain(newItem.Id, candidates);
    }

    // -----------------------------------------------------------------------
    // Test 7: Checkpoint with Empty Session
    // -----------------------------------------------------------------------

    [Fact]
    public async Task E2E_Checkpoint_EmptySession_CreatesCheckpointWithEmptySummary()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await using var db = await CreateDbAsync(connection);

        var userId = Guid.NewGuid();
        var workspaceId = Guid.NewGuid();
        var profile = CreateProfile(userId);
        db.AgentProfiles.Add(profile);
        await db.SaveChangesAsync();

        var (memoryService, _, checkpointService, _) = CreateServices(db);

        // Start a session with no memories
        var session = await memoryService.StartSessionAsync(
            userId, workspaceId, profile.Id, "ext-key-empty", "Empty session test", null);

        // Create a checkpoint for the empty session
        var checkpoint = await checkpointService.CreateCheckpointAsync(session.Id);

        Assert.NotEqual(Guid.Empty, checkpoint.Id);
        Assert.Equal("pending", checkpoint.DeliveryState);
        Assert.Equal(0, checkpoint.FromSequence);
        Assert.Equal(0, checkpoint.ToSequence);
    }

    // -----------------------------------------------------------------------
    // Test 8: Idempotent Archive and Forget
    // -----------------------------------------------------------------------

    [Fact]
    public async Task E2E_Retention_IdempotentOperations_DoNotThrowOnRepeat()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await using var db = await CreateDbAsync(connection);

        var userId = Guid.NewGuid();
        var workspaceId = Guid.NewGuid();
        var profile = CreateProfile(userId);
        db.AgentProfiles.Add(profile);
        await db.SaveChangesAsync();

        var (memoryService, admissionService, _, retentionService) = CreateServices(db);

        var session = await memoryService.StartSessionAsync(
            userId, workspaceId, profile.Id, "ext-key-idempotent", "Idempotent test", null);

        var captured = await memoryService.CaptureMemoryAsync(userId, workspaceId, new CaptureMemoryInput
        {
            SessionId = session.Id,
            Kind = "fact",
            Title = "Idempotent test fact",
            Content = "Testing idempotent operations",
            Evidence = new List<EvidenceInput> { new() { ReferenceId = "ref-idempotent-1" } }
        });

        // Confirm the item
        var dbItem = await db.AgentMemoryItems.FirstAsync(i => i.Id == captured.Id);
        await admissionService.ConfirmMemoryAsync(dbItem);
        await db.SaveChangesAsync();

        // Archive twice (should not throw)
        await retentionService.ArchiveItemAsync(captured.Id);
        await retentionService.ArchiveItemAsync(captured.Id); // Idempotent no-op

        var archivedItem = await db.AgentMemoryItems.FirstAsync(i => i.Id == captured.Id);
        Assert.Equal(MemoryStatus.Archived, archivedItem.Status);

        // Restore twice (should not throw)
        await retentionService.RestoreItemAsync(captured.Id);
        await retentionService.RestoreItemAsync(captured.Id); // Idempotent no-op

        var restoredItem = await db.AgentMemoryItems.FirstAsync(i => i.Id == captured.Id);
        Assert.Equal(MemoryStatus.Active, restoredItem.Status);
    }
}
