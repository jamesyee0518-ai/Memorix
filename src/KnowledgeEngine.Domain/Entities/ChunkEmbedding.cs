namespace KnowledgeEngine.Domain.Entities;

public class ChunkEmbedding
{
    public Guid Id { get; set; }
    public Guid ChunkId { get; set; }
    public string WorkspaceId { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string? ModelVersion { get; set; }
    public int? Dimension { get; set; }
    public string? EmbeddingJson { get; set; }  // JSON序列化的float[]
    public string? VectorRef { get; set; }  // 外部向量存储引用
    public string? ChunkContentHash { get; set; }
    public string LanguageCode { get; set; } = "und";
    public string EmbeddingType { get; set; } = "original";
    public Guid? LocalizationId { get; set; }
    public string? SourceContentHash { get; set; }
    public string Status { get; set; } = "pending";  // pending/done/failed/stale
    public string? ErrorMessage { get; set; }
    public int RetryCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
