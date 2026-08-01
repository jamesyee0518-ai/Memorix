using KnowledgeEngine.Domain.Enums;

namespace KnowledgeEngine.Application.DTOs;

// ── ASR (Speech-to-Text) DTOs ──

public class AsrTranscriptionRequest
{
    public string AudioFilePath { get; set; } = string.Empty;
    public string? AudioCacheKey { get; set; }
    public string MimeType { get; set; } = "audio/wav";
    public long FileSizeBytes { get; set; }
    public long DurationMs { get; set; }

    public string? Language { get; set; }
    public bool EnableVad { get; set; } = true;
    public bool EnableSpeakerDiarization { get; set; }
    public bool EnablePunctuation { get; set; } = true;
    public bool EnableWordTimestamp { get; set; }
    public List<string>? Hotwords { get; set; }

    public DataClassification DataClassification { get; set; } = DataClassification.INTERNAL;
    public ExecutionMode? PreferredExecutionMode { get; set; }
    public CredentialMode? PreferredCredentialMode { get; set; }
    public string? PreferredProviderId { get; set; }
    public string? PreferredModelId { get; set; }
    public string FallbackPolicy { get; set; } = FallbackPolicies.Stop;

    public string? SegmentUuidPrefix { get; set; }
    public Guid? UserId { get; set; }
    public Guid? WorkspaceId { get; set; }
    public Guid? TenantId { get; set; }
}

public class AsrTranscriptionResult
{
    public string ProviderId { get; set; } = string.Empty;
    public string ModelId { get; set; } = string.Empty;
    public string? Language { get; set; }
    public long DurationMs { get; set; }
    public List<AsrSegmentDto> Segments { get; set; } = new();
    public string FullText { get; set; } = string.Empty;
    public decimal? EstimatedCost { get; set; }
    public string? ProviderTaskId { get; set; }
    public Dictionary<string, object> Metadata { get; set; } = new();
}

public class AsrSegmentDto
{
    public string SegmentUuid { get; set; } = string.Empty;
    public long StartMs { get; set; }
    public long EndMs { get; set; }
    public string Text { get; set; } = string.Empty;
    public decimal Confidence { get; set; }
    public string? SpeakerKey { get; set; }
    public List<AsrWordDto>? Words { get; set; }
    public int SegmentIndex { get; set; }
}

public class AsrWordDto
{
    public long StartMs { get; set; }
    public long EndMs { get; set; }
    public string Text { get; set; } = string.Empty;
    public decimal Confidence { get; set; }
}

public class AsrStreamingRequest
{
    public string SessionId { get; set; } = string.Empty;
    public string? Language { get; set; }
    public bool EnablePunctuation { get; set; } = true;
    public List<string>? Hotwords { get; set; }
    public DataClassification DataClassification { get; set; } = DataClassification.INTERNAL;
    public string? PreferredProviderId { get; set; }
    public Guid? UserId { get; set; }
    public Guid? WorkspaceId { get; set; }
}

public class AsrPartialResult
{
    public string SessionId { get; set; } = string.Empty;
    public string PartialText { get; set; } = string.Empty;
    public string? FinalText { get; set; }
    public long? StartMs { get; set; }
    public long? EndMs { get; set; }
    public bool IsFinal { get; set; }
    public int SegmentIndex { get; set; }
}

// ── TTS (Text-to-Speech) DTOs ──

public class TtsRequest
{
    public string Text { get; set; } = string.Empty;
    public string? Language { get; set; }
    public string? VoiceId { get; set; }
    public decimal Speed { get; set; } = 1.0m;
    public decimal Pitch { get; set; } = 1.0m;
    public string OutputFormat { get; set; } = "wav";
    public int SampleRate { get; set; } = 22050;

    public DataClassification DataClassification { get; set; } = DataClassification.INTERNAL;
    public ExecutionMode? PreferredExecutionMode { get; set; }
    public CredentialMode? PreferredCredentialMode { get; set; }
    public string? PreferredProviderId { get; set; }
    public string? PreferredModelId { get; set; }
    public string FallbackPolicy { get; set; } = FallbackPolicies.LocalFallback;

    public Guid? UserId { get; set; }
    public Guid? WorkspaceId { get; set; }
    public Guid? TenantId { get; set; }
}

public class TtsResult
{
    public string ProviderId { get; set; } = string.Empty;
    public string ModelId { get; set; } = string.Empty;
    public string OutputFilePath { get; set; } = string.Empty;
    public string OutputFormat { get; set; } = "wav";
    public long DurationMs { get; set; }
    public long FileSizeBytes { get; set; }
    public decimal? EstimatedCost { get; set; }
    public string? VoiceId { get; set; }
    public Dictionary<string, object> Metadata { get; set; } = new();
}

public class TtsStreamRequest
{
    public string SessionId { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public string? Language { get; set; }
    public string? VoiceId { get; set; }
    public decimal Speed { get; set; } = 1.0m;
    public int SampleRate { get; set; } = 22050;
    public DataClassification DataClassification { get; set; } = DataClassification.INTERNAL;
    public string? PreferredProviderId { get; set; }
    public Guid? UserId { get; set; }
    public Guid? WorkspaceId { get; set; }
}

public class AudioChunk
{
    public string SessionId { get; set; } = string.Empty;
    public byte[] Data { get; set; } = Array.Empty<byte>();
    public int ChunkIndex { get; set; }
    public bool IsFinal { get; set; }
    public string Format { get; set; } = "pcm_s16le";
    public int SampleRate { get; set; } = 22050;
}

public class VoiceProfile
{
    public string VoiceId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Language { get; set; }
    public string? Gender { get; set; }
    public string? PreviewUrl { get; set; }
    public bool IsClonable { get; set; }
    public Dictionary<string, object> Metadata { get; set; } = new();
}

// ── Provider Descriptor DTOs ──

public class AsrProviderDescriptor
{
    public string ProviderId { get; set; } = string.Empty;
    public string ModelId { get; set; } = string.Empty;
    public List<ExecutionMode> ExecutionModes { get; set; } = new();
    public List<CredentialMode> CredentialModes { get; set; } = new();
    public List<string> SupportedLanguages { get; set; } = new();

    public bool SupportsStreaming { get; set; }
    public bool SupportsBatch { get; set; } = true;
    public bool SupportsVad { get; set; }
    public bool SupportsPunctuation { get; set; }
    public bool SupportsDiarization { get; set; }
    public bool SupportsHotwords { get; set; }
    public bool SupportsWordTimestamp { get; set; }
    public bool SupportsSegmentTimestamp { get; set; } = true;

    public long? MaxFileBytes { get; set; }
    public long? MaxAudioDurationMs { get; set; }
    public List<string> AcceptedMimeTypes { get; set; } = new();

    public bool SendsAudioOffDevice { get; set; }
    public ProviderDataRetention StoresProviderData { get; set; } = ProviderDataRetention.UNKNOWN;
    public string? DataRegion { get; set; }
    public string? RetentionPolicy { get; set; }

    public string? PricingUnit { get; set; }
}

public class TtsProviderDescriptor
{
    public string ProviderId { get; set; } = string.Empty;
    public string ModelId { get; set; } = string.Empty;
    public List<ExecutionMode> ExecutionModes { get; set; } = new();
    public List<CredentialMode> CredentialModes { get; set; } = new();
    public List<string> SupportedLanguages { get; set; } = new();

    public bool SupportsStreaming { get; set; }
    public bool SupportsBatch { get; set; } = true;
    public bool SupportsVoiceCloning { get; set; }
    public bool SupportsSpeedControl { get; set; }
    public bool SupportsPitchControl { get; set; }

    public List<string> OutputFormats { get; set; } = new() { "wav" };
    public List<int> SupportedSampleRates { get; set; } = new() { 22050 };

    public bool SendsAudioOffDevice { get; set; }
    public ProviderDataRetention StoresProviderData { get; set; } = ProviderDataRetention.UNKNOWN;
    public string? DataRegion { get; set; }
    public string? RetentionPolicy { get; set; }

    public string? PricingUnit { get; set; }
}

// ── Common DTOs ──

public class ProviderHealth
{
    public string ProviderId { get; set; } = string.Empty;
    public bool IsHealthy { get; set; }
    public long LatencyMs { get; set; }
    public string? StatusMessage { get; set; }
    public DateTime CheckedAt { get; set; } = DateTime.UtcNow;
}

public class CostEstimate
{
    public string ProviderId { get; set; } = string.Empty;
    public string ModelId { get; set; } = string.Empty;
    public string PricingUnit { get; set; } = string.Empty;
    public decimal Units { get; set; }
    public decimal EstimatedCost { get; set; }
    public string Currency { get; set; } = "CNY";
}

public class ValidationResult
{
    public bool IsValid { get; set; }
    public List<string> Errors { get; set; } = new();
    public List<string> Warnings { get; set; } = new();

    public static ValidationResult Ok() => new() { IsValid = true };
    public static ValidationResult Fail(params string[] errors) => new() { IsValid = false, Errors = errors.ToList() };
}

public class ProviderFilter
{
    public string? Capability { get; set; }
    public List<ExecutionMode>? ExecutionModes { get; set; }
    public List<CredentialMode>? CredentialModes { get; set; }
    public string? Language { get; set; }
    public bool? SupportsStreaming { get; set; }
    public bool? SupportsVad { get; set; }
    public bool? SupportsPunctuation { get; set; }
    public bool? SupportsDiarization { get; set; }
    public bool? SupportsHotwords { get; set; }
    public bool? SendsAudioOffDevice { get; set; }
    public string? ProviderId { get; set; }
}

// ── Credential DTOs ──

public class StoreCredentialRequest
{
    public string ProviderId { get; set; } = string.Empty;
    public string CredentialType { get; set; } = "api_key";
    public string Secret { get; set; } = string.Empty;
    public string OwnerType { get; set; } = "user";
    public Guid OwnerId { get; set; }
    public Guid? TenantId { get; set; }
    public string? Label { get; set; }
    public DateTime? ExpiresAt { get; set; }
}

public class CredentialDto
{
    public Guid Id { get; set; }
    public string ProviderId { get; set; } = string.Empty;
    public string CredentialType { get; set; } = string.Empty;
    public string OwnerType { get; set; } = string.Empty;
    public Guid OwnerId { get; set; }
    public string? Label { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime? LastVerifiedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; }
}

// ── Routing DTOs ──

public class AsrRoutingContext
{
    public DataClassification DataClassification { get; set; } = DataClassification.INTERNAL;
    public ExecutionMode? PreferredExecutionMode { get; set; }
    public CredentialMode? PreferredCredentialMode { get; set; }
    public string? PreferredProviderId { get; set; }
    public string? PreferredModelId { get; set; }
    public string? Language { get; set; }
    public bool EnableVad { get; set; }
    public bool EnableSpeakerDiarization { get; set; }
    public bool EnablePunctuation { get; set; }
    public bool EnableHotwords { get; set; }
    public bool EnableWordTimestamp { get; set; }
    public long FileSizeBytes { get; set; }
    public long DurationMs { get; set; }
    public string MimeType { get; set; } = "audio/wav";
    public string FallbackPolicy { get; set; } = FallbackPolicies.Stop;
    public Guid? UserId { get; set; }
    public Guid? WorkspaceId { get; set; }
    public Guid? TenantId { get; set; }
}

public class TtsRoutingContext
{
    public DataClassification DataClassification { get; set; } = DataClassification.INTERNAL;
    public ExecutionMode? PreferredExecutionMode { get; set; }
    public CredentialMode? PreferredCredentialMode { get; set; }
    public string? PreferredProviderId { get; set; }
    public string? PreferredModelId { get; set; }
    public string? Language { get; set; }
    public string? VoiceId { get; set; }
    public bool SupportsStreaming { get; set; }
    public string OutputFormat { get; set; } = "wav";
    public string FallbackPolicy { get; set; } = FallbackPolicies.LocalFallback;
    public Guid? UserId { get; set; }
    public Guid? WorkspaceId { get; set; }
    public Guid? TenantId { get; set; }
}

// ── Audio Asset DTOs ──

public class AudioAssetDto
{
    public Guid Id { get; set; }
    public Guid SourceId { get; set; }
    public Guid? WorkspaceId { get; set; }
    public string OriginalFilePath { get; set; } = string.Empty;
    public string? NormalizedFilePath { get; set; }
    public string SourceSha256 { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public string MimeType { get; set; } = string.Empty;
    public long DurationMs { get; set; }
    public int SampleRate { get; set; }
    public int Channels { get; set; }
    public string DataClassification { get; set; } = string.Empty;
    public bool AllowsOffDevice { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class TranscriptionJobDto
{
    public Guid Id { get; set; }
    public Guid AudioAssetId { get; set; }
    public Guid? WorkspaceId { get; set; }
    public Guid UserId { get; set; }

    public string ExecutionMode { get; set; } = string.Empty;
    public string CredentialMode { get; set; } = string.Empty;
    public string ProviderId { get; set; } = string.Empty;
    public string ModelId { get; set; } = string.Empty;
    public string FallbackPolicy { get; set; } = string.Empty;

    public string? Language { get; set; }
    public bool EnableVad { get; set; }
    public bool EnableSpeakerDiarization { get; set; }
    public bool EnablePunctuation { get; set; }
    public string? Hotwords { get; set; }

    public decimal? EstimatedCost { get; set; }
    public decimal? ActualCost { get; set; }

    public string Status { get; set; } = string.Empty;
    public string? ErrorMessage { get; set; }
    public Guid? DocumentId { get; set; }
    public int? SegmentCount { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}

public class TranscriptionSegmentDto
{
    public Guid Id { get; set; }
    public Guid TranscriptionJobId { get; set; }
    public Guid? DocumentId { get; set; }
    public string SegmentUuid { get; set; } = string.Empty;
    public long SourceStartMs { get; set; }
    public long SourceEndMs { get; set; }
    public string ProviderId { get; set; } = string.Empty;
    public string ModelId { get; set; } = string.Empty;
    public decimal Confidence { get; set; }
    public string? SpeakerKey { get; set; }
    public string Text { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public int SegmentIndex { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class ProviderUsageRecordDto
{
    public Guid Id { get; set; }
    public Guid? TenantId { get; set; }
    public Guid UserId { get; set; }
    public Guid? WorkspaceId { get; set; }
    public string Capability { get; set; } = string.Empty;
    public string ProviderId { get; set; } = string.Empty;
    public string ModelId { get; set; } = string.Empty;
    public string CredentialMode { get; set; } = string.Empty;
    public string ExecutionMode { get; set; } = string.Empty;
    public long DurationMs { get; set; }
    public int RequestCount { get; set; }
    public decimal? InputUnits { get; set; }
    public decimal? OutputUnits { get; set; }
    public decimal? EstimatedCost { get; set; }
    public decimal? ActualCost { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? ErrorMessage { get; set; }
    public Guid? TranscriptionJobId { get; set; }
    public DateTime CreatedAt { get; set; }
}

// ── Audio Upload & Capture DTOs ──

public class AudioUploadRequest
{
    public string? Title { get; set; }
    public Guid? TopicId { get; set; }
    public string? Language { get; set; }
    public bool EnableVad { get; set; } = true;
    public bool EnableSpeakerDiarization { get; set; }
    public bool EnablePunctuation { get; set; } = true;
    public List<string>? Hotwords { get; set; }
    public DataClassification DataClassification { get; set; } = DataClassification.INTERNAL;
    public string? PreferredProviderId { get; set; }
    public string? PreferredModelId { get; set; }
    public string FallbackPolicy { get; set; } = FallbackPolicies.Stop;
    public bool AutoPublish { get; set; }
}

public class AudioUploadResponse
{
    public Guid AudioAssetId { get; set; }
    public Guid TranscriptionJobId { get; set; }
    public string Status { get; set; } = "pending";
    public string? EstimatedDuration { get; set; }
}

public class CreateTranscriptionJobRequest
{
    public Guid AudioAssetId { get; set; }
    public string? Language { get; set; }
    public bool EnableVad { get; set; } = true;
    public bool EnableSpeakerDiarization { get; set; }
    public bool EnablePunctuation { get; set; } = true;
    public List<string>? Hotwords { get; set; }
    public DataClassification DataClassification { get; set; } = DataClassification.INTERNAL;
    public string? PreferredProviderId { get; set; }
    public string? PreferredModelId { get; set; }
    public string FallbackPolicy { get; set; } = FallbackPolicies.Stop;
}

public class TranscriptionStatusResponse
{
    public Guid JobId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? ErrorMessage { get; set; }
    public int? SegmentCount { get; set; }
    public string? ProviderId { get; set; }
    public string? ModelId { get; set; }
    public decimal? EstimatedCost { get; set; }
    public decimal? ActualCost { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public List<TranscriptionSegmentDto>? Segments { get; set; }
}
