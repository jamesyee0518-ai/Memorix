namespace KnowledgeEngine.Domain.Entities;

/// <summary>
/// A physical audio segment with a stable UUID for cross-referencing.
/// All downstream artifacts (Summary, Entity, Todo, Quote, etc.) reference segment_uuid.
/// </summary>
public class TranscriptionSegment
{
    public Guid Id { get; set; }
    public Guid TranscriptionJobId { get; set; }
    public Guid? DocumentId { get; set; }
    public Guid? WorkspaceId { get; set; }

    /// <summary>Stable UUID for external references. Never changes across versions.</summary>
    public string SegmentUuid { get; set; } = string.Empty;

    /// <summary>Start time relative to the original audio, in milliseconds.</summary>
    public long SourceStartMs { get; set; }

    /// <summary>End time relative to the original audio, in milliseconds.</summary>
    public long SourceEndMs { get; set; }

    public string ProviderId { get; set; } = string.Empty;
    public string ModelId { get; set; } = string.Empty;

    public decimal Confidence { get; set; }
    public string? SpeakerKey { get; set; }
    public string Text { get; set; } = string.Empty;

    /// <summary>RAW_MODEL / POST_PROCESSED / SERVER_RETRANSCRIBED / USER_EDITED / MERGED / PUBLISHED</summary>
    public string Version { get; set; } = "RAW_MODEL";

    /// <summary>Sequence number within the transcription job.</summary>
    public int SegmentIndex { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
