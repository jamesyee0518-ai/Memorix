namespace KnowledgeEngine.Domain.Entities;

public class EntityGovernanceTask
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string WorkspaceId { get; set; } = string.Empty;
    public string TaskType { get; set; } = "DUPLICATE_SCAN";
    public Guid? ParentTaskId { get; set; }
    public Guid? SubjectEntityId { get; set; }
    public Guid? CandidateEntityId { get; set; }
    public Guid? MentionId { get; set; }
    public string Status { get; set; } = "pending";
    public int Priority { get; set; }
    public Guid? AssigneeId { get; set; }
    public string? IdempotencyKey { get; set; }
    public string? Cursor { get; set; }
    public int TotalItems { get; set; }
    public int ProcessedItems { get; set; }
    public int SucceededItems { get; set; }
    public int FailedItems { get; set; }
    public decimal? Score { get; set; }
    public string? ReasonCodes { get; set; }
    public string? Payload { get; set; }
    public string? Result { get; set; }
    public int RetryCount { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}
