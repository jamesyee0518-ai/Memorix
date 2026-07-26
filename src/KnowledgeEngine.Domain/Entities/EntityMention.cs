namespace KnowledgeEngine.Domain.Entities;

public class EntityMention
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string WorkspaceId { get; set; } = string.Empty;
    public Guid DocumentId { get; set; }
    public Guid? ChunkId { get; set; }
    public Guid? EntityId { get; set; }
    public string MentionText { get; set; } = string.Empty;
    public string NormalizedMention { get; set; } = string.Empty;
    public string SuggestedType { get; set; } = "CONCEPT";
    public string? ContextText { get; set; }
    public int? StartOffset { get; set; }
    public int? EndOffset { get; set; }
    public int OccurrenceCount { get; set; } = 1;
    public Guid ExtractionBatchId { get; set; }
    public string? ExtractionModel { get; set; }
    public string? ModelVersion { get; set; }
    public string? PromptVersion { get; set; }
    public string SchemaVersion { get; set; } = "entity_mention_v1";
    public decimal? ExtractionConfidence { get; set; }
    public string ResolutionStatus { get; set; } = "UNRESOLVED";
    public string? ResolutionMethod { get; set; }
    public decimal? ResolutionScore { get; set; }
    public string ResolverVersion { get; set; } = "entity_resolver_v1";
    public string? ReasonCodes { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
