namespace KnowledgeEngine.Domain.Entities;

public class Entity
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? NormalizedName { get; set; }
    public string EntityType { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Metadata { get; set; } // JSONB
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // Phase 4 fields
    public string WorkspaceId { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string? Aliases { get; set; }  // JSON
    public string? ExternalRef { get; set; }
    public string Source { get; set; } = "ai";
    public int UsageCount { get; set; }
    public bool IsVerified { get; set; }
    public bool IsArchived { get; set; }

    // Entity resolution V1 fields. Name/NormalizedName remain compatibility aliases
    // while callers migrate to the canonical identity model.
    public string? CanonicalName { get; set; }
    public string? PreferredNameZh { get; set; }
    public string? PreferredNameEn { get; set; }
    public string? Abbreviation { get; set; }
    public string? NormalizedKey { get; set; }
    public string Status { get; set; } = "active";
    public Guid? MergedIntoId { get; set; }
    public decimal? Confidence { get; set; }
    public int SourceCount { get; set; }
    public int MentionCount { get; set; }
    public long RowVersion { get; set; }
    public string NormalizationVersion { get; set; } = "entity_norm_v1";
}
