using KnowledgeEngine.Domain.Entities;

namespace KnowledgeEngine.Application.Interfaces;

/// <summary>
/// CRUD service for the unified model registry.
/// Manages registration, update, disable, and health status of audio models.
/// </summary>
public interface IModelRegistryService
{
    /// <summary>
    /// Lists registered models with optional filters.
    /// </summary>
    /// <param name="capability">Filter by capability (e.g. "audio.transcription"). Null returns all.</param>
    /// <param name="providerId">Filter by provider identifier. Null returns all.</param>
    /// <param name="enabledOnly">When true, only enabled models are returned.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<List<ModelRegistry>> ListAsync(string? capability, string? providerId, bool enabledOnly, CancellationToken ct);

    /// <summary>
    /// Gets a single model registration by ID.
    /// </summary>
    Task<ModelRegistry?> GetAsync(Guid id, CancellationToken ct);

    /// <summary>
    /// Registers a new model.
    /// </summary>
    Task<ModelRegistry> RegisterAsync(ModelRegistry model, CancellationToken ct);

    /// <summary>
    /// Updates an existing model registration.
    /// </summary>
    Task<ModelRegistry> UpdateAsync(Guid id, ModelRegistry model, CancellationToken ct);

    /// <summary>
    /// Disables a model registration (soft delete — sets IsEnabled to false).
    /// </summary>
    /// <returns>True if the model was found and disabled; false otherwise.</returns>
    Task<bool> DisableAsync(Guid id, CancellationToken ct);

    /// <summary>
    /// Updates the health status and last-check timestamp of a model.
    /// </summary>
    Task UpdateHealthStatusAsync(Guid id, string status, CancellationToken ct);
}
