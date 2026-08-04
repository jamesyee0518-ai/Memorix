using KnowledgeEngine.Domain.Entities;
using KnowledgeEngine.Domain.Enums;
using Xunit;

namespace KnowledgeEngine.Infrastructure.Tests;

/// <summary>
/// Tests for AgentMemoryItem state machine transitions, invariants,
/// and evidence append-only behavior.
/// </summary>
public class AgentMemoryDomainTests
{
    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static AgentMemoryItem CreateItem(AdmissionState state = AdmissionState.Ephemeral)
    {
        var now = DateTime.UtcNow;
        return new AgentMemoryItem
        {
            Id = Guid.NewGuid(),
            WorkspaceId = Guid.NewGuid(),
            OwnerUserId = Guid.NewGuid(),
            Kind = MemoryKind.Fact,
            Title = "Test memory item",
            Content = "Some content",
            AdmissionState = state,
            Confidence = 0.5m,
            Visibility = Visibility.Agent,
            Importance = 5,
            Status = MemoryStatus.Active,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    private static AgentMemoryItem CreateConfirmedItem()
    {
        var item = CreateItem();
        item.PromoteToCandidate();
        item.Qualify();
        item.Confirm();
        return item;
    }

    // -----------------------------------------------------------------------
    // Valid state transitions
    // -----------------------------------------------------------------------

    [Fact]
    public void PromoteToCandidate_FromEphemeral_TransitionsToCandidate()
    {
        var item = CreateItem(AdmissionState.Ephemeral);

        item.PromoteToCandidate();

        Assert.Equal(AdmissionState.Candidate, item.AdmissionState);
        Assert.Equal(MemoryStatus.Active, item.Status);
    }

    [Fact]
    public void Qualify_FromCandidate_TransitionsToQualified()
    {
        var item = CreateItem(AdmissionState.Ephemeral);
        item.PromoteToCandidate();

        item.Qualify();

        Assert.Equal(AdmissionState.Qualified, item.AdmissionState);
        Assert.Equal(MemoryStatus.Active, item.Status);
    }

    [Fact]
    public void Confirm_FromQualified_TransitionsToConfirmed()
    {
        var item = CreateItem(AdmissionState.Ephemeral);
        item.PromoteToCandidate();
        item.Qualify();

        item.Confirm();

        Assert.Equal(AdmissionState.Confirmed, item.AdmissionState);
        Assert.Equal(MemoryStatus.Active, item.Status);
    }

    [Fact]
    public void Reject_FromCandidate_TransitionsToRejected_AndSetsStatusArchived()
    {
        var item = CreateItem(AdmissionState.Ephemeral);
        item.PromoteToCandidate();

        item.Reject();

        Assert.Equal(AdmissionState.Rejected, item.AdmissionState);
        Assert.Equal(MemoryStatus.Archived, item.Status);
    }

    [Fact]
    public void Reject_FromQualified_TransitionsToRejected_AndSetsStatusArchived()
    {
        var item = CreateItem(AdmissionState.Ephemeral);
        item.PromoteToCandidate();
        item.Qualify();

        item.Reject();

        Assert.Equal(AdmissionState.Rejected, item.AdmissionState);
        Assert.Equal(MemoryStatus.Archived, item.Status);
    }

    [Fact]
    public void Archive_FromConfirmed_SetsStatusArchived()
    {
        var item = CreateConfirmedItem();

        item.Archive();

        Assert.Equal(AdmissionState.Confirmed, item.AdmissionState);
        Assert.Equal(MemoryStatus.Archived, item.Status);
    }

    [Fact]
    public void Restore_FromArchived_TransitionsToActiveAndConfirmed()
    {
        var item = CreateConfirmedItem();
        item.Archive();
        Assert.Equal(MemoryStatus.Archived, item.Status);

        item.Restore();

        Assert.Equal(MemoryStatus.Active, item.Status);
        Assert.Equal(AdmissionState.Confirmed, item.AdmissionState);
    }

    [Fact]
    public void Forget_FromConfirmed_SetsStatusForgotten()
    {
        var item = CreateConfirmedItem();

        item.Forget();

        Assert.Equal(AdmissionState.Confirmed, item.AdmissionState);
        Assert.Equal(MemoryStatus.Forgotten, item.Status);
    }

    [Fact]
    public void Supersede_FromConfirmed_SetsStatusSuperseded_AndSupersededById()
    {
        var item = CreateConfirmedItem();
        var newItemId = Guid.NewGuid();

        item.Supersede(newItemId);

        Assert.Equal(MemoryStatus.Superseded, item.Status);
        Assert.Equal(newItemId, item.SupersededById);
    }

    // -----------------------------------------------------------------------
    // Confirmed items can be Archived / Restored / Forgotten / Superseded
    // -----------------------------------------------------------------------

    [Fact]
    public void ConfirmedItem_CanBeArchived()
    {
        var item = CreateConfirmedItem();

        item.Archive();

        Assert.Equal(MemoryStatus.Archived, item.Status);
    }

    [Fact]
    public void ConfirmedItem_CanBeArchivedThenRestored()
    {
        var item = CreateConfirmedItem();
        item.Archive();

        item.Restore();

        Assert.Equal(MemoryStatus.Active, item.Status);
        Assert.Equal(AdmissionState.Confirmed, item.AdmissionState);
    }

    [Fact]
    public void ConfirmedItem_CanBeForgotten()
    {
        var item = CreateConfirmedItem();

        item.Forget();

        Assert.Equal(MemoryStatus.Forgotten, item.Status);
    }

    [Fact]
    public void ConfirmedItem_CanBeSuperseded()
    {
        var item = CreateConfirmedItem();

        item.Supersede(Guid.NewGuid());

        Assert.Equal(MemoryStatus.Superseded, item.Status);
    }

    // -----------------------------------------------------------------------
    // Rejected items set Status to Archived
    // -----------------------------------------------------------------------

    [Fact]
    public void Reject_FromCandidate_SetsStatusToArchived()
    {
        var item = CreateItem(AdmissionState.Ephemeral);
        item.PromoteToCandidate();

        item.Reject();

        Assert.Equal(MemoryStatus.Archived, item.Status);
        Assert.Equal(AdmissionState.Rejected, item.AdmissionState);
    }

    [Fact]
    public void Reject_FromQualified_SetsStatusToArchived()
    {
        var item = CreateItem(AdmissionState.Ephemeral);
        item.PromoteToCandidate();
        item.Qualify();

        item.Reject();

        Assert.Equal(MemoryStatus.Archived, item.Status);
        Assert.Equal(AdmissionState.Rejected, item.AdmissionState);
    }

    // -----------------------------------------------------------------------
    // Invalid transitions throw InvalidOperationException
    // -----------------------------------------------------------------------

    [Fact]
    public void PromoteToCandidate_FromCandidate_ThrowsInvalidOperationException()
    {
        var item = CreateItem(AdmissionState.Ephemeral);
        item.PromoteToCandidate();

        Assert.Throws<InvalidOperationException>(() => item.PromoteToCandidate());
    }

    [Fact]
    public void PromoteToCandidate_FromConfirmed_ThrowsInvalidOperationException()
    {
        var item = CreateConfirmedItem();

        Assert.Throws<InvalidOperationException>(() => item.PromoteToCandidate());
    }

    [Fact]
    public void Qualify_FromEphemeral_ThrowsInvalidOperationException()
    {
        var item = CreateItem(AdmissionState.Ephemeral);

        Assert.Throws<InvalidOperationException>(() => item.Qualify());
    }

    [Fact]
    public void Qualify_FromConfirmed_ThrowsInvalidOperationException()
    {
        var item = CreateConfirmedItem();

        Assert.Throws<InvalidOperationException>(() => item.Qualify());
    }

    [Fact]
    public void Qualify_FromQualified_ThrowsInvalidOperationException()
    {
        var item = CreateItem(AdmissionState.Ephemeral);
        item.PromoteToCandidate();
        item.Qualify();

        Assert.Throws<InvalidOperationException>(() => item.Qualify());
    }

    [Fact]
    public void Confirm_FromEphemeral_ThrowsInvalidOperationException()
    {
        var item = CreateItem(AdmissionState.Ephemeral);

        Assert.Throws<InvalidOperationException>(() => item.Confirm());
    }

    [Fact]
    public void Confirm_FromCandidate_ThrowsInvalidOperationException()
    {
        var item = CreateItem(AdmissionState.Ephemeral);
        item.PromoteToCandidate();

        Assert.Throws<InvalidOperationException>(() => item.Confirm());
    }

    [Fact]
    public void Confirm_FromConfirmed_ThrowsInvalidOperationException()
    {
        var item = CreateConfirmedItem();

        Assert.Throws<InvalidOperationException>(() => item.Confirm());
    }

    [Fact]
    public void Reject_FromEphemeral_ThrowsInvalidOperationException()
    {
        var item = CreateItem(AdmissionState.Ephemeral);

        Assert.Throws<InvalidOperationException>(() => item.Reject());
    }

    [Fact]
    public void Reject_FromConfirmed_ThrowsInvalidOperationException()
    {
        var item = CreateConfirmedItem();

        Assert.Throws<InvalidOperationException>(() => item.Reject());
    }

    [Fact]
    public void Reject_FromRejected_ThrowsInvalidOperationException()
    {
        var item = CreateItem(AdmissionState.Ephemeral);
        item.PromoteToCandidate();
        item.Reject();

        Assert.Throws<InvalidOperationException>(() => item.Reject());
    }

    [Fact]
    public void Archive_FromEphemeral_ThrowsInvalidOperationException()
    {
        var item = CreateItem(AdmissionState.Ephemeral);

        Assert.Throws<InvalidOperationException>(() => item.Archive());
    }

    [Fact]
    public void Archive_FromCandidate_ThrowsInvalidOperationException()
    {
        var item = CreateItem(AdmissionState.Ephemeral);
        item.PromoteToCandidate();

        Assert.Throws<InvalidOperationException>(() => item.Archive());
    }

    [Fact]
    public void Archive_FromQualified_ThrowsInvalidOperationException()
    {
        var item = CreateItem(AdmissionState.Ephemeral);
        item.PromoteToCandidate();
        item.Qualify();

        Assert.Throws<InvalidOperationException>(() => item.Archive());
    }

    [Fact]
    public void Restore_FromActive_ThrowsInvalidOperationException()
    {
        var item = CreateConfirmedItem();
        // Status is Active at this point

        Assert.Throws<InvalidOperationException>(() => item.Restore());
    }

    [Fact]
    public void Forget_FromCandidate_ThrowsInvalidOperationException()
    {
        var item = CreateItem(AdmissionState.Ephemeral);
        item.PromoteToCandidate();

        Assert.Throws<InvalidOperationException>(() => item.Forget());
    }

    [Fact]
    public void Forget_FromQualified_ThrowsInvalidOperationException()
    {
        var item = CreateItem(AdmissionState.Ephemeral);
        item.PromoteToCandidate();
        item.Qualify();

        Assert.Throws<InvalidOperationException>(() => item.Forget());
    }

    [Fact]
    public void Forget_FromEphemeral_ThrowsInvalidOperationException()
    {
        var item = CreateItem(AdmissionState.Ephemeral);

        Assert.Throws<InvalidOperationException>(() => item.Forget());
    }

    [Fact]
    public void Supersede_FromCandidate_ThrowsInvalidOperationException()
    {
        var item = CreateItem(AdmissionState.Ephemeral);
        item.PromoteToCandidate();

        Assert.Throws<InvalidOperationException>(() => item.Supersede(Guid.NewGuid()));
    }

    [Fact]
    public void Supersede_FromQualified_ThrowsInvalidOperationException()
    {
        var item = CreateItem(AdmissionState.Ephemeral);
        item.PromoteToCandidate();
        item.Qualify();

        Assert.Throws<InvalidOperationException>(() => item.Supersede(Guid.NewGuid()));
    }

    [Fact]
    public void Supersede_FromEphemeral_ThrowsInvalidOperationException()
    {
        var item = CreateItem(AdmissionState.Ephemeral);

        Assert.Throws<InvalidOperationException>(() => item.Supersede(Guid.NewGuid()));
    }

    // -----------------------------------------------------------------------
    // Supersede with Guid.Empty throws ArgumentException
    // -----------------------------------------------------------------------

    [Fact]
    public void Supersede_WithEmptyGuid_ThrowsArgumentException()
    {
        var item = CreateConfirmedItem();

        Assert.Throws<ArgumentException>(() => item.Supersede(Guid.Empty));
    }

    [Fact]
    public void Supersede_WithEmptyGuid_ThrowsArgumentException_NotInvalidOperationException()
    {
        var item = CreateConfirmedItem();

        var ex = Assert.Throws<ArgumentException>(() => item.Supersede(Guid.Empty));
        Assert.Equal("newItemId", ex.ParamName);
    }

    // -----------------------------------------------------------------------
    // Touch updates UpdatedAt on every state transition
    // -----------------------------------------------------------------------

    [Fact]
    public void StateTransition_UpdatesUpdatedAt()
    {
        var item = CreateItem(AdmissionState.Ephemeral);
        var originalUpdatedAt = item.UpdatedAt;

        // Small delay to ensure timestamp difference
        Thread.Sleep(10);

        item.PromoteToCandidate();

        Assert.True(item.UpdatedAt > originalUpdatedAt);
    }

    // -----------------------------------------------------------------------
    // Evidence is append-only (add to list, no remove API)
    // -----------------------------------------------------------------------

    [Fact]
    public void Evidence_CanBeAddedToList()
    {
        var item = CreateItem(AdmissionState.Ephemeral);

        var evidence1 = new AgentMemoryEvidence
        {
            Id = Guid.NewGuid(),
            MemoryItemId = item.Id,
            EvidenceKind = EvidenceKind.UserInput,
            ReferenceId = "ref-1",
            CapturedAt = DateTime.UtcNow
        };

        item.Evidences.Add(evidence1);

        Assert.Single(item.Evidences);
        Assert.Equal(evidence1.Id, item.Evidences[0].Id);
    }

    [Fact]
    public void Evidence_CanBeAppended_MultipleEvidence()
    {
        var item = CreateItem(AdmissionState.Ephemeral);

        var evidence1 = new AgentMemoryEvidence
        {
            Id = Guid.NewGuid(),
            MemoryItemId = item.Id,
            EvidenceKind = EvidenceKind.UserInput,
            ReferenceId = "ref-1",
            CapturedAt = DateTime.UtcNow
        };

        var evidence2 = new AgentMemoryEvidence
        {
            Id = Guid.NewGuid(),
            MemoryItemId = item.Id,
            EvidenceKind = EvidenceKind.ToolInvocation,
            ReferenceId = "ref-2",
            CapturedAt = DateTime.UtcNow
        };

        item.Evidences.Add(evidence1);
        item.Evidences.Add(evidence2);

        Assert.Equal(2, item.Evidences.Count);
        Assert.Contains(item.Evidences, e => e.Id == evidence1.Id);
        Assert.Contains(item.Evidences, e => e.Id == evidence2.Id);
    }

    [Fact]
    public void Evidence_NewItem_HasEmptyList()
    {
        var item = new AgentMemoryItem();

        Assert.NotNull(item.Evidences);
        Assert.Empty(item.Evidences);
    }

    [Fact]
    public void Feedback_NewItem_HasEmptyList()
    {
        var item = new AgentMemoryItem();

        Assert.NotNull(item.Feedbacks);
        Assert.Empty(item.Feedbacks);
    }

    [Fact]
    public void Feedback_CanBeAddedToList()
    {
        var item = CreateConfirmedItem();

        var feedback = new AgentMemoryFeedback
        {
            Id = Guid.NewGuid(),
            MemoryItemId = item.Id,
            UserId = Guid.NewGuid(),
            Action = "confirm",
            CreatedAt = DateTime.UtcNow
        };

        item.Feedbacks.Add(feedback);

        Assert.Single(item.Feedbacks);
        Assert.Equal(feedback.Id, item.Feedbacks[0].Id);
    }

    // -----------------------------------------------------------------------
    // Default values
    // -----------------------------------------------------------------------

    [Fact]
    public void NewItem_DefaultAdmissionState_IsEphemeral()
    {
        var item = new AgentMemoryItem();

        Assert.Equal(AdmissionState.Ephemeral, item.AdmissionState);
    }

    [Fact]
    public void NewItem_DefaultStatus_IsActive()
    {
        var item = new AgentMemoryItem();

        Assert.Equal(MemoryStatus.Active, item.Status);
    }

    [Fact]
    public void NewItem_DefaultVisibility_IsAgent()
    {
        var item = new AgentMemoryItem();

        Assert.Equal(Visibility.Agent, item.Visibility);
    }

    [Fact]
    public void NewItem_DefaultSupersededById_IsNull()
    {
        var item = new AgentMemoryItem();

        Assert.Null(item.SupersededById);
        Assert.Null(item.SupersedesId);
    }

    // -----------------------------------------------------------------------
    // Full lifecycle: Ephemeral -> Candidate -> Qualified -> Confirmed -> Archived -> Restored
    // -----------------------------------------------------------------------

    [Fact]
    public void FullLifecycle_EphemeralThroughArchivedAndRestored()
    {
        var item = CreateItem(AdmissionState.Ephemeral);

        // Ephemeral -> Candidate
        item.PromoteToCandidate();
        Assert.Equal(AdmissionState.Candidate, item.AdmissionState);

        // Candidate -> Qualified
        item.Qualify();
        Assert.Equal(AdmissionState.Qualified, item.AdmissionState);

        // Qualified -> Confirmed
        item.Confirm();
        Assert.Equal(AdmissionState.Confirmed, item.AdmissionState);
        Assert.Equal(MemoryStatus.Active, item.Status);

        // Confirmed -> Archived
        item.Archive();
        Assert.Equal(MemoryStatus.Archived, item.Status);
        Assert.Equal(AdmissionState.Confirmed, item.AdmissionState);

        // Archived -> Restored
        item.Restore();
        Assert.Equal(MemoryStatus.Active, item.Status);
        Assert.Equal(AdmissionState.Confirmed, item.AdmissionState);
    }

    [Fact]
    public void FullLifecycle_EphemeralThroughForgotten()
    {
        var item = CreateItem(AdmissionState.Ephemeral);

        item.PromoteToCandidate();
        item.Qualify();
        item.Confirm();
        item.Forget();

        Assert.Equal(AdmissionState.Confirmed, item.AdmissionState);
        Assert.Equal(MemoryStatus.Forgotten, item.Status);
    }

    [Fact]
    public void FullLifecycle_EphemeralThroughSuperseded()
    {
        var item = CreateItem(AdmissionState.Ephemeral);
        var newItemId = Guid.NewGuid();

        item.PromoteToCandidate();
        item.Qualify();
        item.Confirm();
        item.Supersede(newItemId);

        Assert.Equal(MemoryStatus.Superseded, item.Status);
        Assert.Equal(newItemId, item.SupersededById);
        Assert.Equal(AdmissionState.Confirmed, item.AdmissionState);
    }

    [Fact]
    public void FullLifecycle_EphemeralThroughRejected()
    {
        var item = CreateItem(AdmissionState.Ephemeral);

        item.PromoteToCandidate();
        item.Reject();

        Assert.Equal(AdmissionState.Rejected, item.AdmissionState);
        Assert.Equal(MemoryStatus.Archived, item.Status);
    }
}
