namespace KnowledgeEngine.Domain.Entities;

/// <summary>
/// Tracks the lifecycle of an inbox item import into a source (§7.5).
/// Each import attempt creates a job row; the job transitions through
/// "running" → "succeeded" | "failed".
/// </summary>
public class ImportJob
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid InboxItemId { get; set; }
    public Guid? SourceId { get; set; }

    /// <summary>"url_import" | "file_import" | "text_import" | "mixed_import"</summary>
    public string JobType { get; set; } = "text_import";

    /// <summary>"queued" | "running" | "succeeded" | "failed"</summary>
    public string Status { get; set; } = "queued";

    public int Attempt { get; set; }
    public int MaxAttempts { get; set; } = 3;
    public DateTime? StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
