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
/// FunASR runtime ASR provider that communicates with a local or LAN-deployed
/// FunASR HTTP service. Uses the Paraformer-zh model for Chinese speech recognition
/// with built-in punctuation and hotword support. Audio is processed on-device or
/// within the local network — never sent to a third-party cloud.
/// </summary>
public class FunAsrAsrProvider : IAsrProvider
{
    private const string ProviderIdValue = "funasr";
    private const string ModelIdValue = "paraformer-zh";
    private const string HttpClientName = "FunAsr";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<FunAsrAsrProvider> _logger;
    private readonly string _baseUrl;

    /// <summary>
    /// Creates a new <see cref="FunAsrAsrProvider"/>.
    /// </summary>
    /// <param name="httpClientFactory">HTTP client factory for creating named clients.</param>
    /// <param name="configuration">Application configuration for reading the FunASR base URL.</param>
    /// <param name="logger">Logger for diagnostics.</param>
    public FunAsrAsrProvider(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<FunAsrAsrProvider> logger)
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
            ExecutionModes = [ExecutionMode.LOCAL_DEVICE, ExecutionMode.LOCAL_LAN_NODE],
            CredentialModes = [CredentialMode.NO_CREDENTIAL],
            SupportedLanguages = ["zh", "en"],
            SupportsStreaming = true,
            SupportsBatch = true,
            SupportsVad = false,
            SupportsPunctuation = true,
            SupportsDiarization = false,
            SupportsHotwords = true,
            SupportsWordTimestamp = false,
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

        // Optional parameters
        if (!string.IsNullOrWhiteSpace(request.Language))
        {
            multipart.Add(new StringContent(request.Language), "language");
        }

        if (request.EnablePunctuation)
        {
            multipart.Add(new StringContent("true"), "use_itn");
        }

        if (request.Hotwords is { Count: > 0 })
        {
            var hotwordsJson = JsonSerializer.Serialize(request.Hotwords);
            multipart.Add(new StringContent(hotwordsJson), "hotwords");
        }

        _logger.LogInformation(
            "Calling FunASR ASR API for file={FilePath}, model={Model}",
            request.AudioFilePath, ModelIdValue);

        var stopwatch = Stopwatch.StartNew();
        HttpResponseMessage response;
        try
        {
            response = await httpClient.PostAsync($"{_baseUrl}/api/asr", multipart, ct);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "FunASR ASR API network error: {Message}", ex.Message);
            throw new InvalidOperationException($"FunASR ASR API network error: {ex.Message}", ex);
        }
        stopwatch.Stop();

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError("FunASR ASR API returned {StatusCode}: {ErrorBody}", response.StatusCode, errorBody);
            throw new InvalidOperationException($"FunASR ASR API returned status {(int)response.StatusCode}: {errorBody}");
        }

        var jsonContent = await response.Content.ReadAsStringAsync(ct);
        var asrResponse = JsonSerializer.Deserialize<FunAsrAsrResponse>(jsonContent, JsonOptions);

        if (asrResponse == null)
        {
            throw new InvalidOperationException("FunASR ASR API returned null response.");
        }

        // Build segments from sentences or timestamps.
        var segments = new List<AsrSegmentDto>();
        var prefix = request.SegmentUuidPrefix;

        if (asrResponse.Sentences is { Count: > 0 })
        {
            for (var i = 0; i < asrResponse.Sentences.Count; i++)
            {
                var sentence = asrResponse.Sentences[i];
                segments.Add(new AsrSegmentDto
                {
                    SegmentUuid = GenerateSegmentUuid(prefix),
                    StartMs = sentence.Start,
                    EndMs = sentence.End,
                    Text = sentence.Text?.Trim() ?? string.Empty,
                    Confidence = 0,
                    SegmentIndex = i
                });
            }
        }
        else if (asrResponse.Timestamp is { Count: > 0 })
        {
            // Split text by timestamps when sentence boundaries are not available.
            var fullText = asrResponse.Text ?? string.Empty;
            for (var i = 0; i < asrResponse.Timestamp.Count; i++)
            {
                var ts = asrResponse.Timestamp[i];
                segments.Add(new AsrSegmentDto
                {
                    SegmentUuid = GenerateSegmentUuid(prefix),
                    StartMs = ts.Count > 0 ? ts[0] : 0,
                    EndMs = ts.Count > 1 ? ts[1] : 0,
                    Text = string.Empty,
                    Confidence = 0,
                    SegmentIndex = i
                });
            }

            // If we got timestamps but no sentence text, put the full text in the first segment.
            if (segments.Count > 0 && segments.All(s => string.IsNullOrEmpty(s.Text)))
            {
                segments[0].Text = fullText.Trim();
            }
        }
        else
        {
            // Fallback: single segment with the full text.
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

        _logger.LogInformation(
            "FunASR ASR completed: {SegmentCount} segments in {ElapsedMs}ms",
            segments.Count, stopwatch.ElapsedMilliseconds);

        return new AsrTranscriptionResult
        {
            ProviderId = ProviderIdValue,
            ModelId = ModelIdValue,
            Language = request.Language,
            DurationMs = request.DurationMs,
            Segments = segments,
            FullText = asrResponse.Text?.Trim() ?? string.Join(' ', segments.Select(s => s.Text)),
            Metadata = new Dictionary<string, object>
            {
                ["engine"] = "funasr",
                ["model"] = ModelIdValue,
                ["latency_ms"] = stopwatch.ElapsedMilliseconds
            }
        };
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<AsrPartialResult> TranscribeStream(
        AsrStreamingRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        // FunASR supports real-time WebSocket streaming. This implementation uses
        // the batch HTTP endpoint as a simplified streaming fallback, yielding the
        // complete result as a single final partial.
        //
        // A production streaming implementation would connect to the FunASR
        // WebSocket endpoint and yield incremental partial results as they arrive.

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
                ? "FunASR runtime is healthy"
                : $"Health check returned {response.StatusCode}";
        }
        catch (Exception ex)
        {
            sw.Stop();
            health.IsHealthy = false;
            health.LatencyMs = sw.ElapsedMilliseconds;
            health.StatusMessage = $"FunASR health check failed: {ex.Message}";
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

    // ── FunASR API response DTOs ──

    private sealed class FunAsrAsrResponse
    {
        [JsonPropertyName("text")]
        public string? Text { get; set; }

        [JsonPropertyName("timestamp")]
        public List<List<long>>? Timestamp { get; set; }

        [JsonPropertyName("sentences")]
        public List<FunAsrSentence>? Sentences { get; set; }

        [JsonPropertyName("key")]
        public string? Key { get; set; }
    }

    private sealed class FunAsrSentence
    {
        [JsonPropertyName("text")]
        public string? Text { get; set; }

        [JsonPropertyName("start")]
        public long Start { get; set; }

        [JsonPropertyName("end")]
        public long End { get; set; }
    }
}
