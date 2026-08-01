using KnowledgeEngine.Application.DTOs;
using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace KnowledgeEngine.Api.Controllers;

/// <summary>
/// Text-to-Speech API.
/// Synthesize text to audio, list providers and voices, preview samples,
/// and check provider health.
/// </summary>
[ApiController]
[Route("api/tts")]
[Authorize]
public class TtsController : BaseController
{
    private readonly IProviderRegistry _providerRegistry;
    private readonly ICurrentUserContext _currentUser;
    private readonly IAppDbContext _db;
    private readonly ILogger<TtsController> _logger;

    public TtsController(
        IProviderRegistry providerRegistry,
        ICurrentUserContext currentUser,
        IAppDbContext db,
        ILogger<TtsController> logger)
    {
        _providerRegistry = providerRegistry;
        _currentUser = currentUser;
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// Synthesizes text to an audio file and returns the result
    /// including the output file path, duration, and estimated cost.
    /// </summary>
    [HttpPost("synthesize")]
    public async Task<IActionResult> Synthesize([FromBody] TtsRequest request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated || _currentUser.UserId == null)
        {
            return Unauthorized(ApiResponse<object>.FailObject(
                "UNAUTHORIZED", "User not authenticated", GetTraceId()));
        }

        var userId = _currentUser.UserId.Value;
        request.UserId = userId;

        if (string.IsNullOrWhiteSpace(request.Text))
        {
            return BadRequest(ApiResponse<object>.FailObject(
                "INVALID_REQUEST", "Text is required", GetTraceId()));
        }

        try
        {
            var provider = await ResolveProviderAsync(request.PreferredProviderId, ct);
            if (provider == null)
            {
                return BadRequest(ApiResponse<object>.FailObject(
                    "NO_PROVIDER", "No TTS provider available", GetTraceId()));
            }

            var validation = await provider.ValidateRequestAsync(request, ct);
            if (!validation.IsValid)
            {
                return BadRequest(ApiResponse<object>.FailObject(
                    "VALIDATION_FAILED",
                    string.Join("; ", validation.Errors),
                    GetTraceId()));
            }

            var result = await provider.SynthesizeAsync(request, ct);

            // Record usage for billing and audit
            var usageRecord = new ProviderUsageRecord
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                WorkspaceId = request.WorkspaceId,
                TenantId = request.TenantId,
                Capability = "audio.synthesis",
                ProviderId = result.ProviderId,
                ModelId = result.ModelId,
                DurationMs = result.DurationMs,
                RequestCount = 1,
                EstimatedCost = result.EstimatedCost,
                Status = "success",
                CreatedAt = DateTime.UtcNow
            };
            _db.ProviderUsageRecords.Add(usageRecord);
            await _db.SaveChangesAsync(ct);

            _logger.LogInformation(
                "TTS synthesis completed for user {UserId}: provider={ProviderId}, duration={DurationMs}ms",
                userId, result.ProviderId, result.DurationMs);

            return Ok(ApiResponse<TtsResult>.Ok(result, GetTraceId()));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TTS synthesis failed for user {UserId}", userId);
            return StatusCode(500, ApiResponse<object>.FailObject(
                "SYNTHESIS_FAILED", ex.Message, GetTraceId()));
        }
    }

    /// <summary>
    /// Lists all available TTS providers and their capability descriptors.
    /// </summary>
    [HttpGet("providers")]
    public async Task<IActionResult> ListProviders(CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
        {
            return Unauthorized(ApiResponse<object>.FailObject(
                "UNAUTHORIZED", "User not authenticated", GetTraceId()));
        }

        var descriptors = await _providerRegistry.GetTtsDescriptorsAsync(ct);
        return Ok(ApiResponse<List<TtsProviderDescriptor>>.Ok(descriptors, GetTraceId()));
    }

    /// <summary>
    /// Lists available voice profiles for a specific TTS provider.
    /// </summary>
    /// <param name="providerId">Provider identifier. If omitted, the first registered provider is used.</param>
    [HttpGet("voices")]
    public async Task<IActionResult> ListVoices([FromQuery] string? providerId, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
        {
            return Unauthorized(ApiResponse<object>.FailObject(
                "UNAUTHORIZED", "User not authenticated", GetTraceId()));
        }

        var provider = await ResolveProviderAsync(providerId, ct);
        if (provider == null)
        {
            return NotFound(ApiResponse<object>.FailObject(
                "PROVIDER_NOT_FOUND", "TTS provider not found", GetTraceId()));
        }

        var voices = await provider.ListVoicesAsync(ct);
        return Ok(ApiResponse<List<VoiceProfile>>.Ok(voices, GetTraceId()));
    }

    /// <summary>
    /// Previews TTS with a short text sample. Useful for testing voices
    /// and providers without incurring full synthesis cost tracking.
    /// </summary>
    [HttpPost("preview")]
    public async Task<IActionResult> Preview([FromBody] TtsRequest request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated || _currentUser.UserId == null)
        {
            return Unauthorized(ApiResponse<object>.FailObject(
                "UNAUTHORIZED", "User not authenticated", GetTraceId()));
        }

        var userId = _currentUser.UserId.Value;
        request.UserId = userId;

        // Use a default sample if no text is provided
        if (string.IsNullOrWhiteSpace(request.Text))
        {
            request.Text = "Hello, this is a text-to-speech preview sample.";
        }

        // Truncate to a reasonable preview length
        const int maxPreviewLength = 200;
        if (request.Text.Length > maxPreviewLength)
        {
            request.Text = request.Text[..maxPreviewLength];
        }

        try
        {
            var provider = await ResolveProviderAsync(request.PreferredProviderId, ct);
            if (provider == null)
            {
                return BadRequest(ApiResponse<object>.FailObject(
                    "NO_PROVIDER", "No TTS provider available", GetTraceId()));
            }

            var result = await provider.SynthesizeAsync(request, ct);

            // Preview does not record usage (it is a free test)

            _logger.LogInformation(
                "TTS preview completed for user {UserId}: provider={ProviderId}",
                userId, result.ProviderId);

            return Ok(ApiResponse<TtsResult>.Ok(result, GetTraceId()));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TTS preview failed for user {UserId}", userId);
            return StatusCode(500, ApiResponse<object>.FailObject(
                "PREVIEW_FAILED", ex.Message, GetTraceId()));
        }
    }

    /// <summary>
    /// Checks the health of a specific TTS provider.
    /// </summary>
    /// <param name="providerId">Provider identifier to check.</param>
    [HttpGet("health/{providerId}")]
    public async Task<IActionResult> HealthCheck(string providerId, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
        {
            return Unauthorized(ApiResponse<object>.FailObject(
                "UNAUTHORIZED", "User not authenticated", GetTraceId()));
        }

        var provider = await _providerRegistry.GetTtsProviderByIdAsync(providerId, ct);
        if (provider == null)
        {
            return NotFound(ApiResponse<object>.FailObject(
                "PROVIDER_NOT_FOUND",
                $"TTS provider '{providerId}' not found",
                GetTraceId()));
        }

        var health = await provider.HealthCheckAsync(ct);
        return Ok(ApiResponse<ProviderHealth>.Ok(health, GetTraceId()));
    }

    // ================================================================
    // Helpers
    // ================================================================

    /// <summary>
    /// Resolves a TTS provider by ID, or falls back to the first
    /// registered provider when no ID is supplied.
    /// </summary>
    private async Task<ITtsProvider?> ResolveProviderAsync(string? providerId, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(providerId))
        {
            return await _providerRegistry.GetTtsProviderByIdAsync(providerId, ct);
        }

        var providers = await _providerRegistry.GetTtsProvidersAsync(ct);
        return providers.FirstOrDefault();
    }
}
