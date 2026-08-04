using KnowledgeEngine.Domain.Enums;

namespace KnowledgeEngine.Domain.Entities;

public class AgentMemoryItem
{
    public Guid Id { get; set; }
    public Guid? SessionId { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid OwnerUserId { get; set; }
    public Guid? AgentProfileId { get; set; }

    // Classification
    public MemoryKind Kind { get; set; }

    // Content
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string? Summary { get; set; }

    // Admission lifecycle
    public AdmissionState AdmissionState { get; set; } = AdmissionState.Ephemeral;
    public decimal Confidence { get; set; }
    public Visibility Visibility { get; set; } = Visibility.Agent;
    public int Importance { get; set; }
    public DateTime? FreshnessAt { get; set; }
    public MemoryStatus Status { get; set; } = MemoryStatus.Active;

    // Supersession chain
    public Guid? SupersededById { get; set; }
    public Guid? SupersedesId { get; set; }

    // Timestamps
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // Navigation
    public AgentMemorySession? Session { get; set; }
    public List<AgentMemoryEvidence> Evidences { get; set; } = new();
    public List<AgentMemoryFeedback> Feedbacks { get; set; } = new();

    // -----------------------------------------------------------------------
    // State transition methods
    // -----------------------------------------------------------------------

    /// <summary>
    /// Promote an ephemeral memory item to candidate status.
    /// </summary>
    public void PromoteToCandidate()
    {
        if (AdmissionState != AdmissionState.Ephemeral)
        {
            throw new InvalidOperationException(
                $"Cannot promote to candidate: current state is {AdmissionState}, expected {nameof(AdmissionState.Ephemeral)}.");
        }

        AdmissionState = AdmissionState.Candidate;
        Touch();
    }

    /// <summary>
    /// Qualify a candidate memory item (candidate -> qualified).
    /// </summary>
    public void Qualify()
    {
        if (AdmissionState != AdmissionState.Candidate)
        {
            throw new InvalidOperationException(
                $"Cannot qualify: current state is {AdmissionState}, expected {nameof(AdmissionState.Candidate)}.");
        }

        AdmissionState = AdmissionState.Qualified;
        Touch();
    }

    /// <summary>
    /// Confirm a qualified memory item (qualified -> confirmed).
    /// </summary>
    public void Confirm()
    {
        if (AdmissionState != AdmissionState.Qualified)
        {
            throw new InvalidOperationException(
                $"Cannot confirm: current state is {AdmissionState}, expected {nameof(AdmissionState.Qualified)}.");
        }

        AdmissionState = AdmissionState.Confirmed;
        Touch();
    }

    /// <summary>
    /// Reject a candidate or qualified memory item (candidate|qualified -> rejected).
    /// </summary>
    public void Reject()
    {
        if (AdmissionState != AdmissionState.Candidate && AdmissionState != AdmissionState.Qualified)
        {
            throw new InvalidOperationException(
                $"Cannot reject: current state is {AdmissionState}, expected {nameof(AdmissionState.Candidate)} or {nameof(AdmissionState.Qualified)}.");
        }

        AdmissionState = AdmissionState.Rejected;
        Status = MemoryStatus.Archived;
        Touch();
    }

    /// <summary>
    /// Archive a confirmed memory item (confirmed -> archived).
    /// </summary>
    public void Archive()
    {
        if (AdmissionState != AdmissionState.Confirmed)
        {
            throw new InvalidOperationException(
                $"Cannot archive: current state is {AdmissionState}, expected {nameof(AdmissionState.Confirmed)}.");
        }

        AdmissionState = AdmissionState.Confirmed;
        Status = MemoryStatus.Archived;
        Touch();
    }

    /// <summary>
    /// Restore an archived memory item (archived -> confirmed).
    /// </summary>
    public void Restore()
    {
        if (Status != MemoryStatus.Archived)
        {
            throw new InvalidOperationException(
                $"Cannot restore: current status is {Status}, expected {nameof(MemoryStatus.Archived)}.");
        }

        Status = MemoryStatus.Active;
        AdmissionState = AdmissionState.Confirmed;
        Touch();
    }

    /// <summary>
    /// Forget a confirmed memory item (confirmed -> forgotten).
    /// </summary>
    public void Forget()
    {
        if (AdmissionState != AdmissionState.Confirmed)
        {
            throw new InvalidOperationException(
                $"Cannot forget: current state is {AdmissionState}, expected {nameof(AdmissionState.Confirmed)}.");
        }

        Status = MemoryStatus.Forgotten;
        Touch();
    }

    /// <summary>
    /// Supersede a confirmed memory item with a new item (confirmed -> superseded).
    /// </summary>
    /// <param name="newItemId">The identifier of the new memory item that supersedes this one.</param>
    public void Supersede(Guid newItemId)
    {
        if (AdmissionState != AdmissionState.Confirmed)
        {
            throw new InvalidOperationException(
                $"Cannot supersede: current state is {AdmissionState}, expected {nameof(AdmissionState.Confirmed)}.");
        }

        if (newItemId == Guid.Empty)
        {
            throw new ArgumentException("The new item id must be a non-empty GUID.", nameof(newItemId));
        }

        SupersededById = newItemId;
        Status = MemoryStatus.Superseded;
        Touch();
    }

    private void Touch()
    {
        UpdatedAt = DateTime.UtcNow;
    }
}
