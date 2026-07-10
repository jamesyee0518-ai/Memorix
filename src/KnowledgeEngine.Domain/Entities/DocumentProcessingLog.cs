namespace KnowledgeEngine.Domain.Entities;

public class DocumentProcessingLog
{
    public Guid Id { get; set; }
    public string WorkspaceId { get; set; } = string.Empty;
    public Guid? SourceId { get; set; }
    public Guid? DocumentId { get; set; }

    public string StepName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? Message { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorStack { get; set; }

    public string? InputSnapshot { get; set; }
    public string? OutputSnapshot { get; set; }

    public DateTime? StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
    public int? DurationMs { get; set; }

    public DateTime CreatedAt { get; set; }
}
