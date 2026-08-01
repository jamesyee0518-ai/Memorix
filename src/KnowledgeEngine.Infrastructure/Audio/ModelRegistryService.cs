using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KnowledgeEngine.Infrastructure.Audio;

/// <summary>
/// EF Core-backed implementation of <see cref="IModelRegistryService"/>.
/// Manages the lifecycle of unified model registrations stored in the application database.
/// </summary>
public class ModelRegistryService : IModelRegistryService
{
    private readonly IAppDbContext _db;
    private readonly ILogger<ModelRegistryService> _logger;

    public ModelRegistryService(
        IAppDbContext db,
        ILogger<ModelRegistryService> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<List<ModelRegistry>> ListAsync(
        string? capability, string? providerId, bool enabledOnly, CancellationToken ct)
    {
        var query = _db.ModelRegistries.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(capability))
        {
            query = query.Where(m => m.Capability == capability);
        }

        if (!string.IsNullOrWhiteSpace(providerId))
        {
            query = query.Where(m => m.ProviderId == providerId);
        }

        if (enabledOnly)
        {
            query = query.Where(m => m.IsEnabled);
        }

        return await query
            .OrderByDescending(m => m.IsEnabled)
            .ThenBy(m => m.ProviderId)
            .ThenBy(m => m.ModelId)
            .ToListAsync(ct);
    }

    /// <inheritdoc/>
    public async Task<ModelRegistry?> GetAsync(Guid id, CancellationToken ct)
    {
        return await _db.ModelRegistries
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == id, ct);
    }

    /// <inheritdoc/>
    public async Task<ModelRegistry> RegisterAsync(ModelRegistry model, CancellationToken ct)
    {
        if (model.Id == Guid.Empty)
        {
            model.Id = Guid.NewGuid();
        }

        var now = DateTime.UtcNow;
        model.CreatedAt = now;
        model.UpdatedAt = now;

        if (string.IsNullOrEmpty(model.HealthStatus))
        {
            model.HealthStatus = ModelRegistryStatuses.Unknown;
        }

        _db.ModelRegistries.Add(model);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Registered model {ProviderId}/{ModelId} (capability={Capability}, id={Id})",
            model.ProviderId, model.ModelId, model.Capability, model.Id);

        return model;
    }

    /// <inheritdoc/>
    public async Task<ModelRegistry> UpdateAsync(Guid id, ModelRegistry model, CancellationToken ct)
    {
        var existing = await _db.ModelRegistries
            .FirstOrDefaultAsync(m => m.Id == id, ct);

        if (existing == null)
        {
            throw new KeyNotFoundException($"Model registry entry with id {id} not found.");
        }

        // Update all mutable fields (Id and CreatedAt are preserved).
        existing.ProviderId = model.ProviderId;
        existing.ModelId = model.ModelId;
        existing.DisplayName = model.DisplayName;
        existing.Capability = model.Capability;
        existing.ExecutionModes = model.ExecutionModes;
        existing.CredentialModes = model.CredentialModes;
        existing.SupportedLanguages = model.SupportedLanguages;
        existing.MaxFileBytes = model.MaxFileBytes;
        existing.MaxAudioDurationMs = model.MaxAudioDurationMs;
        existing.AcceptedMimeTypes = model.AcceptedMimeTypes;
        existing.SupportsStreaming = model.SupportsStreaming;
        existing.SupportsBatch = model.SupportsBatch;
        existing.SupportsVad = model.SupportsVad;
        existing.SupportsPunctuation = model.SupportsPunctuation;
        existing.SupportsDiarization = model.SupportsDiarization;
        existing.SupportsHotwords = model.SupportsHotwords;
        existing.SupportsWordTimestamp = model.SupportsWordTimestamp;
        existing.SupportsSegmentTimestamp = model.SupportsSegmentTimestamp;
        existing.SendsAudioOffDevice = model.SendsAudioOffDevice;
        existing.StoresProviderData = model.StoresProviderData;
        existing.PricingUnit = model.PricingUnit;
        existing.DataRegion = model.DataRegion;
        existing.RetentionPolicy = model.RetentionPolicy;
        existing.IsEnabled = model.IsEnabled;
        existing.HealthStatus = model.HealthStatus;
        existing.LastHealthCheckAt = model.LastHealthCheckAt;
        existing.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Updated model registry entry {Id} ({ProviderId}/{ModelId})",
            existing.Id, existing.ProviderId, existing.ModelId);

        return existing;
    }

    /// <inheritdoc/>
    public async Task<bool> DisableAsync(Guid id, CancellationToken ct)
    {
        var existing = await _db.ModelRegistries
            .FirstOrDefaultAsync(m => m.Id == id, ct);

        if (existing == null)
        {
            return false;
        }

        existing.IsEnabled = false;
        existing.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Disabled model registry entry {Id} ({ProviderId}/{ModelId})",
            existing.Id, existing.ProviderId, existing.ModelId);

        return true;
    }

    /// <inheritdoc/>
    public async Task UpdateHealthStatusAsync(Guid id, string status, CancellationToken ct)
    {
        var existing = await _db.ModelRegistries
            .FirstOrDefaultAsync(m => m.Id == id, ct);

        if (existing == null)
        {
            throw new KeyNotFoundException($"Model registry entry with id {id} not found.");
        }

        existing.HealthStatus = status;
        existing.LastHealthCheckAt = DateTime.UtcNow;
        existing.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Updated health status for model {Id} to {Status}", id, status);
    }
}
