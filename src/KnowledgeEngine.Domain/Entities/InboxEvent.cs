namespace KnowledgeEngine.Domain.Entities;

/// <summary>
/// Append-only event log for inbox items (§7.6).
/// Every state transition (created, imported, failed, retried, archived, ...)
/// is recorded as a row in this table.
/// </summary>
public class InboxEvent
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid InboxItemId { get; set; }

    /// <summary>"created" | "imported" | "failed" | "retried" | "archived" | ...</summary>
    public string EventType { get; set; } = "created";

    /// <summary>JSON payload with event-specific context.</summary>
    public string? EventPayload { get; set; }

    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
}
