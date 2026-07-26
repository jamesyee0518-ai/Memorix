namespace KnowledgeEngine.Domain.Entities;

public class EntityEmbedding
{
    public Guid Id { get; set; }
    public Guid EntityId { get; set; }
    public string WorkspaceId { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string? ModelVersion { get; set; }
    public int? Dimension { get; set; }
    public string EmbeddingType { get; set; } = "name";
    public string? EmbeddingJson { get; set; }
    public string ContentHash { get; set; } = string.Empty;
    public string Status { get; set; } = "pending";
    public string? ErrorMessage { get; set; }
    public int RetryCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
