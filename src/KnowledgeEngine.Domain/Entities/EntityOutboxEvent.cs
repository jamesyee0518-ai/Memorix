namespace KnowledgeEngine.Domain.Entities;

public class EntityOutboxEvent
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string WorkspaceId { get; set; } = string.Empty;
    public Guid EntityId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public long EntityVersion { get; set; }
    public string Payload { get; set; } = "{}";
    public string IdempotencyKey { get; set; } = string.Empty;
    public string Status { get; set; } = "pending";
    public int RetryCount { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ProcessedAt { get; set; }
}
