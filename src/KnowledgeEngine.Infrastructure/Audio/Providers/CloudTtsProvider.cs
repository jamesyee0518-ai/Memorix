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
/// Memorix cloud TTS provider using the hosted cloud speech-synthesis API.
/// Text is sent off-device to the Memorix cloud TTS service (default
/// <c>https://tts.memorix.cloud</c>) which returns synthesized audio bytes.
/// Supports platform-managed credentials (Memorix-issued API key) and
/// tenant BYOK credentials resolved via <see cref="ICredentialManager"/>.
/// </summary>
public class CloudTtsProvider : ITtsProvider
{
    private const string ProviderIdValue = "cloud_tts";
    private const string ModelIdValue = "memorix-cloud-tts";
    private const string HttpClientName = "CloudTts";

    /// <summary>
    /// Relative path appended to the configured base URL for batch synthesis.
    /// </summary>
    private const string SynthesizePath = "/v1/tts";

    /// <summary>
    /// Relative path appended to the configured base URL for streaming synthesis.
    /// </summary>
    private const string StreamPath = "/v1/tts/stream";

    /// <summary>
    /// Environment variable consulted as a last-resort platform-managed API key.
    /// </summary>
    private const string PlatformApiKeyEnvVar = "MEMORIX_CLOUD_TTS_API_KEY";

    private static readonly TimeSpan RequestTimeout = TimeSpan.FromMinutes(5);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ICredentialManager _credentialManager;
    private readonly AudioSettings _settings;
    private readonly ILogger<CloudTtsProvider> _logger;

    /// <summary>
    /// Creates a new <see cref="CloudTtsProvider"/>.
    /// </summary>
    /// <param name="httpClientFactory">HTTP client factory for creating the named <c>CloudTts</c> client.</param>
    /// <param name="credentialManager">Credential manager for resolving BYOK API keys.</param>
    /// <param name="settings">Audio settings containing the configurable cloud TTS base URL.</param>
    /// <param name="logger">Logger for diagnostics.</param>
    public CloudTtsProvider(
        IHttpClientFactory httpClientFactory,
        ICredentialManager credentialManager,
        IOptions<AudioSettings> settings,
        ILogger<CloudTtsProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _credentialManager = credentialManager;
        _settings = settings.Value;
        _logger = logger;
    }

    /// <summary>
    /// Returns the resolved cloud TTS API base URL (trailing slash removed).
    /// </summary>
    private string BaseUrl => _settings.CloudTtsBaseUrl.TrimEnd('/');

    /// <summary>
    /// Returns the default voice ID from settings.
    /// </summary>
    private string DefaultVoice => _settings.TtsDefaultVoice;

    /// <inheritdoc/>
    public Task<TtsProviderDescriptor> GetDescriptorAsync(CancellationToken ct)
    {
        var descriptor = new TtsProviderDescriptor
        {
            ProviderId = ProviderIdValue,
            ModelId = ModelIdValue,
            ExecutionModes = [ExecutionMode.MEMORIX_CLOUD],
            CredentialModes = [CredentialMode.PLATFORM_MANAGED, CredentialMode.TENANT_BYOK],
            SupportedLanguages = ["zh", "en"],
            SupportsStreaming = true,
            SupportsBatch = true,
            SupportsVoiceCloning = false,
            SupportsSpeedControl = true,
            SupportsPitchControl = true,
            OutputFormats = ["wav", "mp3"],
            SupportedSampleRates = [22050, 44100, 16000],
            SendsAudioOffDevice = true,
            StoresProviderData = ProviderDataRetention.NO,
            DataRegion = "cn",
            RetentionPolicy = "Memorix cloud does not retain audio after synthesis completes.",
            PricingUnit = PricingUnits.Request
        };

        return Task.FromResult(descriptor);
    }

    /// <inheritdoc/>
    public Task<ValidationResult> ValidateRequestAsync(TtsRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Text))
        {
            return Task.FromResult(ValidationResult.Fail("Text is required for TTS synthesis."));
        }

        if (string.IsNullOrWhiteSpace(_settings.CloudTtsBaseUrl))
        {
            return Task.FromResult(ValidationResult.Fail(
                "Cloud TTS base URL is not configured (Audio:CloudTtsBaseUrl)."));
        }

        return Task.FromResult(ValidationResult.Ok());
    }

    /// <inheritdoc/>
    public async Task<TtsResult> SynthesizeAsync(TtsRequest request, CancellationToken ct)
    {
        // ── Resolve API key based on the preferred (or default) credential mode ──

        var credentialMode = request.PreferredCredentialMode
                             ?? CredentialMode.PLATFORM_MANAGED;

        var apiKey = await ResolveApiKeyAsync(credentialMode, request, ct);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                $"No active cloud TTS API key found for credential mode {credentialMode}. " +
                "Ensure a valid credential is stored or a platform-managed key is configured.");
        }

        // ── Build the JSON request ──

        var httpClient = _httpClientFactory.CreateClient(HttpClientName);
        httpClient.Timeout = RequestTimeout;

        var payload = new CloudTtsRequest
        {
            Text = request.Text,
            Language = request.Language ?? "zh",
            VoiceId = request.VoiceId ?? DefaultVoice,
            Speed = request.Speed > 0 ? (double)request.Speed : 1.0,
            Pitch = request.Pitch > 0 ? (double)request.Pitch : 1.0,
            Format = request.OutputFormat ?? "wav",
            SampleRate = request.SampleRate
        };

        var json = JsonSerializer.Serialize(payload, JsonOptions);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}{SynthesizePath}")
        {
            Content = content
        };
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        _logger.LogInformation(
            "Calling Memorix cloud TTS API for {TextLength} chars, voice={Voice}, credentialMode={CredentialMode}",
            request.Text.Length, payload.VoiceId, credentialMode);

        // ── Send the request ──

        var sw = Stopwatch.StartNew();
        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(httpRequest, ct);
        }
        catch (HttpRequestException ex)
        {
            sw.Stop();
            _logger.LogError(ex,
                "Cloud TTS API network error after {ElapsedMs}ms: {Message}",
                sw.ElapsedMilliseconds, ex.Message);
            throw new InvalidOperationException(
                $"Cloud TTS API network error: {ex.Message}", ex);
        }
        sw.Stop();

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError(
                "Cloud TTS API returned {StatusCode} after {ElapsedMs}ms: {ErrorBody}",
                response.StatusCode, sw.ElapsedMilliseconds, errorBody);
            throw new InvalidOperationException(
                $"Cloud TTS API returned status {(int)response.StatusCode}: {errorBody}");
        }

        // ── Read the audio bytes and persist to a temp file ──

        var audioBytes = await response.Content.ReadAsByteArrayAsync(ct);

        var outputPath = Path.Combine(Path.GetTempPath(),
            $"memorix-tts-cloud-{Guid.NewGuid():N}.{payload.Format}");
        await File.WriteAllBytesAsync(outputPath, audioBytes, ct);

        _logger.LogInformation(
            "Cloud TTS synthesis completed: {SizeBytes} bytes in {ElapsedMs}ms",
            audioBytes.Length, sw.ElapsedMilliseconds);

        return new TtsResult
        {
            ProviderId = ProviderIdValue,
            ModelId = ModelIdValue,
            OutputFilePath = outputPath,
            OutputFormat = payload.Format,
            FileSizeBytes = audioBytes.Length,
            VoiceId = payload.VoiceId,
            Metadata = new Dictionary<string, object>
            {
                ["engine"] = "cloud_tts",
                ["model"] = ModelIdValue,
                ["credential_mode"] = credentialMode.ToString(),
                ["latency_ms"] = sw.ElapsedMilliseconds
            }
        };
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<AudioChunk> SynthesizeStream(
        TtsStreamRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        // ── Resolve API key (streaming always uses platform-managed credentials) ──

        var credentialMode = CredentialMode.PLATFORM_MANAGED;

        var apiKey = await ResolveApiKeyAsync(credentialMode,
            new TtsRequest { TenantId = Guid.Empty }, ct);

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogError(
                "Cloud TTS streaming aborted: no platform-managed API key available.");
            yield break;
        }

        var httpClient = _httpClientFactory.CreateClient(HttpClientName);
        httpClient.Timeout = RequestTimeout;

        var payload = new CloudTtsStreamRequest
        {
            Text = request.Text,
            Language = request.Language ?? "zh",
            VoiceId = request.VoiceId ?? DefaultVoice,
            Speed = request.Speed > 0 ? (double)request.Speed : 1.0,
            SampleRate = request.SampleRate,
            Stream = true
        };

        var json = JsonSerializer.Serialize(payload, JsonOptions);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}{StreamPath}")
        {
            Content = content
        };
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        _logger.LogInformation(
            "Cloud TTS streaming synthesis for {TextLength} chars, session={SessionId}",
            request.Text.Length, request.SessionId);

        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(
                httpRequest, HttpCompletionOption.ResponseHeadersRead, ct);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Cloud TTS streaming API network error: {Message}", ex.Message);
            yield break;
        }

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError("Cloud TTS streaming API returned {StatusCode}: {ErrorBody}",
                response.StatusCode, errorBody);
            yield break;
        }

        // Read the response stream in chunks for low-latency playback.
        const int chunkSize = 32 * 1024; // 32 KB per chunk
        var buffer = new byte[chunkSize];
        var chunkIndex = 0;

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        int bytesRead;

        while ((bytesRead = await stream.ReadAsync(buffer, ct)) > 0)
        {
            var data = bytesRead == buffer.Length
                ? buffer
                : buffer.AsSpan(0, bytesRead).ToArray();

            yield return new AudioChunk
            {
                SessionId = request.SessionId,
                Data = data,
                ChunkIndex = chunkIndex++,
                IsFinal = false,
                Format = "pcm_s16le",
                SampleRate = request.SampleRate
            };
        }

        // Final marker chunk.
        yield return new AudioChunk
        {
            SessionId = request.SessionId,
            Data = Array.Empty<byte>(),
            ChunkIndex = chunkIndex,
            IsFinal = true,
            Format = "pcm_s16le",
            SampleRate = request.SampleRate
        };
    }

    /// <inheritdoc/>
    public Task<List<VoiceProfile>> ListVoicesAsync(CancellationToken ct)
    {
        var voices = new List<VoiceProfile>
        {
            new()
            {
                VoiceId = "cloud-zh-female",
                Name = "Memorix Cloud Female (Chinese)",
                Language = "zh",
                Gender = "female",
                IsClonable = false
            },
            new()
            {
                VoiceId = "cloud-zh-male",
                Name = "Memorix Cloud Male (Chinese)",
                Language = "zh",
                Gender = "male",
                IsClonable = false
            },
            new()
            {
                VoiceId = "cloud-en-female",
                Name = "Memorix Cloud Female (English)",
                Language = "en",
                Gender = "female",
                IsClonable = false
            }
        };

        return Task.FromResult(voices);
    }

    /// <inheritdoc/>
    public async Task<CostEstimate>? EstimateCostAsync(TtsRequest request, CancellationToken ct)
    {
        // Estimate cost based on the number of characters and the configured per-request rate.
        var charCount = request.Text?.Length ?? 0;

        var ratePerRequest = _settings.ProviderPricingRates.TryGetValue(
            $"{ProviderIdValue}:{PricingUnits.Request}",
            out var providerRate)
            ? providerRate
            : _settings.ProviderPricingRates.GetValueOrDefault(PricingUnits.Request, 0.10m);

        // Scale the per-request rate by character count (100 chars = 1 unit).
        var units = charCount / 100m;

        return await Task.FromResult(new CostEstimate
        {
            ProviderId = ProviderIdValue,
            ModelId = ModelIdValue,
            PricingUnit = PricingUnits.Request,
            Units = units,
            EstimatedCost = Math.Round(units * ratePerRequest, 4),
            Currency = "CNY"
        });
    }

    /// <inheritdoc/>
    public async Task<ProviderHealth> HealthCheckAsync(CancellationToken ct)
    {
        var health = new ProviderHealth
        {
            ProviderId = ProviderIdValue,
            CheckedAt = DateTime.UtcNow
        };

        if (!_settings.CloudTtsEnabled)
        {
            health.IsHealthy = false;
            health.StatusMessage = "Cloud TTS is disabled in configuration.";
            return health;
        }

        var httpClient = _httpClientFactory.CreateClient(HttpClientName);
        httpClient.Timeout = TimeSpan.FromSeconds(10);

        var sw = Stopwatch.StartNew();
        try
        {
            using var response = await httpClient.GetAsync($"{BaseUrl}/health", ct);
            sw.Stop();

            health.IsHealthy = response.IsSuccessStatusCode;
            health.LatencyMs = sw.ElapsedMilliseconds;
            health.StatusMessage = health.IsHealthy
                ? "Cloud TTS endpoint is healthy"
                : $"Health check returned {response.StatusCode}";
        }
        catch (Exception ex)
        {
            sw.Stop();
            health.IsHealthy = false;
            health.LatencyMs = sw.ElapsedMilliseconds;
            health.StatusMessage = $"Cloud TTS health check failed: {ex.Message}";
        }

        return health;
    }

    // ── Private helpers ──

    /// <summary>
    /// Resolves the cloud TTS API key based on the credential mode.
    /// <para>
    /// <list type="bullet">
    /// <item><b>TENANT_BYOK</b>: looks up the active tenant credential via <see cref="ICredentialManager"/>.</item>
    /// <item><b>PLATFORM_MANAGED</b>: uses <see cref="AudioSettings.CloudTtsPlatformApiKey"/> or the
    /// <c>MEMORIX_CLOUD_TTS_API_KEY</c> environment variable.</item>
    /// </list>
    /// </para>
    /// </summary>
    /// <param name="mode">The credential mode to resolve.</param>
    /// <param name="request">The TTS request (contains tenant ID for BYOK).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The decrypted API key, or null if no credential was found.</returns>
    private async Task<string?> ResolveApiKeyAsync(
        CredentialMode mode,
        TtsRequest request,
        CancellationToken ct)
    {
        switch (mode)
        {
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
                        "No active cloud TTS credential found for tenant {TenantId}.", tenantId);
                    return null;
                }

                return await _credentialManager.GetSecretAsync(credential.Id, ct);
            }

            case CredentialMode.PLATFORM_MANAGED:
            {
                // Prefer the configured platform key, then the environment variable.
                var platformKey = !string.IsNullOrWhiteSpace(_settings.CloudTtsPlatformApiKey)
                    ? _settings.CloudTtsPlatformApiKey
                    : Environment.GetEnvironmentVariable(PlatformApiKeyEnvVar);

                if (string.IsNullOrWhiteSpace(platformKey))
                {
                    _logger.LogWarning(
                        "PLATFORM_MANAGED credential mode but no platform cloud TTS API key " +
                        "configured (Audio:CloudTtsPlatformApiKey or {EnvVar} env var).",
                        PlatformApiKeyEnvVar);
                }

                return platformKey;
            }

            default:
                _logger.LogWarning(
                    "Unsupported credential mode {Mode} for cloud TTS provider.", mode);
                return null;
        }
    }

    // ── Cloud TTS API request DTOs ──

    private sealed class CloudTtsRequest
    {
        [JsonPropertyName("text")]
        public string Text { get; set; } = string.Empty;

        [JsonPropertyName("language")]
        public string Language { get; set; } = "zh";

        [JsonPropertyName("voice_id")]
        public string VoiceId { get; set; } = "default";

        [JsonPropertyName("speed")]
        public double Speed { get; set; } = 1.0;

        [JsonPropertyName("pitch")]
        public double Pitch { get; set; } = 1.0;

        [JsonPropertyName("format")]
        public string Format { get; set; } = "wav";

        [JsonPropertyName("sample_rate")]
        public int SampleRate { get; set; } = 22050;
    }

    private sealed class CloudTtsStreamRequest
    {
        [JsonPropertyName("text")]
        public string Text { get; set; } = string.Empty;

        [JsonPropertyName("language")]
        public string Language { get; set; } = "zh";

        [JsonPropertyName("voice_id")]
        public string VoiceId { get; set; } = "default";

        [JsonPropertyName("speed")]
        public double Speed { get; set; } = 1.0;

        [JsonPropertyName("sample_rate")]
        public int SampleRate { get; set; } = 22050;

        [JsonPropertyName("stream")]
        public bool Stream { get; set; } = true;
    }
}
