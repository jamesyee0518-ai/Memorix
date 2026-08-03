namespace KnowledgeEngine.Domain.Enums;

/// <summary>
/// Where the capability actually executes.
/// </summary>
public enum ExecutionMode
{
    LOCAL_DEVICE,
    LOCAL_LAN_NODE,
    MEMORIX_CLOUD,
    THIRD_PARTY_CLOUD
}

/// <summary>
/// Who provides the credential for the capability.
/// </summary>
public enum CredentialMode
{
    NO_CREDENTIAL,
    USER_BYOK,
    TENANT_BYOK,
    PLATFORM_MANAGED
}

/// <summary>
/// Whether the provider stores user data after processing.
/// </summary>
public enum ProviderDataRetention
{
    UNKNOWN,
    NO,
    TEMPORARY,
    YES
}

/// <summary>
/// Data sensitivity classification for privacy routing.
/// </summary>
public enum DataClassification
{
    PUBLIC,
    INTERNAL,
    PRIVATE,
    STRICT_LOCAL
}

/// <summary>
/// Audio capability identifiers.
/// </summary>
public static class AudioCapabilities
{
    public const string Vad = "audio.vad";
    public const string Transcription = "audio.transcription";
    public const string Diarization = "audio.diarization";
    public const string Punctuation = "audio.punctuation";
    public const string Correction = "audio.correction";
    public const string Synthesis = "audio.synthesis";
}

/// <summary>
/// Fallback policy when a provider fails.
/// </summary>
public static class FallbackPolicies
{
    public const string Stop = "STOP";
    public const string LocalFallback = "LOCAL_FALLBACK";
    public const string PlatformFallback = "PLATFORM_FALLBACK";
}

/// <summary>
/// Transcription segment version labels.
/// </summary>
public static class SegmentVersions
{
    public const string RawModel = "RAW_MODEL";
    public const string PostProcessed = "POST_PROCESSED";
    public const string ServerRetranscribed = "SERVER_RETRANSCRIBED";
    public const string UserEdited = "USER_EDITED";
    public const string Merged = "MERGED";
    public const string Published = "PUBLISHED";

    /// <summary>Live/streaming interim result produced before final transcription.</summary>
    public const string Interim = "INTERIM";
}

/// <summary>
/// Transcription job status values.
/// </summary>
public static class TranscriptionJobStatuses
{
    public const string Pending = "pending";
    public const string Running = "running";
    public const string Completed = "completed";
    public const string Failed = "failed";
    public const string Cancelled = "cancelled";
}

/// <summary>
/// Provider credential status values.
/// </summary>
public static class CredentialStatuses
{
    public const string Active = "active";
    public const string Disabled = "disabled";
    public const string Expired = "expired";
}

/// <summary>
/// Provider credential owner types.
/// </summary>
public static class CredentialOwnerTypes
{
    public const string User = "user";
    public const string Tenant = "tenant";
}

/// <summary>
/// Pricing unit types for provider cost estimation.
/// </summary>
public static class PricingUnits
{
    public const string Request = "REQUEST";
    public const string Second = "SECOND";
    public const string Minute = "MINUTE";
    public const string Token = "TOKEN";
}
