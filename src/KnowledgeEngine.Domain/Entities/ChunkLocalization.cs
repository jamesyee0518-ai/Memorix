namespace KnowledgeEngine.Domain.Entities;

public class ChunkLocalization
{
    public Guid Id { get; set; }
    public Guid ChunkId { get; set; }
    public Guid UserId { get; set; }
    public Guid? WorkspaceId { get; set; }
    public string LanguageCode { get; set; } = "zh-CN";
    public string? HeadingLocalized { get; set; }
    public string ContentLocalized { get; set; } = string.Empty;
    public string TranslationType { get; set; } = "machine";
    public string? Model { get; set; }
    public string PromptVersion { get; set; } = "chunk-zh-v1";
    public string? GlossaryVersion { get; set; }
    public int? QualityScore { get; set; }
    public string? QualityIssues { get; set; }
    public string ReviewStatus { get; set; } = "unreviewed";
    public string Status { get; set; } = "pending";
    public string SourceContentHash { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public DateTime? ReviewedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
