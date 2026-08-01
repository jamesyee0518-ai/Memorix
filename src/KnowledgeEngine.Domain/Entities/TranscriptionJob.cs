namespace KnowledgeEngine.Domain.Entities;

/// <summary>
/// A transcription task with full four-layer decoupling metadata.
/// </summary>
public class TranscriptionJob
{
    public Guid Id { get; set; }
    public Guid AudioAssetId { get; set; }
    public Guid? WorkspaceId { get; set; }
    public Guid UserId { get; set; }

    // Four-layer decoupling
    /// <summary>LOCAL_DEVICE / LOCAL_LAN_NODE / MEMORIX_CLOUD / THIRD_PARTY_CLOUD</summary>
    public string ExecutionMode { get; set; } = "LOCAL_DEVICE";

    /// <summary>NO_CREDENTIAL / USER_BYOK / TENANT_BYOK / PLATFORM_MANAGED</summary>
    public string CredentialMode { get; set; } = "NO_CREDENTIAL";

    public string ProviderId { get; set; } = string.Empty;
    public string ModelId { get; set; } = string.Empty;

    /// <summary>STOP / LOCAL_FALLBACK / PLATFORM_FALLBACK</summary>
    public string FallbackPolicy { get; set; } = "STOP";

    // Request options
    public string? Language { get; set; }
    public bool EnableVad { get; set; } = true;
    public bool EnableSpeakerDiarization { get; set; }
    public bool EnablePunctuation { get; set; } = true;
    public string? Hotwords { get; set; }  // JSON array

    // Cost tracking
    public decimal? EstimatedCost { get; set; }
    public decimal? ActualCost { get; set; }

    /// <summary>pending / running / completed / failed / cancelled</summary>
    public string Status { get; set; } = "pending";
    public string? ErrorMessage { get; set; }

    // Result link
    public Guid? DocumentId { get; set; }
    public int? SegmentCount { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}
