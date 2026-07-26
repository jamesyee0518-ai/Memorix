namespace KnowledgeEngine.Domain.Entities;

public class EntityMergeLog
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string WorkspaceId { get; set; } = string.Empty;
    public Guid BatchId { get; set; }
    public Guid SourceEntityId { get; set; }
    public Guid TargetEntityId { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string Method { get; set; } = "manual";
    public decimal? Score { get; set; }
    public Guid? OperatorId { get; set; }
    public string? DeviceId { get; set; }
    public string? RequestId { get; set; }
    public string BeforeSnapshot { get; set; } = "{}";
    public string? MigrationSummary { get; set; }
    public long ExpectedSourceVersion { get; set; }
    public long ExpectedTargetVersion { get; set; }
    public string Status { get; set; } = "pending";
    public string IdempotencyKey { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime? RevertedAt { get; set; }
}
