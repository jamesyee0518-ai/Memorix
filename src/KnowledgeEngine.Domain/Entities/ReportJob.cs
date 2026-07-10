namespace KnowledgeEngine.Domain.Entities;

public class ReportJob
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid? TopicId { get; set; }

    public string ReportType { get; set; } = string.Empty;
    public Guid? ReportId { get; set; }

    public string Status { get; set; } = "pending";

    public string? InputParams { get; set; } // JSONB
    public string? PlanJson { get; set; } // JSONB
    public string? RetrievalSnapshotJson { get; set; } // JSONB
    public string? PromptSnapshot { get; set; }
    public string? ModelOutput { get; set; }
    public string? Model { get; set; }
    public string? PromptVersion { get; set; }

    public int Progress { get; set; }
    public string? CurrentStep { get; set; }

    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public int RetryCount { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
}
