namespace KnowledgeEngine.Domain.Entities;

public class MultilingualBatchJob
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid DocumentId { get; set; }
    public string JobType { get; set; } = "translate";
    public string Status { get; set; } = "pending";
    public bool Force { get; set; }
    public int MaxChunks { get; set; } = 500;
    public int TotalItems { get; set; }
    public int ProcessedItems { get; set; }
    public int SucceededItems { get; set; }
    public int FailedItems { get; set; }
    public Guid? CurrentChunkId { get; set; }
    public string? ErrorMessage { get; set; }
    public int RetryCount { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
