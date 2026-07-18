namespace KnowledgeEngine.Domain.Entities;

public class WorkspaceBinding
{
    public Guid Id { get; set; }
    public Guid LocalWorkspaceId { get; set; }
    public Guid CloudAccountBindingId { get; set; }
    public string CloudWorkspaceId { get; set; } = string.Empty;
    public string SyncMode { get; set; } = SyncModes.None;
    public string BindingStatus { get; set; } = "active";
    public Guid? PrimaryDeviceId { get; set; }
    public bool UploadOriginalFiles { get; set; }
    public string ConflictPolicy { get; set; } = "manual";
    public string? LastInboxCursor { get; set; }
    public string? LastSyncCursor { get; set; }
    public DateTime? LastSyncAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public static class SyncModes
{
    public const string None = "none";
    public const string InboxOnly = "inbox_only";
    public const string Backup = "backup";
    public const string Metadata = "metadata";
    public const string Bidirectional = "bidirectional";

    public static bool IsValid(string? value) =>
        value is None or InboxOnly or Backup or Metadata or Bidirectional;
}
