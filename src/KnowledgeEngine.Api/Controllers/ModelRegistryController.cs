using KnowledgeEngine.Application.DTOs;
using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace KnowledgeEngine.Api.Controllers;

/// <summary>
/// CRUD API for the unified model registry.
/// Supports listing, retrieving, registering, updating, and disabling
/// audio model entries across all providers and capabilities.
/// </summary>
[ApiController]
[Route("api/audio/models")]
[Authorize]
public class ModelRegistryController : BaseController
{
    private readonly IModelRegistryService _service;
    private readonly ICurrentUserContext _currentUser;
    private readonly ILogger<ModelRegistryController> _logger;

    public ModelRegistryController(
        IModelRegistryService service,
        ICurrentUserContext currentUser,
        ILogger<ModelRegistryController> logger)
    {
        _service = service;
        _currentUser = currentUser;
        _logger = logger;
    }

    /// <summary>
    /// Lists registered models with optional filters.
    /// </summary>
    /// <param name="capability">Filter by capability (e.g. "audio.transcription").</param>
    /// <param name="providerId">Filter by provider identifier.</param>
    /// <param name="enabledOnly">When true, only enabled models are returned.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? capability,
        [FromQuery] string? providerId,
        [FromQuery] bool enabledOnly = false,
        CancellationToken ct = default)
    {
        if (!_currentUser.IsAuthenticated)
        {
            return Unauthorized(ApiResponse<object>.FailObject(
                "UNAUTHORIZED", "User not authenticated", GetTraceId()));
        }

        var models = await _service.ListAsync(capability, providerId, enabledOnly, ct);
        return Ok(ApiResponse<List<ModelRegistry>>.Ok(models, GetTraceId()));
    }

    /// <summary>
    /// Gets a single model registration by ID.
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
        {
            return Unauthorized(ApiResponse<object>.FailObject(
                "UNAUTHORIZED", "User not authenticated", GetTraceId()));
        }

        var model = await _service.GetAsync(id, ct);
        if (model == null)
        {
            return NotFound(ApiResponse<object>.FailObject(
                "MODEL_NOT_FOUND", $"Model with id {id} not found", GetTraceId()));
        }

        return Ok(ApiResponse<ModelRegistry>.Ok(model, GetTraceId()));
    }

    /// <summary>
    /// Registers a new model in the registry.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Register(
        [FromBody] RegisterModelRequest request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
        {
            return Unauthorized(ApiResponse<object>.FailObject(
                "UNAUTHORIZED", "User not authenticated", GetTraceId()));
        }

        if (string.IsNullOrWhiteSpace(request.ProviderId))
        {
            return BadRequest(ApiResponse<object>.FailObject(
                "MISSING_PROVIDER", "ProviderId is required", GetTraceId()));
        }

        if (string.IsNullOrWhiteSpace(request.ModelId))
        {
            return BadRequest(ApiResponse<object>.FailObject(
                "MISSING_MODEL", "ModelId is required", GetTraceId()));
        }

        if (string.IsNullOrWhiteSpace(request.Capability))
        {
            return BadRequest(ApiResponse<object>.FailObject(
                "MISSING_CAPABILITY", "Capability is required", GetTraceId()));
        }

        var model = MapFromRequest(request);
        var created = await _service.RegisterAsync(model, ct);

        return Ok(ApiResponse<ModelRegistry>.Ok(created, GetTraceId()));
    }

    /// <summary>
    /// Updates an existing model registration.
    /// </summary>
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(
        Guid id,
        [FromBody] RegisterModelRequest request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
        {
            return Unauthorized(ApiResponse<object>.FailObject(
                "UNAUTHORIZED", "User not authenticated", GetTraceId()));
        }

        if (string.IsNullOrWhiteSpace(request.ProviderId))
        {
            return BadRequest(ApiResponse<object>.FailObject(
                "MISSING_PROVIDER", "ProviderId is required", GetTraceId()));
        }

        if (string.IsNullOrWhiteSpace(request.ModelId))
        {
            return BadRequest(ApiResponse<object>.FailObject(
                "MISSING_MODEL", "ModelId is required", GetTraceId()));
        }

        if (string.IsNullOrWhiteSpace(request.Capability))
        {
            return BadRequest(ApiResponse<object>.FailObject(
                "MISSING_CAPABILITY", "Capability is required", GetTraceId()));
        }

        var model = MapFromRequest(request);

        try
        {
            var updated = await _service.UpdateAsync(id, model, ct);
            return Ok(ApiResponse<ModelRegistry>.Ok(updated, GetTraceId()));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(ApiResponse<object>.FailObject(
                "MODEL_NOT_FOUND", $"Model with id {id} not found", GetTraceId()));
        }
    }

    /// <summary>
    /// Disables a model registration (soft delete).
    /// </summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Disable(Guid id, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
        {
            return Unauthorized(ApiResponse<object>.FailObject(
                "UNAUTHORIZED", "User not authenticated", GetTraceId()));
        }

        var disabled = await _service.DisableAsync(id, ct);
        if (!disabled)
        {
            return NotFound(ApiResponse<object>.FailObject(
                "MODEL_NOT_FOUND", $"Model with id {id} not found", GetTraceId()));
        }

        return Ok(ApiResponse<object>.Ok(new { id, disabled = true }, GetTraceId()));
    }

    // ── Helpers ──

    private static ModelRegistry MapFromRequest(RegisterModelRequest request)
    {
        return new ModelRegistry
        {
            ProviderId = request.ProviderId,
            ModelId = request.ModelId,
            DisplayName = request.DisplayName ?? string.Empty,
            Capability = request.Capability,
            ExecutionModes = request.ExecutionModes ?? string.Empty,
            CredentialModes = request.CredentialModes ?? string.Empty,
            SupportedLanguages = request.SupportedLanguages ?? string.Empty,
            MaxFileBytes = request.MaxFileBytes,
            MaxAudioDurationMs = request.MaxAudioDurationMs,
            AcceptedMimeTypes = request.AcceptedMimeTypes ?? string.Empty,
            SupportsStreaming = request.SupportsStreaming,
            SupportsBatch = request.SupportsBatch,
            SupportsVad = request.SupportsVad,
            SupportsPunctuation = request.SupportsPunctuation,
            SupportsDiarization = request.SupportsDiarization,
            SupportsHotwords = request.SupportsHotwords,
            SupportsWordTimestamp = request.SupportsWordTimestamp,
            SupportsSegmentTimestamp = request.SupportsSegmentTimestamp,
            SendsAudioOffDevice = request.SendsAudioOffDevice,
            StoresProviderData = request.StoresProviderData,
            PricingUnit = request.PricingUnit,
            DataRegion = request.DataRegion,
            RetentionPolicy = request.RetentionPolicy,
            IsEnabled = request.IsEnabled,
            HealthStatus = string.IsNullOrWhiteSpace(request.HealthStatus)
                ? ModelRegistryStatuses.Unknown
                : request.HealthStatus,
        };
    }
}

// ── Controller Request DTOs ──

/// <summary>
/// Request payload for registering or updating a model registry entry.
/// </summary>
public class RegisterModelRequest
{
    public string ProviderId { get; set; } = string.Empty;
    public string ModelId { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string Capability { get; set; } = string.Empty;

    /// <summary>Comma-separated execution modes.</summary>
    public string? ExecutionModes { get; set; }

    /// <summary>Comma-separated credential modes.</summary>
    public string? CredentialModes { get; set; }

    /// <summary>Comma-separated supported languages.</summary>
    public string? SupportedLanguages { get; set; }

    public long? MaxFileBytes { get; set; }
    public long? MaxAudioDurationMs { get; set; }

    /// <summary>Comma-separated accepted MIME types.</summary>
    public string? AcceptedMimeTypes { get; set; }

    public bool SupportsStreaming { get; set; }
    public bool SupportsBatch { get; set; } = true;
    public bool SupportsVad { get; set; }
    public bool SupportsPunctuation { get; set; }
    public bool SupportsDiarization { get; set; }
    public bool SupportsHotwords { get; set; }
    public bool SupportsWordTimestamp { get; set; }
    public bool SupportsSegmentTimestamp { get; set; } = true;

    public bool SendsAudioOffDevice { get; set; }
    public bool StoresProviderData { get; set; }

    public string? PricingUnit { get; set; }
    public string? DataRegion { get; set; }
    public string? RetentionPolicy { get; set; }

    public bool IsEnabled { get; set; } = true;
    public string? HealthStatus { get; set; }
}
