using KnowledgeEngine.Domain.Entities;

namespace KnowledgeEngine.Application.Interfaces;

public interface IEntityNameNormalizer
{
    EntityNameNormalizationResult Normalize(string rawName, string? entityType = null, string? languageHint = null);
}

public sealed class EntityNameNormalizationResult
{
    public string RawName { get; init; } = string.Empty;
    public string CanonicalName { get; init; } = string.Empty;
    public string NormalizedKey { get; init; } = string.Empty;
    public string EntityType { get; init; } = "CONCEPT";
    public string? Abbreviation { get; init; }
    public string Version { get; init; } = "entity_norm_v1";
    public IReadOnlyList<string> AliasCandidates { get; init; } = [];
    public IReadOnlyList<string> AppliedRules { get; init; } = [];
}

public interface IEntityTypeRegistry
{
    string Normalize(string? entityType);
    bool IsKnown(string? entityType);
    IReadOnlyCollection<string> All { get; }
}

public interface IEntityResolutionOrchestrator
{
    Task<EntityResolutionBatchResult> ResolveDocumentAsync(
        Guid documentId,
        IReadOnlyCollection<EntityResult> extractedEntities,
        EntityExtractionContext? extractionContext = null,
        CancellationToken ct = default);
}

public interface IEntityCandidateResolver
{
    Task<IReadOnlyList<EntityCandidateMatch>> RetrieveAsync(
        EntityCandidateRequest request,
        CancellationToken ct = default);

    decimal GetAutoLinkThreshold(string entityType);
    bool ShouldAutoLink(EntityCandidateMatch match, string entityType);
}

public interface IEntityVectorSimilarityService
{
    Task<IReadOnlyDictionary<Guid, EntityVectorScores>> ScoreAsync(
        string workspaceId,
        string queryName,
        string? queryContext,
        IReadOnlyCollection<Entity> candidates,
        CancellationToken ct = default);
}

public interface IEntityDisambiguationService
{
    Task<EntityDisambiguationResult> DecideAsync(
        EntityDisambiguationRequest request,
        CancellationToken ct = default);
}

public sealed class EntityDisambiguationRequest
{
    public Guid UserId { get; init; }
    public string WorkspaceId { get; init; } = string.Empty;
    public string Mention { get; init; } = string.Empty;
    public string EntityType { get; init; } = string.Empty;
    public string? Context { get; init; }
    public string? DocumentTitle { get; init; }
    public string? SourceDomain { get; init; }
    public DateTime? PublishedAt { get; init; }
    public string? Language { get; init; }
    public IReadOnlyCollection<EntityCandidateMatch> Candidates { get; init; } = [];
}

public sealed class EntityDisambiguationResult
{
    public string Decision { get; init; } = "INSUFFICIENT_EVIDENCE";
    public Guid? CandidateEntityId { get; init; }
    public decimal Confidence { get; init; }
    public IReadOnlyList<string> ReasonCodes { get; init; } = [];
    public string? Explanation { get; init; }
    public string? Model { get; init; }
    public int? InputTokens { get; init; }
    public int? OutputTokens { get; init; }
    public string PromptVersion { get; init; } = "entity_disambiguation_v1";
    public string? RawOutput { get; init; }
    public bool IsFallback { get; init; }
}

public sealed class EntityVectorScores
{
    public decimal NameScore { get; init; }
    public decimal DescriptionScore { get; init; }
}

public sealed class EntityCandidateRequest
{
    public Guid UserId { get; init; }
    public string WorkspaceId { get; init; } = string.Empty;
    public EntityNameNormalizationResult Normalized { get; init; } = new();
    public string Mention { get; init; } = string.Empty;
    public string? Context { get; init; }
    public string? Description { get; init; }
    public string? SourceDomain { get; init; }
    public IReadOnlyCollection<string> CooccurringNormalizedKeys { get; init; } = [];
    public int TopK { get; init; } = 20;
}

public sealed class EntityCandidateMatch
{
    public Guid EntityId { get; init; }
    public decimal NameScore { get; init; }
    public decimal AliasScore { get; init; }
    public decimal DescriptionScore { get; init; }
    public decimal ContextScore { get; init; }
    public decimal RelationScore { get; init; }
    public decimal SourceScore { get; init; }
    public decimal TotalScore { get; init; }
    public bool HardBlocked { get; init; }
    public IReadOnlyList<string> ReasonCodes { get; init; } = [];
}

public sealed class EntityExtractionContext
{
    public Guid? BatchId { get; init; }
    public string? Model { get; init; }
    public string? ModelVersion { get; init; }
    public string? PromptVersion { get; init; }
    public string SchemaVersion { get; init; } = "entity_mention_v1";
}

public sealed class EntityResolutionBatchResult
{
    public Guid BatchId { get; init; }
    public Guid DocumentId { get; init; }
    public string WorkspaceId { get; init; } = string.Empty;
    public int ExtractedCount { get; init; }
    public int AcceptedCount { get; init; }
    public int LinkedCount { get; init; }
    public int CreatedCount { get; init; }
    public int RejectedCount { get; init; }
}
