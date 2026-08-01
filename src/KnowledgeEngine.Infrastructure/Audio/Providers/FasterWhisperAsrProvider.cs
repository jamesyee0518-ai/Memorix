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
/// Local faster-whisper ASR provider using its HTTP API (default
/// <c>http://localhost:8000</c>).
/// faster-whisper is a CTranslate2-backed reimplementation of OpenAI Whisper that
/// runs on-device or on a local LAN node. It exposes an OpenAI-compatible
/// <c>/v1/audio/transcriptions</c> endpoint that accepts multipart audio uploads
/// and returns segment-level (and optionally word-level) timestamps as JSON.
/// No credentials are required and audio never leaves the local network.
/// </summary>
public class FasterWhisperAsrProvider : IAsrProvider
{
    private const string ProviderIdValue = "faster_whisper";
    private const string ModelIdValue = "faster-whisper-large-v3";
    private const string HttpClientName = "FasterWhisper";

    /// <summary>
    /// Relative path appended to the configured endpoint for transcription.
    /// Matches the OpenAI-compatible audio transcription route.
    /// </summary>
    private const string TranscriptionPath = "/v1/audio/transcriptions";

    private static readonly TimeSpan RequestTimeout = TimeSpan.FromMinutes(10);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly AudioSettings _settings;
    private readonly ILogger<FasterWhisperAsrProvider> _logger;

    /// <summary>
    /// Creates a new <see cref="FasterWhisperAsrProvider"/>.
    /// </summary>
    /// <param name="httpClientFactory">HTTP client factory for creating the named <c>FasterWhisper</c> client.</param>
    /// <param name="settings">Audio settings containing the configurable faster-whisper endpoint.</param>
    /// <param name="logger">Logger for diagnostics.</param>
    public FasterWhisperAsrProvider(
        IHttpClientFactory httpClientFactory,
        IOptions<AudioSettings> settings,
        ILogger<FasterWhisperAsrProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _settings = settings.Value;
        _logger = logger;
    }

    /// <summary>
    /// Returns the resolved faster-whisper HTTP endpoint (trailing slash removed).
    /// </summary>
    private string BaseUrl => _settings.FasterWhisperEndpoint.TrimEnd('/');

    /// <inheritdoc/>
    public Task<AsrProviderDescriptor> GetDescriptorAsync(CancellationToken ct)
    {
        var descriptor = new AsrProviderDescriptor
        {
            ProviderId = ProviderIdValue,
            ModelId = ModelIdValue,
            ExecutionModes = [ExecutionMode.LOCAL_DEVICE, ExecutionMode.LOCAL_LAN_NODE],
            CredentialModes = [CredentialMode.NO_CREDENTIAL],
            SupportedLanguages = [], // faster-whisper auto-detects; empty = all languages
            SupportsStreaming = false,
            SupportsBatch = true,
            SupportsVad = true,
            SupportsPunctuation = false,
            SupportsDiarization = false,
            SupportsHotwords = false,
            SupportsWordTimestamp = true,
            SupportsSegmentTimestamp = true,
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

        if (string.IsNullOrWhiteSpace(_settings.FasterWhisperEndpoint))
        {
            return Task.FromResult(ValidationResult.Fail(
                "Faster-whisper endpoint is not configured (Audio:FasterWhisperEndpoint)."));
        }

        return Task.FromResult(ValidationResult.Ok());
    }

    /// <inheritdoc/>
    public async Task<AsrTranscriptionResult> TranscribeAsync(
        AsrTranscriptionRequest request,
        CancellationToken ct)
    {
        // ── Build the multipart/form-data request ──

        var httpClient = _httpClientFactory.CreateClient(HttpClientName);
        httpClient.Timeout = RequestTimeout;

        using var multipart = new MultipartFormDataContent();
        await using var fileStream = File.OpenRead(request.AudioFilePath);

        var fileContent = new StreamContent(fileStream);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(
            !string.IsNullOrWhiteSpace(request.MimeType) ? request.MimeType : "audio/wav");
        multipart.Add(fileContent, "file", Path.GetFileName(request.AudioFilePath));

        // Required model identifier (OpenAI-compatible field).
        multipart.Add(new StringContent(ModelIdValue), "model");

        // Request verbose_json so segments and word timestamps are returned.
        multipart.Add(new StringContent("verbose_json"), "response_format");

        // Optional language hint.
        if (!string.IsNullOrWhiteSpace(request.Language))
        {
            multipart.Add(new StringContent(request.Language), "language");
        }

        // Word-level timestamps.
        if (request.EnableWordTimestamp)
        {
            multipart.Add(new StringContent("word"), "timestamp_granularities[]");
        }

        // Segment-level timestamps (default, but explicit for clarity).
        multipart.Add(new StringContent("segment"), "timestamp_granularities[]");

        // ── Send the request ──

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}{TranscriptionPath}")
        {
            Content = multipart
        };

        _logger.LogInformation(
            "Calling faster-whisper ASR API for file={FilePath}, model={Model}",
            request.AudioFilePath, ModelIdValue);

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
                "Faster-whisper ASR API network error after {ElapsedMs}ms: {Message}",
                stopwatch.ElapsedMilliseconds, ex.Message);
            throw new InvalidOperationException(
                $"Faster-whisper ASR API network error: {ex.Message}", ex);
        }
        stopwatch.Stop();

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError(
                "Faster-whisper ASR API returned {StatusCode} after {ElapsedMs}ms: {ErrorBody}",
                response.StatusCode, stopwatch.ElapsedMilliseconds, errorBody);
            throw new InvalidOperationException(
                $"Faster-whisper ASR API returned status {(int)response.StatusCode}: {errorBody}");
        }

        // ── Parse the JSON response ──

        var jsonContent = await response.Content.ReadAsStringAsync(ct);
        var asrResponse = JsonSerializer.Deserialize<FasterWhisperResponse>(jsonContent, JsonOptions);

        if (asrResponse == null)
        {
            throw new InvalidOperationException("Faster-whisper ASR API returned an empty response.");
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

                var segment = new AsrSegmentDto
                {
                    SegmentUuid = GenerateSegmentUuid(prefix),
                    StartMs = (long)(seg.Start * 1000),
                    EndMs = (long)(seg.End * 1000),
                    Text = text,
                    Confidence = 0,
                    SegmentIndex = i
                };

                // Map word-level timestamps if available.
                if (request.EnableWordTimestamp && seg.Words is { Count: > 0 })
                {
                    segment.Words = seg.Words.Select(w => new AsrWordDto
                    {
                        StartMs = (long)(w.Start * 1000),
                        EndMs = (long)(w.End * 1000),
                        Text = w.Word?.Trim() ?? string.Empty,
                        Confidence = w.Probability
                    }).ToList();
                }

                segments.Add(segment);
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

        // Resolve the final full text: prefer the top-level "text" field,
        // fall back to concatenating segment texts.
        var fullText = !string.IsNullOrWhiteSpace(asrResponse.Text)
            ? asrResponse.Text.Trim()
            : fullTextBuilder.ToString().Trim();

        _logger.LogInformation(
            "Faster-whisper transcription completed: {SegmentCount} segments in {ElapsedMs}ms",
            segments.Count, stopwatch.ElapsedMilliseconds);

        return new AsrTranscriptionResult
        {
            ProviderId = ProviderIdValue,
            ModelId = ModelIdValue,
            Language = asrResponse.Language ?? request.Language,
            DurationMs = request.DurationMs,
            Segments = segments,
            FullText = fullText,
            Metadata = new Dictionary<string, object>
            {
                ["engine"] = "faster_whisper",
                ["model"] = ModelIdValue,
                ["latency_ms"] = stopwatch.ElapsedMilliseconds
            }
        };
    }

    /// <inheritdoc/>
    public IAsyncEnumerable<AsrPartialResult>? TranscribeStream(AsrStreamingRequest request, CancellationToken ct)
    {
        // faster-whisper HTTP API does not support streaming transcription.
        return null;
    }

    /// <inheritdoc/>
    public Task<CostEstimate>? EstimateCostAsync(AsrTranscriptionRequest request, CancellationToken ct)
    {
        // Local/LAN execution — no monetary cost.
        return null;
    }

    /// <inheritdoc/>
    public Task CancelAsync(string providerTaskId, CancellationToken ct)
    {
        // The faster-whisper batch HTTP API does not expose a task-cancellation endpoint.
        // Cancellation is handled via the CancellationToken passed to SendAsync.
        _logger.LogDebug(
            "CancelAsync called for providerTaskId={ProviderTaskId} (no-op for faster-whisper batch API)",
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

        if (!_settings.FasterWhisperEnabled)
        {
            health.IsHealthy = false;
            health.StatusMessage = "Faster-whisper ASR is disabled in configuration.";
            return health;
        }

        var httpClient = _httpClientFactory.CreateClient(HttpClientName);
        httpClient.Timeout = TimeSpan.FromSeconds(10);

        var sw = Stopwatch.StartNew();
        try
        {
            // Lightweight connectivity check: GET the root or /health endpoint.
            // A 200 (or even 404) indicates the server is reachable.
            using var response = await httpClient.GetAsync($"{BaseUrl}/health", ct);
            sw.Stop();

            health.IsHealthy = response.IsSuccessStatusCode
                || response.StatusCode == System.Net.HttpStatusCode.NotFound;
            health.LatencyMs = sw.ElapsedMilliseconds;
            health.StatusMessage = health.IsHealthy
                ? "Faster-whisper endpoint is reachable"
                : $"Health check returned {response.StatusCode}";
        }
        catch (Exception ex)
        {
            sw.Stop();
            health.IsHealthy = false;
            health.LatencyMs = sw.ElapsedMilliseconds;
            health.StatusMessage = $"Faster-whisper health check failed: {ex.Message}";
        }

        return health;
    }

    // ── Private helpers ──

    /// <summary>
    /// Generates a stable segment UUID with an optional prefix.
    /// Format: <c>{prefix}_{guid}</c> or just <c>{guid}</c> when no prefix is provided.
    /// </summary>
    private static string GenerateSegmentUuid(string? prefix)
    {
        var guid = Guid.NewGuid().ToString("N");
        return string.IsNullOrWhiteSpace(prefix) ? guid : $"{prefix}_{guid}";
    }

    // ── Faster-whisper API response DTOs ──

    /// <summary>
    /// Represents the JSON response from the faster-whisper transcription endpoint.
    /// Matches the OpenAI-compatible verbose_json format.
    /// </summary>
    private sealed class FasterWhisperResponse
    {
        [JsonPropertyName("text")]
        public string? Text { get; set; }

        [JsonPropertyName("language")]
        public string? Language { get; set; }

        [JsonPropertyName("segments")]
        public List<FasterWhisperSegment>? Segments { get; set; }
    }

    /// <summary>
    /// Represents a single transcription segment from the faster-whisper API.
    /// Timestamps are in seconds.
    /// </summary>
    private sealed class FasterWhisperSegment
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("start")]
        public double Start { get; set; }

        [JsonPropertyName("end")]
        public double End { get; set; }

        [JsonPropertyName("text")]
        public string? Text { get; set; }

        [JsonPropertyName("words")]
        public List<FasterWhisperWord>? Words { get; set; }
    }

    /// <summary>
    /// Represents a single word with timestamp from the faster-whisper API.
    /// </summary>
    private sealed class FasterWhisperWord
    {
        [JsonPropertyName("word")]
        public string? Word { get; set; }

        [JsonPropertyName("start")]
        public double Start { get; set; }

        [JsonPropertyName("end")]
        public double End { get; set; }

        [JsonPropertyName("probability")]
        public decimal Probability { get; set; }
    }
}
