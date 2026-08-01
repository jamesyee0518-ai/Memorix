using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using KnowledgeEngine.Application.DTOs;
using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Domain.Enums;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace KnowledgeEngine.Infrastructure.Audio.Providers;

/// <summary>
/// FunASR VAD (Voice Activity Detection) provider using the FSMN-VAD model.
/// Detects speech segments in audio and returns them as timestamped segments
/// without transcribed text. These segments serve as the universal time baseline
/// for all downstream ASR capabilities.
/// </summary>
public class FunAsrVadProvider : IAsrProvider
{
    private const string ProviderIdValue = "funasr_vad";
    private const string ModelIdValue = "fsmn-vad";
    private const string HttpClientName = "FunAsr";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<FunAsrVadProvider> _logger;
    private readonly string _baseUrl;

    /// <summary>
    /// Creates a new <see cref="FunAsrVadProvider"/>.
    /// </summary>
    /// <param name="httpClientFactory">HTTP client factory for creating named clients.</param>
    /// <param name="configuration">Application configuration for reading the FunASR base URL.</param>
    /// <param name="logger">Logger for diagnostics.</param>
    public FunAsrVadProvider(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<FunAsrVadProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _baseUrl = (configuration["Audio:FunAsrBaseUrl"] ?? "http://localhost:8000").TrimEnd('/');
    }

    /// <inheritdoc/>
    public Task<AsrProviderDescriptor> GetDescriptorAsync(CancellationToken ct)
    {
        var descriptor = new AsrProviderDescriptor
        {
            ProviderId = ProviderIdValue,
            ModelId = ModelIdValue,
            ExecutionModes = [ExecutionMode.LOCAL_DEVICE],
            CredentialModes = [CredentialMode.NO_CREDENTIAL],
            SupportedLanguages = [], // VAD is language-independent
            SupportsStreaming = false,
            SupportsBatch = true,
            SupportsVad = true,
            SupportsPunctuation = false,
            SupportsDiarization = false,
            SupportsHotwords = false,
            SupportsWordTimestamp = false,
            SupportsSegmentTimestamp = false,
            SendsAudioOffDevice = false,
            StoresProviderData = ProviderDataRetention.NO,
            AcceptedMimeTypes =
            [
                "audio/wav", "audio/mp3", "audio/m4a",
                "audio/flac", "audio/ogg", "audio/webm"
            ]
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

        if (string.IsNullOrWhiteSpace(_baseUrl))
        {
            return Task.FromResult(ValidationResult.Fail("FunASR base URL is not configured."));
        }

        return Task.FromResult(ValidationResult.Ok());
    }

    /// <inheritdoc/>
    public async Task<AsrTranscriptionResult> TranscribeAsync(AsrTranscriptionRequest request, CancellationToken ct)
    {
        var httpClient = _httpClientFactory.CreateClient(HttpClientName);

        using var multipart = new MultipartFormDataContent();
        await using var fileStream = File.OpenRead(request.AudioFilePath);
        var fileContent = new StreamContent(fileStream);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(
            string.IsNullOrWhiteSpace(request.MimeType) ? "audio/wav" : request.MimeType);
        multipart.Add(fileContent, "audio", Path.GetFileName(request.AudioFilePath));

        _logger.LogInformation(
            "Calling FunASR VAD API for file={FilePath}", request.AudioFilePath);

        var stopwatch = Stopwatch.StartNew();
        HttpResponseMessage response;
        try
        {
            response = await httpClient.PostAsync($"{_baseUrl}/api/vad", multipart, ct);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "FunASR VAD API network error: {Message}", ex.Message);
            throw new InvalidOperationException($"FunASR VAD API network error: {ex.Message}", ex);
        }
        stopwatch.Stop();

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError("FunASR VAD API returned {StatusCode}: {ErrorBody}", response.StatusCode, errorBody);
            throw new InvalidOperationException($"FunASR VAD API returned status {(int)response.StatusCode}: {errorBody}");
        }

        var jsonContent = await response.Content.ReadAsStringAsync(ct);
        var vadResponse = JsonSerializer.Deserialize<FunAsrVadResponse>(jsonContent, JsonOptions);

        if (vadResponse == null)
        {
            throw new InvalidOperationException("FunASR VAD API returned null response.");
        }

        // Map VAD segments to AsrSegmentDto — text is empty since VAD only
        // produces speech/non-speech boundaries, not transcriptions.
        var segments = new List<AsrSegmentDto>();
        var prefix = request.SegmentUuidPrefix;
        var vadSegments = vadResponse.Segments ?? vadResponse.Timestamp;

        if (vadSegments is { Count: > 0 })
        {
            for (var i = 0; i < vadSegments.Count; i++)
            {
                var seg = vadSegments[i];
                segments.Add(new AsrSegmentDto
                {
                    SegmentUuid = GenerateSegmentUuid(prefix),
                    StartMs = seg.Start,
                    EndMs = seg.End,
                    Text = string.Empty,
                    Confidence = seg.Confidence,
                    SegmentIndex = i
                });
            }
        }

        _logger.LogInformation(
            "FunASR VAD completed: {SegmentCount} speech segments in {ElapsedMs}ms",
            segments.Count, stopwatch.ElapsedMilliseconds);

        return new AsrTranscriptionResult
        {
            ProviderId = ProviderIdValue,
            ModelId = ModelIdValue,
            Language = null,
            DurationMs = request.DurationMs,
            Segments = segments,
            FullText = string.Empty,
            Metadata = new Dictionary<string, object>
            {
                ["engine"] = "funasr_vad",
                ["model"] = ModelIdValue,
                ["segment_count"] = segments.Count,
                ["latency_ms"] = stopwatch.ElapsedMilliseconds
            }
        };
    }

    /// <inheritdoc/>
    public IAsyncEnumerable<AsrPartialResult>? TranscribeStream(AsrStreamingRequest request, CancellationToken ct)
    {
        // VAD does not support streaming.
        return null;
    }

    /// <inheritdoc/>
    public Task<CostEstimate>? EstimateCostAsync(AsrTranscriptionRequest request, CancellationToken ct)
    {
        // Local execution — no monetary cost.
        return null;
    }

    /// <inheritdoc/>
    public Task CancelAsync(string providerTaskId, CancellationToken ct)
    {
        _logger.LogDebug("CancelAsync called for providerTaskId={ProviderTaskId} (no-op for HTTP)", providerTaskId);
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

        var httpClient = _httpClientFactory.CreateClient(HttpClientName);
        var sw = Stopwatch.StartNew();

        try
        {
            var response = await httpClient.GetAsync($"{_baseUrl}/health", ct);
            sw.Stop();

            health.IsHealthy = response.IsSuccessStatusCode;
            health.LatencyMs = sw.ElapsedMilliseconds;
            health.StatusMessage = health.IsHealthy
                ? "FunASR VAD runtime is healthy"
                : $"Health check returned {response.StatusCode}";
        }
        catch (Exception ex)
        {
            sw.Stop();
            health.IsHealthy = false;
            health.LatencyMs = sw.ElapsedMilliseconds;
            health.StatusMessage = $"FunASR VAD health check failed: {ex.Message}";
        }

        return health;
    }

    // ── Private helpers ──

    /// <summary>
    /// Generates a stable segment UUID with an optional prefix.
    /// </summary>
    private static string GenerateSegmentUuid(string? prefix)
    {
        var guid = Guid.NewGuid().ToString("N");
        return string.IsNullOrWhiteSpace(prefix) ? guid : $"{prefix}_{guid}";
    }

    // ── FunASR VAD API response DTOs ──

    private sealed class FunAsrVadResponse
    {
        [JsonPropertyName("segments")]
        public List<FunAsrVadSegment>? Segments { get; set; }

        [JsonPropertyName("timestamp")]
        public List<FunAsrVadSegment>? Timestamp { get; set; }
    }

    private sealed class FunAsrVadSegment
    {
        [JsonPropertyName("start")]
        public long Start { get; set; }

        [JsonPropertyName("end")]
        public long End { get; set; }

        [JsonPropertyName("confidence")]
        public decimal Confidence { get; set; }
    }
}
