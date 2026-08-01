using KnowledgeEngine.Application.DTOs;

namespace KnowledgeEngine.Application.Interfaces;

/// <summary>
/// Audio cache service for FFmpeg-normalized audio deduplication.
/// Cache key: source_sha256 + sample_rate + channels + normalize_version.
/// Avoids redundant FFmpeg transcoding and normalization passes.
/// </summary>
public interface IAudioCacheService
{
    /// <summary>
    /// Returns a cached normalized audio path if available, or null.
    /// </summary>
    Task<string?> GetAsync(string cacheKey, CancellationToken ct);

    /// <summary>
    /// Stores a normalized audio file in the cache.
    /// </summary>
    Task<string> PutAsync(string cacheKey, string sourceFilePath, CancellationToken ct);

    /// <summary>
    /// Computes the cache key from audio asset properties.
    /// </summary>
    string ComputeCacheKey(string sourceSha256, int sampleRate, int channels, int normalizeVersion = 1);

    /// <summary>
    /// Removes expired cache entries.
    /// </summary>
    Task PurgeAsync(TimeSpan maxAge, CancellationToken ct);
}

/// <summary>
/// VAD (Voice Activity Detection) service for physical audio segmentation.
/// VAD segments are the universal time baseline for all downstream capabilities.
/// </summary>
public interface IVadService
{
    /// <summary>
    /// Performs VAD on the given audio file and returns speech segments.
    /// </summary>
    Task<List<VadSegment>> DetectSegmentsAsync(string audioFilePath, CancellationToken ct);

    /// <summary>
    /// Splits an audio file into physical segment files based on VAD results.
    /// </summary>
    Task<List<string>> SplitAudioAsync(string audioFilePath, List<VadSegment> segments, string outputDir, CancellationToken ct);
}

/// <summary>
/// Represents a single VAD-detected speech segment.
/// </summary>
public class VadSegment
{
    public long StartMs { get; set; }
    public long EndMs { get; set; }
    public decimal Confidence { get; set; }
}

/// <summary>
/// Media preparation service orchestrating the full pre-ASR pipeline:
/// file validation → SHA-256 dedup → FFmpeg normalization → audio cache → VAD → physical segments.
/// </summary>
public interface IMediaPreparationService
{
    /// <summary>
    /// Prepares an audio file for ASR processing.
    /// Returns the normalized file path and VAD segments.
    /// </summary>
    Task<MediaPreparationResult> PrepareAsync(string audioFilePath, string mimeType, CancellationToken ct);

    /// <summary>
    /// Computes SHA-256 hash of a file for deduplication.
    /// </summary>
    Task<string> ComputeSha256Async(string filePath, CancellationToken ct);
}

/// <summary>
/// Result of media preparation.
/// </summary>
public class MediaPreparationResult
{
    public string NormalizedFilePath { get; set; } = string.Empty;
    public string SourceSha256 { get; set; } = string.Empty;
    public string CacheKey { get; set; } = string.Empty;
    public long DurationMs { get; set; }
    public int SampleRate { get; set; }
    public int Channels { get; set; }
    public List<VadSegment> VadSegments { get; set; } = new();
    public List<string> SegmentFilePaths { get; set; } = new();
}

/// <summary>
/// Audio policy router for capability-to-provider resolution.
/// Implements the 8-step routing strategy: privacy → execution → credential → capability → language → health/cost → user preference → fallback.
/// </summary>
public interface IAudioPolicyRouter
{
    /// <summary>
    /// Resolves the best ASR provider for the given routing context.
    /// </summary>
    Task<IAsrProvider> ResolveAsrProviderAsync(AsrRoutingContext context, CancellationToken ct);

    /// <summary>
    /// Resolves the best TTS provider for the given routing context.
    /// </summary>
    Task<ITtsProvider> ResolveTtsProviderAsync(TtsRoutingContext context, CancellationToken ct);

    /// <summary>
    /// Explains the routing decision for debugging and UI display.
    /// </summary>
    Task<RoutingDecision> ExplainAsrRoutingAsync(AsrRoutingContext context, CancellationToken ct);
}

/// <summary>
/// Routing decision explanation for debugging.
/// </summary>
public class RoutingDecision
{
    public string SelectedProviderId { get; set; } = string.Empty;
    public string SelectedModelId { get; set; } = string.Empty;
    public string ExecutionMode { get; set; } = string.Empty;
    public string CredentialMode { get; set; } = string.Empty;
    public List<string> Steps { get; set; } = new();
    public List<string> EliminatedProviders { get; set; } = new();
    public string? FallbackReason { get; set; }
}

/// <summary>
/// Audio capability orchestrator for end-to-end transcription management.
/// Coordinates media preparation, policy routing, provider execution, and result persistence.
/// </summary>
public interface IAudioCapabilityOrchestrator
{
    /// <summary>
    /// Creates and starts a transcription job from an uploaded audio file.
    /// </summary>
    Task<Guid> StartTranscriptionAsync(Guid audioAssetId, CreateTranscriptionJobRequest request, Guid userId, Guid? workspaceId, CancellationToken ct);

    /// <summary>
    /// Gets the current status and segments of a transcription job.
    /// </summary>
    Task<TranscriptionStatusResponse> GetJobStatusAsync(Guid jobId, CancellationToken ct);

    /// <summary>
    /// Cancels an in-progress transcription job.
    /// </summary>
    Task CancelJobAsync(Guid jobId, CancellationToken ct);
}
