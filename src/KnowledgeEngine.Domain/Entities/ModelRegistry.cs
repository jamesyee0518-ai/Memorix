namespace KnowledgeEngine.Domain.Entities;

/// <summary>
/// Unified model registration entity for the audio capability platform.
/// Each record represents one model variant offered by a provider for a
/// specific capability (transcription, synthesis, VAD, punctuation).
/// </summary>
public class ModelRegistry
{
    public Guid Id { get; set; }

    /// <summary>Provider identifier (e.g. "whispercpp", "funasr", "fishspeech").</summary>
    public string ProviderId { get; set; } = string.Empty;

    /// <summary>Model identifier (e.g. "whisper-large-v3", "paraformer-zh").</summary>
    public string ModelId { get; set; } = string.Empty;

    /// <summary>Human-readable display name for UI and logs.</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Capability this model serves: audio.transcription / audio.synthesis /
    /// audio.vad / audio.punctuation.
    /// </summary>
    public string Capability { get; set; } = string.Empty;

    /// <summary>Comma-separated execution modes (LOCAL_DEVICE, LOCAL_LAN_NODE, MEMORIX_CLOUD, THIRD_PARTY_CLOUD).</summary>
    public string ExecutionModes { get; set; } = string.Empty;

    /// <summary>Comma-separated credential modes (NO_CREDENTIAL, USER_BYOK, TENANT_BYOK, PLATFORM_MANAGED).</summary>
    public string CredentialModes { get; set; } = string.Empty;

    /// <summary>Comma-separated supported languages (e.g. "zh,en,ja"). Empty means all languages.</summary>
    public string SupportedLanguages { get; set; } = string.Empty;

    /// <summary>Maximum accepted file size in bytes, or null for no limit.</summary>
    public long? MaxFileBytes { get; set; }

    /// <summary>Maximum accepted audio duration in milliseconds, or null for no limit.</summary>
    public long? MaxAudioDurationMs { get; set; }

    /// <summary>Comma-separated accepted MIME types (e.g. "audio/wav,audio/mp3").</summary>
    public string AcceptedMimeTypes { get; set; } = string.Empty;

    public bool SupportsStreaming { get; set; }
    public bool SupportsBatch { get; set; } = true;
    public bool SupportsVad { get; set; }
    public bool SupportsPunctuation { get; set; }
    public bool SupportsDiarization { get; set; }
    public bool SupportsHotwords { get; set; }
    public bool SupportsWordTimestamp { get; set; }
    public bool SupportsSegmentTimestamp { get; set; } = true;

    /// <summary>Whether the model sends audio data off the user's device.</summary>
    public bool SendsAudioOffDevice { get; set; }

    /// <summary>Whether the provider stores user data after processing.</summary>
    public bool StoresProviderData { get; set; }

    /// <summary>Pricing unit (REQUEST / SECOND / MINUTE / TOKEN).</summary>
    public string? PricingUnit { get; set; }

    /// <summary>Data region for compliance (e.g. "CN", "US", "EU").</summary>
    public string? DataRegion { get; set; }

    /// <summary>Provider data retention policy description.</summary>
    public string? RetentionPolicy { get; set; }

    /// <summary>Whether this model registration is enabled for routing.</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>Health status: healthy / degraded / unhealthy / unknown.</summary>
    public string HealthStatus { get; set; } = ModelRegistryStatuses.Unknown;

    public DateTime? LastHealthCheckAt { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// Health status string constants for <see cref="ModelRegistry.HealthStatus"/>.
/// </summary>
public static class ModelRegistryStatuses
{
    public const string Healthy = "healthy";
    public const string Degraded = "degraded";
    public const string Unhealthy = "unhealthy";
    public const string Unknown = "unknown";
}
