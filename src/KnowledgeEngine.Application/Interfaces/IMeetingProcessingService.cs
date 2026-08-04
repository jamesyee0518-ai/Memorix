using KnowledgeEngine.Application.DTOs;

namespace KnowledgeEngine.Application.Interfaces;

/// <summary>
/// Orchestrates the asynchronous meeting processing pipeline (§14).
/// Handles task splitting, state machine transitions, idempotency,
/// retry logic, and degradation strategies for the full meeting lifecycle:
/// audio normalize → VAD → batch transcribe → diarize → summarize → action items → publish.
/// </summary>
public interface IMeetingProcessingService
{
    /// <summary>
    /// Creates and queues the full processing pipeline for a meeting.
    /// Generates idempotent tasks for each step in §14.1.
    /// Returns the list of created processing tasks.
    /// </summary>
    Task<List<MeetingProcessingTaskDto>> CreatePipelineAsync(
        Guid meetingId, Guid audioAssetId, Guid userId, CancellationToken ct);

    /// <summary>
    /// Executes the processing pipeline for a meeting.
    /// Runs tasks in dependency order, with idempotency checks.
    /// </summary>
    Task<MeetingProcessingResultDto> ExecutePipelineAsync(
        Guid meetingId, Guid userId, CancellationToken ct);

    /// <summary>
    /// Gets the current status of all processing tasks for a meeting.
    /// </summary>
    Task<List<MeetingProcessingTaskDto>> GetTaskStatusAsync(
        Guid meetingId, CancellationToken ct);

    /// <summary>
    /// Retries a failed task. Resets status to PENDING and increments retry count.
    /// Returns false if max retries have been exceeded.
    /// </summary>
    Task<bool> RetryTaskAsync(Guid taskId, Guid userId, CancellationToken ct);

    /// <summary>
    /// Cancels all pending/running tasks for a meeting.
    /// </summary>
    Task<bool> CancelPipelineAsync(Guid meetingId, Guid userId, CancellationToken ct);

    /// <summary>
    /// Gets the overall pipeline progress for a meeting.
    /// Returns a summary with counts of tasks in each status.
    /// </summary>
    Task<MeetingProcessingResultDto> GetPipelineProgressAsync(
        Guid meetingId, CancellationToken ct);
}

/// <summary>DTO for a single meeting processing task.</summary>
public class MeetingProcessingTaskDto
{
    public Guid Id { get; set; }
    public Guid MeetingId { get; set; }
    public Guid? AudioAssetId { get; set; }
    public Guid? TranscriptionJobId { get; set; }
    public string TaskType { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public string ExecutionMode { get; set; } = string.Empty;
    public string? ProviderId { get; set; }
    public string? ModelId { get; set; }
    public string? DependsOn { get; set; }
    public int RetryCount { get; set; }
    public int MaxRetries { get; set; }
    public string? ErrorMessage { get; set; }
    public decimal? EstimatedCost { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
}

/// <summary>DTO for overall pipeline execution result.</summary>
public class MeetingProcessingResultDto
{
    public Guid MeetingId { get; set; }
    public string OverallStatus { get; set; } = "PENDING";
    public int TotalTasks { get; set; }
    public int SucceededTasks { get; set; }
    public int FailedTasks { get; set; }
    public int PendingTasks { get; set; }
    public int RunningTasks { get; set; }
    public int CanceledTasks { get; set; }
    public List<MeetingProcessingTaskDto> Tasks { get; set; } = new();
    public string? ErrorMessage { get; set; }
}
