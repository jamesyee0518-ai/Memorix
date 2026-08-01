using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using KnowledgeEngine.Application.DTOs;
using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace KnowledgeEngine.Infrastructure.Audio.Providers;

/// <summary>
/// ASR provider that delegates transcription to a LAN compute node.
/// Audio is sent via HTTP to the node's endpoint, where a remote ASR
/// engine (e.g. whisper.cpp, FunASR) processes it on the node's hardware.
/// All traffic stays within the local network — no audio leaves the LAN.
/// </summary>
public class LanNodeProvider : IAsrProvider
{
    private const string ProviderIdValue = "lan_node";
    private const string DefaultModelId = "whisper-base";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    private readonly ILanNodeDiscovery _nodeDiscovery;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<LanNodeProvider> _logger;

    /// <summary>
    /// Creates a new <see cref="LanNodeProvider"/>.
    /// </summary>
    /// <param name="nodeDiscovery">LAN node discovery service for finding healthy nodes.</param>
    /// <param name="httpClientFactory">HTTP client factory for sending requests to the node.</param>
    /// <param name="logger">Logger for diagnostics.</param>
    public LanNodeProvider(
        ILanNodeDiscovery nodeDiscovery,
        IHttpClientFactory httpClientFactory,
        ILogger<LanNodeProvider> logger)
    {
        _nodeDiscovery = nodeDiscovery;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <inheritdoc/>
    public Task<AsrProviderDescriptor> GetDescriptorAsync(CancellationToken ct)
    {
        var descriptor = new AsrProviderDescriptor
        {
            ProviderId = ProviderIdValue,
            ModelId = DefaultModelId,
            ExecutionModes = [ExecutionMode.LOCAL_LAN_NODE],
            CredentialModes = [CredentialMode.NO_CREDENTIAL],
            SupportedLanguages = [], // empty = all languages
            SupportsStreaming = false,
            SupportsBatch = true,
            SupportsVad = true,
            SupportsPunctuation = true,
            SupportsDiarization = false,
            SupportsHotwords = false,
            SupportsWordTimestamp = true,
            SupportsSegmentTimestamp = true,
            SendsAudioOffDevice = false, // stays within LAN
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

        return Task.FromResult(ValidationResult.Ok());
    }

    /// <inheritdoc/>
    public async Task<AsrTranscriptionResult> TranscribeAsync(AsrTranscriptionRequest request, CancellationToken ct)
    {
        // Find a healthy LAN node that supports transcription.
        var node = await _nodeDiscovery.GetHealthyNodeAsync(AudioCapabilities.Transcription, ct);
        if (node == null)
        {
            throw new InvalidOperationException(
                "No healthy LAN node available for transcription. " +
                "Ensure at least one node is online and has a fresh heartbeat.");
        }

        _logger.LogInformation(
            "Delegating transcription to LAN node {NodeId} at {Endpoint} for file={FilePath}",
            node.Id, node.EndpointUrl, request.AudioFilePath);

        // Read the audio file and send it to the LAN node's transcription endpoint.
        using var httpClient = _httpClientFactory.CreateClient();
        httpClient.Timeout = TimeSpan.FromMinutes(10);

        await using var fileStream = File.OpenRead(request.AudioFilePath);
        using var multipart = new MultipartFormDataContent();

        var fileContent = new StreamContent(fileStream);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(
            !string.IsNullOrWhiteSpace(request.MimeType) ? request.MimeType : "audio/wav");
        multipart.Add(fileContent, "audio", Path.GetFileName(request.AudioFilePath));

        // Add optional parameters.
        if (!string.IsNullOrWhiteSpace(request.Language))
        {
            multipart.Add(new StringContent(request.Language), "language");
        }

        if (request.EnableWordTimestamp)
        {
            multipart.Add(new StringContent("true"), "word_timestamps");
        }

        if (request.EnablePunctuation)
        {
            multipart.Add(new StringContent("true"), "punctuation");
        }

        var transcriptionUrl = $"{node.EndpointUrl.TrimEnd('/')}/api/transcribe";

        using var response = await httpClient.PostAsync(transcriptionUrl, multipart, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<LanNodeTranscriptionResponse>(JsonOptions, ct)
                     ?? throw new InvalidOperationException("LAN node returned an empty transcription response.");

        // Update heartbeat since the node successfully processed the request.
        await _nodeDiscovery.UpdateHeartbeatAsync(node.Id, ct);

        // Map the response to the standard ASR result DTO.
        var segments = new List<AsrSegmentDto>();
        var fullTextBuilder = new System.Text.StringBuilder();
        var prefix = request.SegmentUuidPrefix;

        if (result.Segments != null)
        {
            for (var i = 0; i < result.Segments.Count; i++)
            {
                var seg = result.Segments[i];
                var text = seg.Text?.Trim() ?? string.Empty;

                if (fullTextBuilder.Length > 0)
                {
                    fullTextBuilder.Append(' ');
                }
                fullTextBuilder.Append(text);

                var segment = new AsrSegmentDto
                {
                    SegmentUuid = GenerateSegmentUuid(prefix),
                    StartMs = seg.StartMs,
                    EndMs = seg.EndMs,
                    Text = text,
                    Confidence = seg.Confidence,
                    SpeakerKey = seg.SpeakerKey,
                    SegmentIndex = i
                };

                if (seg.Words is { Count: > 0 })
                {
                    segment.Words = seg.Words.Select(w => new AsrWordDto
                    {
                        StartMs = w.StartMs,
                        EndMs = w.EndMs,
                        Text = w.Text?.Trim() ?? string.Empty,
                        Confidence = w.Confidence
                    }).ToList();
                }

                segments.Add(segment);
            }
        }

        var modelId = !string.IsNullOrWhiteSpace(result.ModelId)
            ? result.ModelId
            : DefaultModelId;

        _logger.LogInformation(
            "LAN node transcription completed: {SegmentCount} segments from node {NodeId} (model={Model})",
            segments.Count, node.Id, modelId);

        return new AsrTranscriptionResult
        {
            ProviderId = ProviderIdValue,
            ModelId = modelId,
            Language = result.Language ?? request.Language,
            DurationMs = result.DurationMs > 0 ? result.DurationMs : request.DurationMs,
            Segments = segments,
            FullText = !string.IsNullOrWhiteSpace(result.Text)
                ? result.Text.Trim()
                : fullTextBuilder.ToString().Trim(),
            Metadata = new Dictionary<string, object>
            {
                ["engine"] = "lan_node",
                ["node_id"] = node.Id.ToString(),
                ["node_endpoint"] = node.EndpointUrl,
                ["model"] = modelId
            }
        };
    }

    /// <inheritdoc/>
    public IAsyncEnumerable<AsrPartialResult>? TranscribeStream(AsrStreamingRequest request, CancellationToken ct)
    {
        // LAN node streaming is not supported in the current implementation.
        return null;
    }

    /// <inheritdoc/>
    public Task<CostEstimate>? EstimateCostAsync(AsrTranscriptionRequest request, CancellationToken ct)
    {
        // LAN node execution — no monetary cost (local hardware).
        return null;
    }

    /// <inheritdoc/>
    public Task CancelAsync(string providerTaskId, CancellationToken ct)
    {
        _logger.LogDebug("CancelAsync called for providerTaskId={ProviderTaskId} (no-op for LAN node)", providerTaskId);
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
            var node = await _nodeDiscovery.GetHealthyNodeAsync(AudioCapabilities.Transcription, ct);
            sw.Stop();

            health.IsHealthy = node != null;
            health.LatencyMs = sw.ElapsedMilliseconds;
            health.StatusMessage = health.IsHealthy
                ? $"Healthy LAN node available: {node!.NodeName} at {node.EndpointUrl}"
                : "No healthy LAN node available for transcription";
        }
        catch (Exception ex)
        {
            sw.Stop();
            health.IsHealthy = false;
            health.LatencyMs = sw.ElapsedMilliseconds;
            health.StatusMessage = $"LAN node health check failed: {ex.Message}";
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

    // ── LAN node response DTOs ──

    private sealed class LanNodeTranscriptionResponse
    {
        [JsonPropertyName("text")]
        public string? Text { get; set; }

        [JsonPropertyName("language")]
        public string? Language { get; set; }

        [JsonPropertyName("model_id")]
        public string? ModelId { get; set; }

        [JsonPropertyName("duration_ms")]
        public long DurationMs { get; set; }

        [JsonPropertyName("segments")]
        public List<LanNodeSegment>? Segments { get; set; }
    }

    private sealed class LanNodeSegment
    {
        [JsonPropertyName("start_ms")]
        public long StartMs { get; set; }

        [JsonPropertyName("end_ms")]
        public long EndMs { get; set; }

        [JsonPropertyName("text")]
        public string? Text { get; set; }

        [JsonPropertyName("confidence")]
        public decimal Confidence { get; set; }

        [JsonPropertyName("speaker_key")]
        public string? SpeakerKey { get; set; }

        [JsonPropertyName("words")]
        public List<LanNodeWord>? Words { get; set; }
    }

    private sealed class LanNodeWord
    {
        [JsonPropertyName("start_ms")]
        public long StartMs { get; set; }

        [JsonPropertyName("end_ms")]
        public long EndMs { get; set; }

        [JsonPropertyName("text")]
        public string? Text { get; set; }

        [JsonPropertyName("confidence")]
        public decimal Confidence { get; set; }
    }
}
