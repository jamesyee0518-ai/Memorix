namespace KnowledgeEngine.Domain.Entities;

/// <summary>
/// Incremental ingestion cursor for idempotent event import.
///
/// <para>
/// Each external source (an agent's transcript file, hook stream, etc.) tracks
/// its last-ingested position. Before importing a batch, the ingest service
/// checks the cursor to skip already-processed events — enabling crash-safe,
/// resumable ingestion.
/// </para>
/// </summary>
public class IngestOffset
{
    public Guid Id { get; set; }

    /// <summary>Source identifier: "&lt;agent&gt;:&lt;file/path or stream&gt;" (e.g. "claude:~/.claude/projects/slug/abc.jsonl").</summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>Last ingested position — a line number, event sequence, or timestamp.</summary>
    public string Offset { get; set; } = string.Empty;

    /// <summary>Checksum of the last-ingested batch (for dedup verification).</summary>
    public string? Checksum { get; set; }

    public DateTime IngestedAt { get; set; }
}
