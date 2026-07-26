namespace KnowledgeEngine.Application.DTOs;

public class EntityListItem
{
    public Guid Id { get; set; }
    public string WorkspaceId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? CanonicalName { get; set; }
    public string? PreferredNameZh { get; set; }
    public string? PreferredNameEn { get; set; }
    public string? Abbreviation { get; set; }
    public string EntityType { get; set; } = string.Empty;
    public string Status { get; set; } = "active";
    public bool IsVerified { get; set; }
    public decimal? Confidence { get; set; }
    public string? Description { get; set; }
    public int DocumentCount { get; set; }
    public int MentionCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class EntityDetail
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string WorkspaceId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? CanonicalName { get; set; }
    public string? PreferredNameZh { get; set; }
    public string? PreferredNameEn { get; set; }
    public string? Abbreviation { get; set; }
    public string? NormalizedName { get; set; }
    public string? NormalizedKey { get; set; }
    public string NormalizationVersion { get; set; } = string.Empty;
    public long RowVersion { get; set; }
    public string EntityType { get; set; } = string.Empty;
    public string Status { get; set; } = "active";
    public Guid? MergedIntoId { get; set; }
    public Guid? RedirectedFrom { get; set; }
    public bool IsVerified { get; set; }
    public decimal? Confidence { get; set; }
    public int SourceCount { get; set; }
    public int MentionCount { get; set; }
    public string? Description { get; set; }
    public string? Metadata { get; set; }
    public List<EntityAliasItem> Aliases { get; set; } = new();
    public List<RelatedDocument> RelatedDocuments { get; set; } = new();
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class EntityAliasItem
{
    public Guid Id { get; set; }
    public string Alias { get; set; } = string.Empty;
    public string NormalizedAlias { get; set; } = string.Empty;
    public string? LanguageCode { get; set; }
    public string AliasType { get; set; } = string.Empty;
    public string SourceType { get; set; } = string.Empty;
    public decimal? Confidence { get; set; }
    public bool IsVerified { get; set; }
}

public class RelatedDocument
{
    public Guid DocumentId { get; set; }
    public string Title { get; set; } = string.Empty;
    public int MentionCount { get; set; }
    public decimal? Confidence { get; set; }
    public string? Evidence { get; set; }
}

public sealed class CreateEntityRequest
{
    public Guid WorkspaceId { get; set; }
    public string CanonicalName { get; set; } = string.Empty;
    public string EntityType { get; set; } = "CONCEPT";
    public string? PreferredNameZh { get; set; }
    public string? PreferredNameEn { get; set; }
    public string? Abbreviation { get; set; }
    public string? Description { get; set; }
}

public sealed class UpdateEntityRequest
{
    public long ExpectedVersion { get; set; }
    public string? CanonicalName { get; set; }
    public string? EntityType { get; set; }
    public string? PreferredNameZh { get; set; }
    public string? PreferredNameEn { get; set; }
    public string? Abbreviation { get; set; }
    public string? Description { get; set; }
    public bool? IsVerified { get; set; }
    public bool? IsArchived { get; set; }
}

public sealed class UpsertEntityAliasRequest
{
    public string Alias { get; set; } = string.Empty;
    public string? LanguageCode { get; set; }
    public string AliasType { get; set; } = "MANUAL";
    public bool IsVerified { get; set; } = true;
    public decimal? Confidence { get; set; }
}

public sealed class EntityMentionItem
{
    public Guid Id { get; set; }
    public Guid DocumentId { get; set; }
    public Guid? ChunkId { get; set; }
    public string MentionText { get; set; } = string.Empty;
    public string? ContextText { get; set; }
    public int OccurrenceCount { get; set; }
    public string ResolutionStatus { get; set; } = string.Empty;
    public string? ResolutionMethod { get; set; }
    public decimal? ResolutionScore { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class EntityRelationItem
{
    public Guid Id { get; set; }
    public Guid SourceEntityId { get; set; }
    public Guid TargetEntityId { get; set; }
    public string RelationType { get; set; } = string.Empty;
    public Guid? EvidenceDocumentId { get; set; }
    public string? EvidenceText { get; set; }
    public decimal? Confidence { get; set; }
    public DateTime CreatedAt { get; set; }
}
