namespace KnowledgeEngine.Domain.Entities;

public class ExportJob
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid? TopicId { get; set; }

    public string ExportType { get; set; } = string.Empty;
    public string TargetType { get; set; } = string.Empty;
    public Guid? TargetId { get; set; }

    public string Status { get; set; } = "pending";

    public string? Params { get; set; } // JSONB
    public Guid? FileId { get; set; }
    public string? OutputPath { get; set; }

    public int Progress { get; set; }
    public string? ErrorMessage { get; set; }
    public int RetryCount { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
}
