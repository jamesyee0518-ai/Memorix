using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KnowledgeEngine.Infrastructure.Audio;

/// <summary>
/// Provider marketplace service for browsing, installing, uninstalling,
/// and rating audio capability providers.
/// Uses <see cref="IAppDbContext"/> for persistence.
/// </summary>
public class ProviderMarketplaceService : IProviderMarketplaceService
{
    private readonly IAppDbContext _db;
    private readonly ILogger<ProviderMarketplaceService> _logger;

    /// <summary>
    /// Creates a new <see cref="ProviderMarketplaceService"/>.
    /// </summary>
    /// <param name="db">Application database context.</param>
    /// <param name="logger">Logger for diagnostics.</param>
    public ProviderMarketplaceService(
        IAppDbContext db,
        ILogger<ProviderMarketplaceService> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<List<ProviderMarketplaceEntry>> BrowseAsync(
        string? capability,
        string? providerId,
        CancellationToken ct)
    {
        var query = _db.ProviderMarketplaceEntries.AsQueryable();

        if (!string.IsNullOrWhiteSpace(capability))
        {
            query = query.Where(e => e.Capability == capability);
        }

        if (!string.IsNullOrWhiteSpace(providerId))
        {
            query = query.Where(e => e.ProviderId == providerId);
        }

        var entries = await query
            .OrderByDescending(e => e.IsOfficial)
            .ThenByDescending(e => e.Rating)
            .ThenByDescending(e => e.InstallCount)
            .ToListAsync(ct);

        return entries;
    }

    /// <inheritdoc/>
    public async Task<ProviderMarketplaceEntry> InstallAsync(Guid entryId, CancellationToken ct)
    {
        var entry = await _db.ProviderMarketplaceEntries
            .FirstOrDefaultAsync(e => e.Id == entryId, ct)
            ?? throw new InvalidOperationException(
                $"Marketplace entry {entryId} not found.");

        if (entry.IsInstalled)
        {
            _logger.LogInformation("Marketplace entry {EntryId} is already installed.", entryId);
            return entry;
        }

        entry.IsInstalled = true;
        entry.InstallCount += 1;
        entry.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Installed marketplace entry '{Name}' (provider={ProviderId}, capability={Capability}). " +
            "Total installs: {InstallCount}",
            entry.Name, entry.ProviderId, entry.Capability, entry.InstallCount);

        // In a full implementation, this would also register the provider
        // configuration in the ProviderRegistry or write a provider config
        // entry so the runtime can activate it. For now, the IsInstalled flag
        // and InstallCount serve as the installation record.

        return entry;
    }

    /// <inheritdoc/>
    public async Task<bool> UninstallAsync(Guid entryId, CancellationToken ct)
    {
        var entry = await _db.ProviderMarketplaceEntries
            .FirstOrDefaultAsync(e => e.Id == entryId, ct);

        if (entry == null)
        {
            return false;
        }

        if (!entry.IsInstalled)
        {
            _logger.LogInformation("Marketplace entry {EntryId} is not currently installed.", entryId);
            return true;
        }

        entry.IsInstalled = false;
        entry.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Uninstalled marketplace entry '{Name}' (provider={ProviderId}).",
            entry.Name, entry.ProviderId);

        return true;
    }

    /// <inheritdoc/>
    public async Task RateAsync(Guid entryId, int rating, CancellationToken ct)
    {
        if (rating < 0 || rating > 5)
        {
            throw new ArgumentOutOfRangeException(nameof(rating), "Rating must be between 0 and 5.");
        }

        var entry = await _db.ProviderMarketplaceEntries
            .FirstOrDefaultAsync(e => e.Id == entryId, ct)
            ?? throw new InvalidOperationException(
                $"Marketplace entry {entryId} not found.");

        // Simple incremental average: newRating = oldRating + (newVote - oldRating) / (count + 1)
        // For simplicity, we treat InstallCount as a proxy for vote count when it's the first rating.
        var voteCount = Math.Max(entry.InstallCount, 1);
        entry.Rating = entry.Rating + ((decimal)rating - entry.Rating) / (voteCount + 1);
        entry.Rating = Math.Round(entry.Rating, 2);
        entry.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Rated marketplace entry '{Name}': rating={Rating} (vote={Vote})",
            entry.Name, entry.Rating, rating);
    }
}
