namespace KnowledgeEngine.Domain.Entities;

/// <summary>
/// Persistent audit log for cloud inbox pull attempts.
/// Records both successful and failed pulls without storing auth tokens.
/// </summary>
public class CloudInboxSyncLog
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }

    public string Direction { get; set; } = "pull";
    public string Status { get; set; } = "success";
    public string? CloudApiBaseUrl { get; set; }
    public string? CloudWorkspaceId { get; set; }
    public string Retention { get; set; } = "keep";

    public int PulledCount { get; set; }
    public int FailedCount { get; set; }
    public string? NextCursor { get; set; }
    public string? ErrorMessage { get; set; }

    public DateTime StartedAt { get; set; }
    public DateTime FinishedAt { get; set; }
    public long DurationMs { get; set; }
    public DateTime CreatedAt { get; set; }
}
