namespace KnowledgeEngine.Domain.Entities;

public class EntityAlias
{
    public Guid Id { get; set; }
    public Guid EntityId { get; set; }
    public Guid UserId { get; set; }
    public string WorkspaceId { get; set; } = string.Empty;
    public string Alias { get; set; } = string.Empty;
    public string NormalizedAlias { get; set; } = string.Empty;
    public string? LanguageCode { get; set; }
    public string AliasType { get; set; } = "MODEL_GENERATED";
    public string SourceType { get; set; } = "ai";
    public string? SourceId { get; set; }
    public decimal? Confidence { get; set; }
    public bool IsVerified { get; set; }
    public DateTime? ValidFrom { get; set; }
    public DateTime? ValidTo { get; set; }
    public string NormalizationVersion { get; set; } = "entity_norm_v1";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
