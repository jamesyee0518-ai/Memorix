namespace KnowledgeEngine.Domain.Entities;

/// <summary>
/// An independent recording slice produced by the chunked recording strategy.
/// Each chunk is a self-contained audio file with its own header, checksum and
/// write-status. The collection of chunks forms the meeting's complete audio timeline.
/// </summary>
public class RecordingChunk
{
    public Guid Id { get; set; }
    public Guid MeetingId { get; set; }

    /// <summary>Link to the parent audio asset (if consolidated).</summary>
    public Guid? AssetId { get; set; }

    /// <summary>Zero-based sequence number within the meeting.</summary>
    public int SequenceNo { get; set; }

    /// <summary>Chunk start time relative to meeting start, in milliseconds.</summary>
    public long StartMs { get; set; }

    /// <summary>Chunk end time relative to meeting start, in milliseconds.</summary>
    public long EndMs { get; set; }

    /// <summary>Local file path of the completed chunk file.</summary>
    public string LocalUri { get; set; } = string.Empty;

    /// <summary>Audio codec, e.g. pcm_s16le, flac, opus.</summary>
    public string Codec { get; set; } = "pcm_s16le";

    public int SampleRate { get; set; } = 16000;
    public int Channels { get; set; } = 1;

    public long FileSize { get; set; }

    /// <summary>SHA-256 checksum of the completed chunk file.</summary>
    public string? Checksum { get; set; }

    /// <summary>WRITING / COMPLETE / RECOVERED / CORRUPTED / MISSING</summary>
    public string WriteStatus { get; set; } = ChunkWriteStatuses.Writing;

    /// <summary>PENDING / VALIDATED / REPAIRED / UNRECOVERABLE</summary>
    public string? RecoveryStatus { get; set; }

    /// <summary>Gap before this chunk in milliseconds (0 if continuous).</summary>
    public long TimelineGapBeforeMs { get; set; }

    public DateTime CreatedAt { get; set; }

    /// <summary>When the chunk was finalized (atomically renamed from .part).</summary>
    public DateTime? FinalizedAt { get; set; }
}

/// <summary>Recording chunk write status values.</summary>
public static class ChunkWriteStatuses
{
    public const string Writing = "WRITING";
    public const string Complete = "COMPLETE";
    public const string Recovered = "RECOVERED";
    public const string Corrupted = "CORRUPTED";
    public const string Missing = "MISSING";
}

/// <summary>Recording chunk recovery status values.</summary>
public static class ChunkRecoveryStatuses
{
    public const string Pending = "PENDING";
    public const string Validated = "VALIDATED";
    public const string Repaired = "REPAIRED";
    public const string Unrecoverable = "UNRECOVERABLE";
}
