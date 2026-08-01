using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KnowledgeEngine.Infrastructure.Audio;

/// <summary>
/// Implementation of <see cref="IPromptRegistryService"/> using <see cref="IAppDbContext"/>.
/// Manages the full lifecycle of prompt versions: create (draft), publish, archive,
/// and provider-aware resolution.
/// </summary>
public class PromptRegistryService : IPromptRegistryService
{
    private readonly IAppDbContext _db;
    private readonly ILogger<PromptRegistryService> _logger;

    public PromptRegistryService(
        IAppDbContext db,
        ILogger<PromptRegistryService> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<PromptRegistry> GetActivePromptAsync(
        string promptKey, string? language, CancellationToken ct)
    {
        var query = _db.PromptRegistries
            .Where(p => p.PromptKey == promptKey
                        && p.Status == PromptRegistryStatuses.Published
                        && p.IsActive);

        if (!string.IsNullOrWhiteSpace(language))
        {
            // Prefer language-specific prompt; fall back to language-agnostic (null)
            var langSpecific = await query
                .Where(p => p.Language == language)
                .OrderByDescending(p => p.PublishedAt)
                .FirstOrDefaultAsync(ct);

            if (langSpecific != null)
            {
                return langSpecific;
            }

            // Fall back to language-agnostic (null language)
            return await query
                .Where(p => p.Language == null)
                .OrderByDescending(p => p.PublishedAt)
                .FirstOrDefaultAsync(ct)
                ?? throw new InvalidOperationException(
                    $"No active published prompt found for key '{promptKey}' with language '{language}'.");
        }

        return await query
            .OrderByDescending(p => p.PublishedAt)
            .FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException(
                $"No active published prompt found for key '{promptKey}'.");
    }

    /// <inheritdoc />
    public async Task<List<PromptRegistry>> ListVersionsAsync(
        string promptKey, CancellationToken ct)
    {
        return await _db.PromptRegistries
            .Where(p => p.PromptKey == promptKey)
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync(ct);
    }

    /// <inheritdoc />
    public async Task<PromptRegistry> PublishAsync(Guid id, CancellationToken ct)
    {
        var prompt = await _db.PromptRegistries
            .FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw new InvalidOperationException($"Prompt with ID '{id}' not found.");

        if (prompt.Status == PromptRegistryStatuses.Archived)
        {
            throw new InvalidOperationException(
                "Cannot publish an archived prompt. Create a new version instead.");
        }

        // Archive any currently active prompt for the same key + language
        var activePrompts = await _db.PromptRegistries
            .Where(p => p.PromptKey == prompt.PromptKey
                        && p.IsActive
                        && p.Id != id)
            .ToListAsync(ct);

        foreach (var active in activePrompts)
        {
            active.IsActive = false;
            active.Status = PromptRegistryStatuses.Archived;
            active.UpdatedAt = DateTime.UtcNow;
        }

        prompt.Status = PromptRegistryStatuses.Published;
        prompt.IsActive = true;
        prompt.PublishedAt = DateTime.UtcNow;
        prompt.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Published prompt {PromptKey} version {Version} (ID: {Id})",
            prompt.PromptKey, prompt.Version, prompt.Id);

        return prompt;
    }

    /// <inheritdoc />
    public async Task<PromptRegistry> ArchiveAsync(Guid id, CancellationToken ct)
    {
        var prompt = await _db.PromptRegistries
            .FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw new InvalidOperationException($"Prompt with ID '{id}' not found.");

        prompt.Status = PromptRegistryStatuses.Archived;
        prompt.IsActive = false;
        prompt.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Archived prompt {PromptKey} version {Version} (ID: {Id})",
            prompt.PromptKey, prompt.Version, prompt.Id);

        return prompt;
    }

    /// <inheritdoc />
    public async Task<PromptRegistry> CreateAsync(PromptRegistry prompt, CancellationToken ct)
    {
        if (prompt.Id == Guid.Empty)
        {
            prompt.Id = Guid.NewGuid();
        }

        prompt.Status = PromptRegistryStatuses.Draft;
        prompt.IsActive = false;
        prompt.CreatedAt = DateTime.UtcNow;
        prompt.UpdatedAt = DateTime.UtcNow;

        _db.PromptRegistries.Add(prompt);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Created prompt draft {PromptKey} version {Version} (ID: {Id})",
            prompt.PromptKey, prompt.Version, prompt.Id);

        return prompt;
    }

    /// <inheritdoc />
    public async Task<PromptRegistry?> ResolveForProviderAsync(
        string promptKey, string providerId, CancellationToken ct)
    {
        // First, try to find a published prompt that explicitly lists the provider
        var providerSpecific = _db.PromptRegistries
            .Where(p => p.PromptKey == promptKey
                        && p.Status == PromptRegistryStatuses.Published
                        && p.IsActive
                        && p.ProviderCompatibility != null
                        && p.ProviderCompatibility != "")
            .AsEnumerable()
            .Where(p => p.ProviderCompatibility
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Contains(providerId, StringComparer.OrdinalIgnoreCase))
            .OrderByDescending(p => p.PublishedAt)
            .FirstOrDefault();

        if (providerSpecific != null)
        {
            return providerSpecific;
        }

        // Fall back to a prompt with empty ProviderCompatibility (compatible with all)
        return await _db.PromptRegistries
            .Where(p => p.PromptKey == promptKey
                        && p.Status == PromptRegistryStatuses.Published
                        && p.IsActive
                        && (p.ProviderCompatibility == null || p.ProviderCompatibility == ""))
            .OrderByDescending(p => p.PublishedAt)
            .FirstOrDefaultAsync(ct);
    }
}
