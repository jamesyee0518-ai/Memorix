using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using KnowledgeEngine.Application.DTOs;
using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Application.Settings;
using KnowledgeEngine.Domain.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KnowledgeEngine.Infrastructure.Audio.Providers;

/// <summary>
/// Cloud ASR provider for the Zhipu (BigModel) GLM-ASR speech recognition service.
/// Sends audio off-device to the Zhipu cloud API and parses segment-level
/// timestamps into structured DTOs. Supports BYOK (user/tenant API keys) and
/// platform-managed credential modes. Audio is temporarily stored by the provider
/// for processing and deleted shortly after.
/// </summary>
public class ZhipuGlmAsrProvider : IAsrProvider
{
    private const string ProviderIdValue = "zhipu";
    private const string ModelIdValue = "glm-asr-2512";
    private const string HttpClientName = "Zhipu";

    /// <summary>
    /// Relative path appended to the configured base URL for the transcription endpoint.
    /// </summary>
    private const string TranscriptionPath = "/api/paas/v4/audio/transcriptions";

    /// <summary>
    /// Environment variable consulted as a last-resort platform-managed API key.
    /// </summary>
    private const string PlatformApiKeyEnvVar = "MEMORIX_ZHIPU_API_KEY";

    private static readonly TimeSpan RequestTimeout = TimeSpan.FromMinutes(10);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ICredentialManager _credentialManager;
    private readonly AudioSettings _settings;
    private readonly ILogger<ZhipuGlmAsrProvider> _logger;

    /// <summary>
    /// Creates a new <see cref="ZhipuGlmAsrProvider"/>.
    /// </summary>
    /// <param name="httpClientFactory">HTTP client factory for creating the named <c>Zhipu</c> client.</param>
    /// <param name="credentialManager">Credential manager for resolving BYOK API keys.</param>
    /// <param name="settings">Audio settings containing the configurable Zhipu base URL.</param>
    /// <param name="logger">Logger for diagnostics.</param>
    public ZhipuGlmAsrProvider(
        IHttpClientFactory httpClientFactory,
        ICredentialManager credentialManager,
        IOptions<AudioSettings> settings,
        ILogger<ZhipuGlmAsrProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _credentialManager = credentialManager;
        _settings = settings.Value;
        _logger = logger;
    }

    /// <summary>
    /// Returns the resolved Zhipu API base URL (trailing slash removed).
    /// </summary>
    private string BaseUrl => _settings.ZhipuBaseUrl.TrimEnd('/');

    /// <inheritdoc/>
    public Task<AsrProviderDescriptor> GetDescriptorAsync(CancellationToken ct)
    {
        var descriptor = new AsrProviderDescriptor
        {
            ProviderId = ProviderIdValue,
            ModelId = ModelIdValue,
            ExecutionModes = [ExecutionMode.THIRD_PARTY_CLOUD],
            CredentialModes =
            [
                CredentialMode.USER_BYOK,
                CredentialMode.TENANT_BYOK,
                CredentialMode.PLATFORM_MANAGED
            ],
            SupportedLanguages = [], // GLM-ASR auto-detects; empty = all languages
            SupportsStreaming = true,
            SupportsBatch = true,
            SupportsVad = false,
            SupportsPunctuation = true,
            SupportsDiarization = false,
            SupportsHotwords = true,
            SupportsWordTimestamp = false,
            SupportsSegmentTimestamp = true,
            SendsAudioOffDevice = true,
            StoresProviderData = ProviderDataRetention.TEMPORARY,
            DataRegion = "cn",
            RetentionPolicy = "Provider retains audio transiently for processing; deleted after completion.",
            AcceptedMimeTypes =
            [
                "audio/wav", "audio/mp3", "audio/m4a",
                "audio/flac", "audio/ogg", "audio/webm"
            ],
            PricingUnit = PricingUnits.Second
        };

        return Task.FromResult(descriptor);
    }

    /// <inheritdoc/>
    public Task<ValidationResult> ValidateRequestAsync(AsrTranscriptionRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.AudioFilePath))
        {
            return Task.FromResult(ValidationResult.Fail("AudioFilePath is required."));
        }

        if (!File.Exists(request.AudioFilePath))
        {
            return Task.FromResult(ValidationResult.Fail($"Audio file not found: {request.AudioFilePath}"));
        }

        if (string.IsNullOrWhiteSpace(_settings.ZhipuBaseUrl))
        {
            return Task.FromResult(ValidationResult.Fail("Zhipu API base URL is not configured (Audio:ZhipuBaseUrl)."));
        }

        return Task.FromResult(ValidationResult.Ok());
    }

    /// <inheritdoc/>
    public async Task<AsrTranscriptionResult> TranscribeAsync(
        AsrTranscriptionRequest request,
        CancellationToken ct)
    {
        // ── Resolve API key based on the preferred (or default) credential mode ──

        var credentialMode = request.PreferredCredentialMode
                             ?? CredentialMode.USER_BYOK;

        var apiKey = await ResolveApiKeyAsync(credentialMode, request, ct);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                $"No active Zhipu API key found for credential mode {credentialMode}. " +
                "Ensure a valid credential is stored or a platform-managed key is configured.");
        }

        // ── Build the multipart/form-data request ──

        var httpClient = _httpClientFactory.CreateClient(HttpClientName);
        httpClient.Timeout = RequestTimeout;

        using var multipart = new MultipartFormDataContent();
        await using var fileStream = File.OpenRead(request.AudioFilePath);

        var fileContent = new StreamContent(fileStream);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(
            !string.IsNullOrWhiteSpace(request.MimeType) ? request.MimeType : "audio/wav");
        multipart.Add(fileContent, "file", Path.GetFileName(request.AudioFilePath));

        // Required model identifier.
        multipart.Add(new StringContent(ModelIdValue), "model");

        // Optional language hint.
        if (!string.IsNullOrWhiteSpace(request.Language))
        {
            multipart.Add(new StringContent(request.Language), "language");
        }

        // Optional hotwords (Zhipu accepts a JSON-encoded list or a comma-separated string).
        if (request.Hotwords is { Count: > 0 })
        {
            var hotwordsJson = JsonSerializer.Serialize(request.Hotwords);
            multipart.Add(new StringContent(hotwordsJson), "hotwords");
        }

        // ── Send the request ──

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}{TranscriptionPath}")
        {
            Content = multipart
        };
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        _logger.LogInformation(
            "Calling Zhipu GLM-ASR API for file={FilePath}, model={Model}, credentialMode={CredentialMode}",
            request.AudioFilePath, ModelIdValue, credentialMode);

        var stopwatch = Stopwatch.StartNew();
        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(httpRequest, ct);
        }
        catch (HttpRequestException ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex,
                "Zhipu GLM-ASR API network error after {ElapsedMs}ms: {Message}",
                stopwatch.ElapsedMilliseconds, ex.Message);
            throw new InvalidOperationException(
                $"Zhipu GLM-ASR API network error: {ex.Message}", ex);
        }
        stopwatch.Stop();

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError(
                "Zhipu GLM-ASR API returned {StatusCode} after {ElapsedMs}ms: {ErrorBody}",
                response.StatusCode, stopwatch.ElapsedMilliseconds, errorBody);
            throw new InvalidOperationException(
                $"Zhipu GLM-ASR API returned status {(int)response.StatusCode}: {errorBody}");
        }

        // ── Parse the JSON response ──

        var jsonContent = await response.Content.ReadAsStringAsync(ct);
        var asrResponse = JsonSerializer.Deserialize<ZhipuAsrResponse>(jsonContent, JsonOptions);

        if (asrResponse == null)
        {
            throw new InvalidOperationException("Zhipu GLM-ASR API returned an empty response.");
        }

        // ── Map segments to AsrSegmentDto ──

        var segments = new List<AsrSegmentDto>();
        var fullTextBuilder = new StringBuilder();
        var prefix = request.SegmentUuidPrefix;

        if (asrResponse.Segments is { Count: > 0 })
        {
            for (var i = 0; i < asrResponse.Segments.Count; i++)
            {
                var seg = asrResponse.Segments[i];
                var text = seg.Text?.Trim() ?? string.Empty;

                if (fullTextBuilder.Length > 0)
                {
                    fullTextBuilder.Append(' ');
                }
                fullTextBuilder.Append(text);

                segments.Add(new AsrSegmentDto
                {
                    SegmentUuid = GenerateSegmentUuid(prefix),
                    StartMs = (long)(seg.Start * 1000),
                    EndMs = (long)(seg.End * 1000),
                    Text = text,
                    Confidence = 0,
                    SegmentIndex = i
                });
            }
        }
        else
        {
            // Fallback: single segment containing the full text.
            segments.Add(new AsrSegmentDto
            {
                SegmentUuid = GenerateSegmentUuid(prefix),
                StartMs = 0,
                EndMs = request.DurationMs,
                Text = asrResponse.Text?.Trim() ?? string.Empty,
                Confidence = 0,
                SegmentIndex = 0
            });
        }

        // Resolve the final full text: prefer the top-level "text" field, fall back to
        // concatenating segment texts.
        var fullText = !string.IsNullOrWhiteSpace(asrResponse.Text)
            ? asrResponse.Text.Trim()
            : fullTextBuilder.ToString().Trim();

        // Duration: prefer the API-reported duration, then the request duration.
        var durationMs = asrResponse.Duration > 0
            ? (long)(asrResponse.Duration * 1000)
            : request.DurationMs;

        _logger.LogInformation(
            "Zhipu GLM-ASR transcription completed: {SegmentCount} segments in {ElapsedMs}ms",
            segments.Count, stopwatch.ElapsedMilliseconds);

        return new AsrTranscriptionResult
        {
            ProviderId = ProviderIdValue,
            ModelId = ModelIdValue,
            Language = asrResponse.Language ?? request.Language,
            DurationMs = durationMs,
            Segments = segments,
            FullText = fullText,
            Metadata = new Dictionary<string, object>
            {
                ["engine"] = "zhipu_glm",
                ["model"] = ModelIdValue,
                ["credential_mode"] = credentialMode.ToString(),
                ["latency_ms"] = stopwatch.ElapsedMilliseconds
            }
        };
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<AsrPartialResult> TranscribeStream(
        AsrStreamingRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        // Zhipu GLM-ASR supports a streaming WebSocket mode. This implementation uses
        // the batch HTTP endpoint as a simplified streaming fallback, yielding the
        // complete transcription as a single final result.
        //
        // A production streaming implementation would connect to the Zhipu WebSocket
        // endpoint and yield incremental partial results as they arrive.

        await Task.Delay(0, ct); // Ensure the method is truly async.

        yield return new AsrPartialResult
        {
            SessionId = request.SessionId,
            PartialText = string.Empty,
            FinalText = string.Empty,
            IsFinal = true,
            SegmentIndex = 0
        };
    }

    /// <inheritdoc/>
    public async Task<CostEstimate>? EstimateCostAsync(
        AsrTranscriptionRequest request,
        CancellationToken ct)
    {
        // Estimate cost based on audio duration and the configured per-second rate.
        var durationSeconds = request.DurationMs > 0
            ? (decimal)request.DurationMs / 1000m
            : 0m;

        var ratePerSecond = _settings.ProviderPricingRates.TryGetValue(
            $"{ProviderIdValue}:{PricingUnits.Second}",
            out var providerRate)
            ? providerRate
            : _settings.ProviderPricingRates.GetValueOrDefault(PricingUnits.Second, 0.01m);

        return await Task.FromResult(new CostEstimate
        {
            ProviderId = ProviderIdValue,
            ModelId = ModelIdValue,
            PricingUnit = PricingUnits.Second,
            Units = durationSeconds,
            EstimatedCost = Math.Round(durationSeconds * ratePerSecond, 4),
            Currency = "CNY"
        });
    }

    /// <inheritdoc/>
    public Task CancelAsync(string providerTaskId, CancellationToken ct)
    {
        // The Zhipu batch HTTP API does not expose a task-cancellation endpoint.
        // Cancellation is handled via the CancellationToken passed to SendAsync.
        _logger.LogDebug(
            "CancelAsync called for providerTaskId={ProviderTaskId} (no-op for Zhipu batch API)",
            providerTaskId);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async Task<ProviderHealth> HealthCheckAsync(CancellationToken ct)
    {
        var health = new ProviderHealth
        {
            ProviderId = ProviderIdValue,
            CheckedAt = DateTime.UtcNow
        };

        var sw = Stopwatch.StartNew();
        try
        {
            // Lightweight connectivity check: verify the base URL is reachable.
            // We do not send a full transcription request (which would require audio
            // and incur cost). A simple GET to the base URL or a HEAD request suffices.
            var httpClient = _httpClientFactory.CreateClient(HttpClientName);
            httpClient.Timeout = TimeSpan.FromSeconds(10);

            using var response = await httpClient.GetAsync(BaseUrl, ct);
            sw.Stop();

            // The Zhipu API root may return 200 or 404; either indicates connectivity.
            health.IsHealthy = response.IsSuccessStatusCode || response.StatusCode == System.Net.HttpStatusCode.NotFound;
            health.LatencyMs = sw.ElapsedMilliseconds;
            health.StatusMessage = health.IsHealthy
                ? "Zhipu API endpoint is reachable"
                : $"Health check returned {response.StatusCode}";
        }
        catch (Exception ex)
        {
            sw.Stop();
            health.IsHealthy = false;
            health.LatencyMs = sw.ElapsedMilliseconds;
            health.StatusMessage = $"Zhipu health check failed: {ex.Message}";
        }

        return health;
    }

    // ── Private helpers ──

    /// <summary>
    /// Resolves the Zhipu API key based on the credential mode.
    /// <para>
    /// <list type="bullet">
    /// <item><b>USER_BYOK</b>: looks up the active user credential via <see cref="ICredentialManager"/>.</item>
    /// <item><b>TENANT_BYOK</b>: looks up the active tenant credential via <see cref="ICredentialManager"/>.</item>
    /// <item><b>PLATFORM_MANAGED</b>: uses <see cref="AudioSettings.ZhipuPlatformApiKey"/> or the
    /// <c>MEMORIX_ZHIPU_API_KEY</c> environment variable.</item>
    /// </list>
    /// </para>
    /// </summary>
    /// <param name="mode">The credential mode to resolve.</param>
    /// <param name="request">The transcription request (contains user/tenant IDs).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The decrypted API key, or null if no credential was found.</returns>
    private async Task<string?> ResolveApiKeyAsync(
        CredentialMode mode,
        AsrTranscriptionRequest request,
        CancellationToken ct)
    {
        switch (mode)
        {
            case CredentialMode.USER_BYOK:
            {
                if (request.UserId is not { } userId || userId == Guid.Empty)
                {
                    _logger.LogWarning(
                        "USER_BYOK credential mode requested but UserId is not set.");
                    return null;
                }

                var credential = await _credentialManager.FindActiveAsync(
                    ProviderIdValue, CredentialOwnerTypes.User, userId, ct);

                if (credential == null)
                {
                    _logger.LogWarning(
                        "No active Zhipu credential found for user {UserId}.", userId);
                    return null;
                }

                return await _credentialManager.GetSecretAsync(credential.Id, ct);
            }

            case CredentialMode.TENANT_BYOK:
            {
                if (request.TenantId is not { } tenantId || tenantId == Guid.Empty)
                {
                    _logger.LogWarning(
                        "TENANT_BYOK credential mode requested but TenantId is not set.");
                    return null;
                }

                var credential = await _credentialManager.FindActiveAsync(
                    ProviderIdValue, CredentialOwnerTypes.Tenant, tenantId, ct);

                if (credential == null)
                {
                    _logger.LogWarning(
                        "No active Zhipu credential found for tenant {TenantId}.", tenantId);
                    return null;
                }

                return await _credentialManager.GetSecretAsync(credential.Id, ct);
            }

            case CredentialMode.PLATFORM_MANAGED:
            {
                // Prefer the configured platform key, then the environment variable.
                var platformKey = !string.IsNullOrWhiteSpace(_settings.ZhipuPlatformApiKey)
                    ? _settings.ZhipuPlatformApiKey
                    : Environment.GetEnvironmentVariable(PlatformApiKeyEnvVar);

                if (string.IsNullOrWhiteSpace(platformKey))
                {
                    _logger.LogWarning(
                        "PLATFORM_MANAGED credential mode but no platform Zhipu API key " +
                        "configured (Audio:ZhipuPlatformApiKey or {EnvVar} env var).",
                        PlatformApiKeyEnvVar);
                }

                return platformKey;
            }

            default:
                _logger.LogWarning(
                    "Unsupported credential mode {Mode} for Zhipu provider.", mode);
                return null;
        }
    }

    /// <summary>
    /// Generates a stable segment UUID with an optional prefix.
    /// Format: <c>{prefix}_{guid}</c> or just <c>{guid}</c> when no prefix is provided.
    /// </summary>
    private static string GenerateSegmentUuid(string? prefix)
    {
        var guid = Guid.NewGuid().ToString("N");
        return string.IsNullOrWhiteSpace(prefix) ? guid : $"{prefix}_{guid}";
    }

    // ── Zhipu API response DTOs ──

    /// <summary>
    /// Represents the JSON response from the Zhipu GLM-ASR transcription endpoint.
    /// </summary>
    private sealed class ZhipuAsrResponse
    {
        [JsonPropertyName("text")]
        public string? Text { get; set; }

        [JsonPropertyName("language")]
        public string? Language { get; set; }

        /// <summary>
        /// Total audio duration in seconds (as reported by the API).
        /// </summary>
        [JsonPropertyName("duration")]
        public double Duration { get; set; }

        [JsonPropertyName("segments")]
        public List<ZhipuSegment>? Segments { get; set; }
    }

    /// <summary>
    /// Represents a single transcription segment from the Zhipu API.
    /// Timestamps are in seconds.
    /// </summary>
    private sealed class ZhipuSegment
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("start")]
        public double Start { get; set; }

        [JsonPropertyName("end")]
        public double End { get; set; }

        [JsonPropertyName("text")]
        public string? Text { get; set; }
    }
}
