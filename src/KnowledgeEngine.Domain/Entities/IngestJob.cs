namespace KnowledgeEngine.Domain.Entities;

public class IngestJob
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid SourceId { get; set; }

    public string JobType { get; set; } = string.Empty;
    public string Status { get; set; } = "pending";

    public string? ErrorMessage { get; set; }
    public int RetryCount { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
}
