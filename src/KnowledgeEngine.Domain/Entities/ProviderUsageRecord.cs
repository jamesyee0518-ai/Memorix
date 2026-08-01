namespace KnowledgeEngine.Domain.Entities;

/// <summary>
/// Records each provider invocation for billing, audit, and cost analysis.
/// </summary>
public class ProviderUsageRecord
{
    public Guid Id { get; set; }
    public Guid? TenantId { get; set; }
    public Guid UserId { get; set; }
    public Guid? WorkspaceId { get; set; }

    /// <summary>audio.transcription / audio.synthesis / audio.vad / audio.diarization</summary>
    public string Capability { get; set; } = string.Empty;

    public string ProviderId { get; set; } = string.Empty;
    public string ModelId { get; set; } = string.Empty;

    /// <summary>NO_CREDENTIAL / USER_BYOK / TENANT_BYOK / PLATFORM_MANAGED</summary>
    public string CredentialMode { get; set; } = "NO_CREDENTIAL";

    /// <summary>LOCAL_DEVICE / LOCAL_LAN_NODE / MEMORIX_CLOUD / THIRD_PARTY_CLOUD</summary>
    public string ExecutionMode { get; set; } = "LOCAL_DEVICE";

    /// <summary>Audio duration processed, in milliseconds.</summary>
    public long DurationMs { get; set; }

    public int RequestCount { get; set; } = 1;

    public decimal? InputUnits { get; set; }
    public decimal? OutputUnits { get; set; }

    public decimal? EstimatedCost { get; set; }
    public decimal? ActualCost { get; set; }

    /// <summary>success / failed / partial</summary>
    public string Status { get; set; } = "success";
    public string? ErrorMessage { get; set; }

    /// <summary>Link to the transcription job if applicable.</summary>
    public Guid? TranscriptionJobId { get; set; }

    public DateTime CreatedAt { get; set; }
}
