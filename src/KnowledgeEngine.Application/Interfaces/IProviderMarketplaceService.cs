using KnowledgeEngine.Domain.Entities;

namespace KnowledgeEngine.Application.Interfaces;

/// <summary>
/// Provider marketplace service for browsing, installing, and rating
/// audio capability providers.
/// </summary>
public interface IProviderMarketplaceService
{
    /// <summary>
    /// Browses marketplace entries with optional filters.
    /// </summary>
    /// <param name="capability">Optional capability filter (e.g. "audio.transcription").</param>
    /// <param name="providerId">Optional provider ID filter.</param>
    Task<List<ProviderMarketplaceEntry>> BrowseAsync(string? capability, string? providerId, CancellationToken ct);

    /// <summary>
    /// Installs a marketplace entry by marking it as installed and registering
    /// the provider configuration. Returns the updated entry.
    /// </summary>
    Task<ProviderMarketplaceEntry> InstallAsync(Guid entryId, CancellationToken ct);

    /// <summary>
    /// Uninstalls a marketplace entry by marking it as not installed.
    /// Returns true if the entry was found and uninstalled.
    /// </summary>
    Task<bool> UninstallAsync(Guid entryId, CancellationToken ct);

    /// <summary>
    /// Rates a marketplace entry. Rating must be between 0 and 5.
    /// The rating is stored as a simple average.
    /// </summary>
    Task RateAsync(Guid entryId, int rating, CancellationToken ct);
}
