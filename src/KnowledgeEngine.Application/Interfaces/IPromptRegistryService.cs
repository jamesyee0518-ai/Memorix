using KnowledgeEngine.Domain.Entities;

namespace KnowledgeEngine.Application.Interfaces;

/// <summary>
/// Service for managing the prompt registry: creating, versioning,
/// publishing, archiving, and resolving prompts for specific providers.
/// </summary>
public interface IPromptRegistryService
{
    /// <summary>
    /// Gets the latest published (active) prompt for the given key,
    /// optionally filtered by language.
    /// </summary>
    Task<PromptRegistry> GetActivePromptAsync(string promptKey, string? language, CancellationToken ct);

    /// <summary>
    /// Lists all versions of a prompt by key, ordered by creation date descending.
    /// </summary>
    Task<List<PromptRegistry>> ListVersionsAsync(string promptKey, CancellationToken ct);

    /// <summary>
    /// Publishes a draft prompt, activating it and archiving the previously active version.
    /// </summary>
    Task<PromptRegistry> PublishAsync(Guid id, CancellationToken ct);

    /// <summary>
    /// Archives a published prompt, deactivating it.
    /// </summary>
    Task<PromptRegistry> ArchiveAsync(Guid id, CancellationToken ct);

    /// <summary>
    /// Creates a new prompt version in draft status.
    /// </summary>
    Task<PromptRegistry> CreateAsync(PromptRegistry prompt, CancellationToken ct);

    /// <summary>
    /// Resolves the best matching published prompt for a specific provider.
    /// Filters by <see cref="PromptRegistry.ProviderCompatibility"/>; if no
    /// provider-specific prompt exists, falls back to the active prompt.
    /// </summary>
    Task<PromptRegistry?> ResolveForProviderAsync(string promptKey, string providerId, CancellationToken ct);
}
