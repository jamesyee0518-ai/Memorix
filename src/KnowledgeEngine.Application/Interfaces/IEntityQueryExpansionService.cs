namespace KnowledgeEngine.Application.Interfaces;

public interface IEntityQueryExpansionService
{
    Task<EntityQueryExpansion> ExpandAsync(
        Guid userId,
        string query,
        IReadOnlyCollection<Guid>? explicitEntityIds = null,
        CancellationToken ct = default);
}

public sealed class EntityQueryExpansion
{
    public IReadOnlyList<Guid> EntityIds { get; init; } = [];
    public IReadOnlyList<string> CanonicalTerms { get; init; } = [];
    public IReadOnlyList<string> VerifiedAliases { get; init; } = [];
}
