namespace KnowledgeEngine.Domain.Entities;

/// <summary>
/// Represents a raw or normalized audio file with privacy classification.
/// </summary>
public class AudioAsset
{
    public Guid Id { get; set; }
    public Guid SourceId { get; set; }
    public Guid? WorkspaceId { get; set; }
    public Guid? UserId { get; set; }

    /// <summary>Link to the meeting this asset belongs to (if imported via meeting workflow).</summary>
    public Guid? MeetingId { get; set; }

    /// <summary>Original file path or object key.</summary>
    public string OriginalFilePath { get; set; } = string.Empty;

    /// <summary>Path to the FFmpeg-normalized WAV file (16kHz, mono, pcm_s16le).</summary>
    public string? NormalizedFilePath { get; set; }

    /// <summary>SHA-256 of the original file for deduplication.</summary>
    public string SourceSha256 { get; set; } = string.Empty;

    public long FileSizeBytes { get; set; }
    public string MimeType { get; set; } = "audio/wav";

    /// <summary>Audio duration in milliseconds.</summary>
    public long DurationMs { get; set; }

    public int SampleRate { get; set; }
    public int Channels { get; set; }

    /// <summary>PUBLIC / INTERNAL / PRIVATE / STRICT_LOCAL</summary>
    public string DataClassification { get; set; } = "INTERNAL";

    /// <summary>Whether audio is allowed to leave the device.</summary>
    public bool AllowsOffDevice { get; set; } = true;

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
