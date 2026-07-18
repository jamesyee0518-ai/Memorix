namespace KnowledgeEngine.Domain.Entities;

/// <summary>
/// Tracks the last-synced position for a given cursor type (§7.7).
/// Used by the dual-mode sync engine to perform incremental syncs
/// between local SQLite and cloud PostgreSQL.
/// </summary>
public class SyncCursor
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid RemoteWorkspaceId { get; set; }

    /// <summary>"inbox" | "sources" | "documents" | ...</summary>
    public string CursorType { get; set; } = "inbox";

    public string? CursorValue { get; set; }
    public DateTime? LastSyncedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
