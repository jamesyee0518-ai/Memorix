using KnowledgeEngine.Application.DTOs;

namespace KnowledgeEngine.Application.Interfaces;

/// <summary>
/// Job queue abstraction.
/// Local mode: uses DB table polling.
/// Cloud mode: uses Redis / external queue.
/// Business code calls jobQueue.EnqueueAsync() without knowing the backing store.
/// </summary>
public interface IJobQueue
{
    Task<JobDto> EnqueueAsync(CreateJobInput input, CancellationToken ct = default);
    Task<JobDto?> GetNextAsync(string[]? jobTypes = null, CancellationToken ct = default);
    Task MarkRunningAsync(string jobId, CancellationToken ct = default);
    Task MarkDoneAsync(string jobId, CancellationToken ct = default);
    Task MarkFailedAsync(string jobId, string error, CancellationToken ct = default);
    Task<int> GetPendingCountAsync(CancellationToken ct = default);
}

public class JobDto
{
    public string Id { get; set; } = string.Empty;
    public string WorkspaceId { get; set; } = string.Empty;
    public string JobType { get; set; } = string.Empty;
    public string TargetType { get; set; } = string.Empty;
    public string TargetId { get; set; } = string.Empty;
    public string Status { get; set; } = "pending";
    public string? ErrorMessage { get; set; }
    public int RetryCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
}

public class CreateJobInput
{
    public string WorkspaceId { get; set; } = string.Empty;
    public string JobType { get; set; } = string.Empty;
    public string TargetType { get; set; } = string.Empty;
    public string TargetId { get; set; } = string.Empty;
}
