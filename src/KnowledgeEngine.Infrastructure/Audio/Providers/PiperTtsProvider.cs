using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using KnowledgeEngine.Application.DTOs;
using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Domain.Enums;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace KnowledgeEngine.Infrastructure.Audio.Providers;

/// <summary>
/// Piper TTS provider using the Wyoming Piper HTTP protocol.
/// Piper is a fast, local neural TTS engine that runs on-device or on a local
/// LAN node. It communicates via HTTP (default <c>localhost:10200</c>) and
/// supports streaming audio output through chunked transfer encoding.
/// No credentials are required — all processing is local.
/// </summary>
public class PiperTtsProvider : ITtsProvider
{
    private const string ProviderIdValue = "piper";
    private const string ModelIdValue = "default";
    private const string HttpClientName = "Piper";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<PiperTtsProvider> _logger;
    private readonly string _baseUrl;
    private readonly bool _enabled;

    /// <summary>
    /// Creates a new <see cref="PiperTtsProvider"/>.
    /// </summary>
    /// <param name="httpClientFactory">HTTP client factory for creating named clients.</param>
    /// <param name="configuration">Application configuration for reading the Piper base URL.</param>
    /// <param name="logger">Logger for diagnostics.</param>
    public PiperTtsProvider(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<PiperTtsProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _baseUrl = (configuration["Audio:PiperBaseUrl"] ?? "http://localhost:10200").TrimEnd('/');
        _enabled = configuration.GetValue("Audio:PiperEnabled", false);
    }

    /// <inheritdoc/>
    public Task<TtsProviderDescriptor> GetDescriptorAsync(CancellationToken ct)
    {
        var descriptor = new TtsProviderDescriptor
        {
            ProviderId = ProviderIdValue,
            ModelId = ModelIdValue,
            ExecutionModes = [ExecutionMode.LOCAL_DEVICE],
            CredentialModes = [CredentialMode.NO_CREDENTIAL],
            SupportedLanguages = ["zh", "en"],
            SupportsStreaming = true,
            SupportsBatch = true,
            SupportsVoiceCloning = false,
            SupportsSpeedControl = true,
            SupportsPitchControl = false,
            OutputFormats = ["wav"],
            SupportedSampleRates = [22050, 16000],
            SendsAudioOffDevice = false,
            StoresProviderData = ProviderDataRetention.NO,
            PricingUnit = null
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

        if (string.IsNullOrWhiteSpace(_baseUrl))
        {
            return Task.FromResult(ValidationResult.Fail("Piper base URL is not configured."));
        }

        return Task.FromResult(ValidationResult.Ok());
    }

    /// <inheritdoc/>
    public async Task<TtsResult> SynthesizeAsync(TtsRequest request, CancellationToken ct)
    {
        var httpClient = _httpClientFactory.CreateClient(HttpClientName);

        var payload = new PiperSynthesizeRequest
        {
            Text = request.Text,
            SpeakerId = 0,
            LengthScale = request.Speed > 0 ? (double)(1.0m / request.Speed) : 1.0
        };

        var json = JsonSerializer.Serialize(payload, JsonOptions);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        _logger.LogInformation(
            "Calling Piper TTS API for {TextLength} chars", request.Text.Length);

        var sw = Stopwatch.StartNew();
        HttpResponseMessage response;
        try
        {
            response = await httpClient.PostAsync($"{_baseUrl}/synthesize", content, ct);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Piper TTS API network error: {Message}", ex.Message);
            throw new InvalidOperationException($"Piper TTS API network error: {ex.Message}", ex);
        }
        sw.Stop();

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError("Piper TTS API returned {StatusCode}: {ErrorBody}",
                response.StatusCode, errorBody);
            throw new InvalidOperationException(
                $"Piper TTS API returned status {(int)response.StatusCode}: {errorBody}");
        }

        var audioBytes = await response.Content.ReadAsByteArrayAsync(ct);

        // Persist to temp file for consistent TtsResult contract.
        var outputPath = Path.Combine(Path.GetTempPath(), $"memorix-tts-piper-{Guid.NewGuid():N}.wav");
        await File.WriteAllBytesAsync(outputPath, audioBytes, ct);

        _logger.LogInformation(
            "Piper TTS synthesis completed: {SizeBytes} bytes in {ElapsedMs}ms",
            audioBytes.Length, sw.ElapsedMilliseconds);

        return new TtsResult
        {
            ProviderId = ProviderIdValue,
            ModelId = ModelIdValue,
            OutputFilePath = outputPath,
            OutputFormat = "wav",
            FileSizeBytes = audioBytes.Length,
            VoiceId = request.VoiceId,
            Metadata = new Dictionary<string, object>
            {
                ["engine"] = "piper",
                ["latency_ms"] = sw.ElapsedMilliseconds
            }
        };
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<AudioChunk> SynthesizeStream(
        TtsStreamRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var httpClient = _httpClientFactory.CreateClient(HttpClientName);

        var payload = new PiperSynthesizeRequest
        {
            Text = request.Text,
            SpeakerId = 0,
            LengthScale = request.Speed > 0 ? (double)(1.0m / request.Speed) : 1.0
        };

        var json = JsonSerializer.Serialize(payload, JsonOptions);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        _logger.LogInformation(
            "Piper TTS streaming synthesis for {TextLength} chars, session={SessionId}",
            request.Text.Length, request.SessionId);

        HttpResponseMessage response;
        try
        {
            response = await httpClient.PostAsync(
                $"{_baseUrl}/synthesize-stream", content, ct);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Piper TTS streaming API network error: {Message}", ex.Message);
            yield break;
        }

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError("Piper TTS streaming API returned {StatusCode}: {ErrorBody}",
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
                Format = "wav",
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
            Format = "wav",
            SampleRate = request.SampleRate
        };
    }

    /// <inheritdoc/>
    public Task<List<VoiceProfile>> ListVoicesAsync(CancellationToken ct)
    {
        // Piper uses on-device voice models; expose a default voice.
        var voices = new List<VoiceProfile>
        {
            new()
            {
                VoiceId = "default",
                Name = "Piper Default Voice",
                Language = "en",
                Gender = "unknown",
                IsClonable = false
            }
        };

        return Task.FromResult(voices);
    }

    /// <inheritdoc/>
    public Task<CostEstimate>? EstimateCostAsync(TtsRequest request, CancellationToken ct)
    {
        // Local execution — no monetary cost.
        return null;
    }

    /// <inheritdoc/>
    public async Task<ProviderHealth> HealthCheckAsync(CancellationToken ct)
    {
        var health = new ProviderHealth
        {
            ProviderId = ProviderIdValue,
            CheckedAt = DateTime.UtcNow
        };

        if (!_enabled)
        {
            health.IsHealthy = false;
            health.StatusMessage = "Piper TTS is disabled in configuration.";
            return health;
        }

        var httpClient = _httpClientFactory.CreateClient(HttpClientName);
        var sw = Stopwatch.StartNew();

        try
        {
            var response = await httpClient.GetAsync($"{_baseUrl}/health", ct);
            sw.Stop();

            health.IsHealthy = response.IsSuccessStatusCode;
            health.LatencyMs = sw.ElapsedMilliseconds;
            health.StatusMessage = health.IsHealthy
                ? "Piper TTS is healthy"
                : $"Health check returned {response.StatusCode}";
        }
        catch (Exception ex)
        {
            sw.Stop();
            health.IsHealthy = false;
            health.LatencyMs = sw.ElapsedMilliseconds;
            health.StatusMessage = $"Piper TTS health check failed: {ex.Message}";
        }

        return health;
    }

    // ── Piper API request DTO ──

    private sealed class PiperSynthesizeRequest
    {
        [JsonPropertyName("text")]
        public string Text { get; set; } = string.Empty;

        [JsonPropertyName("speaker_id")]
        public int SpeakerId { get; set; }

        [JsonPropertyName("length_scale")]
        public double LengthScale { get; set; } = 1.0;
    }
}
