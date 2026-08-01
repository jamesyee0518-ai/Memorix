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
/// Fish-Speech TTS provider using its HTTP API (default <c>localhost:8080</c>).
/// Fish-Speech is a high-quality local neural TTS engine that supports voice
/// cloning, streaming synthesis, and speed/pitch control. It runs on-device or
/// on a local LAN node — no credentials are required and audio never leaves the
/// local network.
/// </summary>
public class FishSpeechTtsProvider : ITtsProvider
{
    private const string ProviderIdValue = "fish_speech";
    private const string ModelIdValue = "fish-1.5";
    private const string HttpClientName = "FishSpeech";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<FishSpeechTtsProvider> _logger;
    private readonly string _baseUrl;
    private readonly bool _enabled;
    private readonly string _defaultVoice;

    /// <summary>
    /// Creates a new <see cref="FishSpeechTtsProvider"/>.
    /// </summary>
    /// <param name="httpClientFactory">HTTP client factory for creating named clients.</param>
    /// <param name="configuration">Application configuration for reading Fish-Speech settings.</param>
    /// <param name="logger">Logger for diagnostics.</param>
    public FishSpeechTtsProvider(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<FishSpeechTtsProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _baseUrl = (configuration["Audio:FishSpeechBaseUrl"] ?? "http://localhost:8080").TrimEnd('/');
        _enabled = configuration.GetValue("Audio:FishSpeechEnabled", false);
        _defaultVoice = configuration["Audio:TtsDefaultVoice"] ?? "default";
    }

    /// <inheritdoc/>
    public Task<TtsProviderDescriptor> GetDescriptorAsync(CancellationToken ct)
    {
        var descriptor = new TtsProviderDescriptor
        {
            ProviderId = ProviderIdValue,
            ModelId = ModelIdValue,
            ExecutionModes = [ExecutionMode.LOCAL_DEVICE, ExecutionMode.LOCAL_LAN_NODE],
            CredentialModes = [CredentialMode.NO_CREDENTIAL],
            SupportedLanguages = ["zh", "en"],
            SupportsStreaming = true,
            SupportsBatch = true,
            SupportsVoiceCloning = true,
            SupportsSpeedControl = true,
            SupportsPitchControl = true,
            OutputFormats = ["wav", "mp3"],
            SupportedSampleRates = [22050, 44100],
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
            return Task.FromResult(ValidationResult.Fail("Fish-Speech base URL is not configured."));
        }

        return Task.FromResult(ValidationResult.Ok());
    }

    /// <inheritdoc/>
    public async Task<TtsResult> SynthesizeAsync(TtsRequest request, CancellationToken ct)
    {
        var httpClient = _httpClientFactory.CreateClient(HttpClientName);

        var payload = new FishSpeechRequest
        {
            Text = request.Text,
            VoiceId = request.VoiceId ?? _defaultVoice,
            Speed = request.Speed > 0 ? (double)request.Speed : 1.0,
            Pitch = request.Pitch > 0 ? (double)request.Pitch : 1.0,
            Format = request.OutputFormat ?? "wav",
            SampleRate = request.SampleRate
        };

        var json = JsonSerializer.Serialize(payload, JsonOptions);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        _logger.LogInformation(
            "Calling Fish-Speech TTS API for {TextLength} chars, voice={Voice}",
            request.Text.Length, payload.VoiceId);

        var sw = Stopwatch.StartNew();
        HttpResponseMessage response;
        try
        {
            response = await httpClient.PostAsync($"{_baseUrl}/v1/tts", content, ct);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Fish-Speech TTS API network error: {Message}", ex.Message);
            throw new InvalidOperationException($"Fish-Speech TTS API network error: {ex.Message}", ex);
        }
        sw.Stop();

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError("Fish-Speech TTS API returned {StatusCode}: {ErrorBody}",
                response.StatusCode, errorBody);
            throw new InvalidOperationException(
                $"Fish-Speech TTS API returned status {(int)response.StatusCode}: {errorBody}");
        }

        var audioBytes = await response.Content.ReadAsByteArrayAsync(ct);

        var outputPath = Path.Combine(Path.GetTempPath(),
            $"memorix-tts-fish-{Guid.NewGuid():N}.{payload.Format}");
        await File.WriteAllBytesAsync(outputPath, audioBytes, ct);

        _logger.LogInformation(
            "Fish-Speech TTS synthesis completed: {SizeBytes} bytes in {ElapsedMs}ms",
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
                ["engine"] = "fish_speech",
                ["model"] = ModelIdValue,
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

        var payload = new FishSpeechStreamRequest
        {
            Text = request.Text,
            VoiceId = request.VoiceId ?? _defaultVoice,
            Speed = request.Speed > 0 ? (double)request.Speed : 1.0,
            SampleRate = request.SampleRate,
            Stream = true
        };

        var json = JsonSerializer.Serialize(payload, JsonOptions);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        _logger.LogInformation(
            "Fish-Speech TTS streaming synthesis for {TextLength} chars, session={SessionId}",
            request.Text.Length, request.SessionId);

        HttpResponseMessage response;
        try
        {
            response = await httpClient.PostAsync(
                $"{_baseUrl}/v1/tts/stream", content, ct);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Fish-Speech TTS streaming API network error: {Message}", ex.Message);
            yield break;
        }

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError("Fish-Speech TTS streaming API returned {StatusCode}: {ErrorBody}",
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
                VoiceId = "default",
                Name = "Fish-Speech Default Voice",
                Language = "zh",
                Gender = "unknown",
                IsClonable = true
            },
            new()
            {
                VoiceId = "alice",
                Name = "Alice (English)",
                Language = "en",
                Gender = "female",
                IsClonable = true
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
            health.StatusMessage = "Fish-Speech TTS is disabled in configuration.";
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
                ? "Fish-Speech TTS is healthy"
                : $"Health check returned {response.StatusCode}";
        }
        catch (Exception ex)
        {
            sw.Stop();
            health.IsHealthy = false;
            health.LatencyMs = sw.ElapsedMilliseconds;
            health.StatusMessage = $"Fish-Speech TTS health check failed: {ex.Message}";
        }

        return health;
    }

    // ── Fish-Speech API request DTOs ──

    private sealed class FishSpeechRequest
    {
        [JsonPropertyName("text")]
        public string Text { get; set; } = string.Empty;

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

    private sealed class FishSpeechStreamRequest
    {
        [JsonPropertyName("text")]
        public string Text { get; set; } = string.Empty;

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
