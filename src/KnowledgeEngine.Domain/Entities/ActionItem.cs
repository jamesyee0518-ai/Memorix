namespace KnowledgeEngine.Domain.Entities;

/// <summary>
/// An action item extracted from meeting minutes.
/// Requires user confirmation before it can be written to the formal task system.
/// Source segment references enable traceability to the original transcript.
/// </summary>
public class ActionItem
{
    public Guid Id { get; set; }
    public Guid MeetingId { get; set; }

    /// <summary>Link to the minutes version this item was extracted from.</summary>
    public Guid? MinutesVersionId { get; set; }

    /// <summary>Task description text.</summary>
    public string TaskText { get; set; } = string.Empty;

    /// <summary>Owner name as mentioned in the meeting (pre-confirmation).</summary>
    public string? OwnerText { get; set; }

    /// <summary>Resolved user id after confirmation.</summary>
    public Guid? OwnerUserId { get; set; }

    /// <summary>Due date as mentioned in the meeting (pre-confirmation).</summary>
    public DateTime? DueDate { get; set; }

    /// <summary>Priority: LOW / MEDIUM / HIGH / URGENT</summary>
    public string Priority { get; set; } = "MEDIUM";

    /// <summary>LLM confidence score (0-1) for the extraction.</summary>
    public decimal Confidence { get; set; }

    /// <summary>PENDING_CONFIRMATION / CONFIRMED / MODIFIED / IGNORED / CONVERTED_TO_TASK</summary>
    public string ConfirmationStatus { get; set; } = ActionItemConfirmationStatuses.PendingConfirmation;

    /// <summary>Link to the created Memorix task (after confirmation).</summary>
    public Guid? TaskId { get; set; }

    /// <summary>JSON array of source segment UUIDs for traceability.</summary>
    public string? SourceSegmentIds { get; set; }

    public DateTime CreatedAt { get; set; }
}

/// <summary>Action item confirmation status values.</summary>
public static class ActionItemConfirmationStatuses
{
    public const string PendingConfirmation = "PENDING_CONFIRMATION";
    public const string Confirmed = "CONFIRMED";
    public const string Modified = "MODIFIED";
    public const string Ignored = "IGNORED";
    public const string ConvertedToTask = "CONVERTED_TO_TASK";
}

/// <summary>Action item priority values.</summary>
public static class ActionItemPriorities
{
    public const string Low = "LOW";
    public const string Medium = "MEDIUM";
    public const string High = "HIGH";
    public const string Urgent = "URGENT";
}
