using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using KnowledgeEngine.Application.DTOs;
using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Domain.Enums;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace KnowledgeEngine.Infrastructure.Audio.Providers;

/// <summary>
/// FunASR punctuation restoration provider using the CT-Punc model.
/// Accepts raw transcription text (without punctuation) and returns the text
/// with punctuation restored. This provider implements <see cref="IAsrProvider"/>
/// so it can participate in the provider pipeline as a post-processing capability
/// (capability "audio.punctuation").
/// </summary>
public class FunAsrPunctuationProvider : IAsrProvider
{
    private const string ProviderIdValue = "funasr_punc";
    private const string ModelIdValue = "ct-punc";
    private const string HttpClientName = "FunAsr";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<FunAsrPunctuationProvider> _logger;
    private readonly string _baseUrl;

    /// <summary>
    /// Creates a new <see cref="FunAsrPunctuationProvider"/>.
    /// </summary>
    /// <param name="httpClientFactory">HTTP client factory for creating named clients.</param>
    /// <param name="configuration">Application configuration for reading the FunASR base URL.</param>
    /// <param name="logger">Logger for diagnostics.</param>
    public FunAsrPunctuationProvider(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<FunAsrPunctuationProvider> logger)
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
            SupportedLanguages = ["zh", "en"],
            SupportsStreaming = false,
            SupportsBatch = true,
            SupportsVad = false,
            SupportsPunctuation = true,
            SupportsDiarization = false,
            SupportsHotwords = false,
            SupportsWordTimestamp = false,
            SupportsSegmentTimestamp = false,
            SendsAudioOffDevice = false,
            StoresProviderData = ProviderDataRetention.NO,
            AcceptedMimeTypes = ["text/plain", "application/json"]
        };

        return Task.FromResult(descriptor);
    }

    /// <inheritdoc/>
    public Task<ValidationResult> ValidateRequestAsync(AsrTranscriptionRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.AudioFilePath))
        {
            return Task.FromResult(ValidationResult.Fail("AudioFilePath is required (should point to a text file containing raw transcription)."));
        }

        if (!File.Exists(request.AudioFilePath))
        {
            return Task.FromResult(ValidationResult.Fail($"Input file not found: {request.AudioFilePath}"));
        }

        if (string.IsNullOrWhiteSpace(_baseUrl))
        {
            return Task.FromResult(ValidationResult.Fail("FunASR base URL is not configured."));
        }

        return Task.FromResult(ValidationResult.Ok());
    }

    /// <inheritdoc/>
    /// <remarks>
    /// For this provider, <paramref name="request"/>.<see cref="AsrTranscriptionRequest.AudioFilePath"/>
    /// points to a text file containing the raw transcription text (without punctuation).
    /// The provider reads the text, sends it to the FunASR punctuation API, and returns
    /// the punctuated text as a single-segment <see cref="AsrTranscriptionResult"/>.
    /// </remarks>
    public async Task<AsrTranscriptionResult> TranscribeAsync(AsrTranscriptionRequest request, CancellationToken ct)
    {
        // Read raw transcription text from the input file.
        var rawText = await File.ReadAllTextAsync(request.AudioFilePath, ct);
        if (string.IsNullOrWhiteSpace(rawText))
        {
            _logger.LogWarning("Input text file is empty: {FilePath}", request.AudioFilePath);
            return new AsrTranscriptionResult
            {
                ProviderId = ProviderIdValue,
                ModelId = ModelIdValue,
                Language = request.Language,
                DurationMs = request.DurationMs,
                Segments = [],
                FullText = string.Empty
            };
        }

        var httpClient = _httpClientFactory.CreateClient(HttpClientName);

        var requestBody = new FunAsrPuncRequest { Text = rawText.Trim() };

        _logger.LogInformation(
            "Calling FunASR punctuation API for {CharCount} chars of text",
            rawText.Length);

        var stopwatch = Stopwatch.StartNew();
        HttpResponseMessage response;
        try
        {
            response = await httpClient.PostAsJsonAsync($"{_baseUrl}/api/punc", requestBody, JsonOptions, ct);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "FunASR punctuation API network error: {Message}", ex.Message);
            throw new InvalidOperationException($"FunASR punctuation API network error: {ex.Message}", ex);
        }
        stopwatch.Stop();

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError("FunASR punctuation API returned {StatusCode}: {ErrorBody}", response.StatusCode, errorBody);
            throw new InvalidOperationException($"FunASR punctuation API returned status {(int)response.StatusCode}: {errorBody}");
        }

        var puncResponse = await response.Content.ReadFromJsonAsync<FunAsrPuncResponse>(JsonOptions, ct);
        if (puncResponse == null)
        {
            throw new InvalidOperationException("FunASR punctuation API returned null response.");
        }

        var punctuatedText = puncResponse.Text?.Trim() ?? rawText.Trim();

        _logger.LogInformation(
            "FunASR punctuation completed: {InLen} → {OutLen} chars in {ElapsedMs}ms",
            rawText.Length, punctuatedText.Length, stopwatch.ElapsedMilliseconds);

        // Return the punctuated text as a single segment.
        var segment = new AsrSegmentDto
        {
            SegmentUuid = GenerateSegmentUuid(request.SegmentUuidPrefix),
            StartMs = 0,
            EndMs = request.DurationMs,
            Text = punctuatedText,
            Confidence = 0,
            SegmentIndex = 0
        };

        return new AsrTranscriptionResult
        {
            ProviderId = ProviderIdValue,
            ModelId = ModelIdValue,
            Language = request.Language,
            DurationMs = request.DurationMs,
            Segments = [segment],
            FullText = punctuatedText,
            Metadata = new Dictionary<string, object>
            {
                ["engine"] = "funasr_punc",
                ["model"] = ModelIdValue,
                ["input_chars"] = rawText.Length,
                ["output_chars"] = punctuatedText.Length,
                ["latency_ms"] = stopwatch.ElapsedMilliseconds
            }
        };
    }

    /// <inheritdoc/>
    public IAsyncEnumerable<AsrPartialResult>? TranscribeStream(AsrStreamingRequest request, CancellationToken ct)
    {
        // Punctuation restoration does not support streaming.
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
                ? "FunASR punctuation runtime is healthy"
                : $"Health check returned {response.StatusCode}";
        }
        catch (Exception ex)
        {
            sw.Stop();
            health.IsHealthy = false;
            health.LatencyMs = sw.ElapsedMilliseconds;
            health.StatusMessage = $"FunASR punctuation health check failed: {ex.Message}";
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

    // ── FunASR punctuation API DTOs ──

    private sealed class FunAsrPuncRequest
    {
        [JsonPropertyName("text")]
        public string Text { get; set; } = string.Empty;
    }

    private sealed class FunAsrPuncResponse
    {
        [JsonPropertyName("text")]
        public string? Text { get; set; }
    }
}
