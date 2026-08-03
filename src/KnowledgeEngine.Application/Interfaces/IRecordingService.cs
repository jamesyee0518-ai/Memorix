using KnowledgeEngine.Application.DTOs;

namespace KnowledgeEngine.Application.Interfaces;

/// <summary>
/// Recording control service for meeting audio capture.
/// Implements the short-chunk independent-encapsulation strategy described in
/// §5.2.1: each chunk is a self-contained file with its own header, checksum,
/// and write-status. Recording and real-time STT are decoupled — recording
/// failure does not affect transcription and vice versa.
/// </summary>
public interface IRecordingService
{
    /// <summary>
    /// Starts recording for a meeting. Transitions meeting status to RECORDING.
    /// Creates the first recording chunk and begins writing to disk.
    /// </summary>
    Task<RecordingSessionDto> StartAsync(Guid meetingId, StartRecordingRequest request, Guid userId, CancellationToken ct);

    /// <summary>
    /// Pauses recording. The current in-progress chunk is finalized and the
    /// meeting enters a PAUSED state. A timeline gap is recorded.
    /// </summary>
    Task<RecordingSessionDto> PauseAsync(Guid meetingId, CancellationToken ct);

    /// <summary>
    /// Resumes recording after a pause. A new chunk is started.
    /// </summary>
    Task<RecordingSessionDto> ResumeAsync(Guid meetingId, CancellationToken ct);

    /// <summary>
    /// Stops recording and finalizes the meeting. Transitions meeting to
    /// FINALIZING status, then COMPLETED. All in-progress chunks are
    /// finalized and checksums computed.
    /// </summary>
    Task<RecordingSessionDto> StopAsync(Guid meetingId, CancellationToken ct);

    /// <summary>
    /// Gets the current recording status for a meeting.
    /// </summary>
    Task<RecordingSessionDto?> GetStatusAsync(Guid meetingId, CancellationToken ct);

    /// <summary>
    /// Lists all recording chunks for a meeting.
    /// </summary>
    Task<List<RecordingChunkDto>> GetChunksAsync(Guid meetingId, CancellationToken ct);

    /// <summary>
    /// Scans for meetings left in RECORDING/PAUSED/FINALIZING status after
    /// a crash or abnormal exit. Validates completed chunks, attempts to
    /// repair the last in-progress chunk, rebuilds the chunk index, and
    /// marks unrecoverable gaps.
    /// </summary>
    Task<List<RecoveryResultDto>> RecoverIncompleteMeetingsAsync(CancellationToken ct);
}

/// <summary>
/// In-memory recording session state. Not persisted — reconstructed from
/// RecordingChunk records and Meeting status on restart.
/// </summary>
public class RecordingSessionDto
{
    public Guid MeetingId { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime? StartedAt { get; set; }
    public long ElapsedMs { get; set; }
    public int ChunkCount { get; set; }
    public int CurrentChunkSequence { get; set; }
    public string Codec { get; set; } = "pcm_s16le";
    public int SampleRate { get; set; } = 16000;
    public int Channels { get; set; } = 1;
    public long TotalBytesWritten { get; set; }
    public List<RecordingChunkDto> Chunks { get; set; } = new();
}

/// <summary>
/// Result of a crash-recovery scan for a single meeting.
/// </summary>
public class RecoveryResultDto
{
    public Guid MeetingId { get; set; }
    public string MeetingTitle { get; set; } = string.Empty;
    public string PreviousStatus { get; set; } = string.Empty;
    public string NewStatus { get; set; } = string.Empty;
    public int ValidChunks { get; set; }
    public int CorruptedChunks { get; set; }
    public int MissingChunks { get; set; }
    public long RecoveredDurationMs { get; set; }
    public List<string> Gaps { get; set; } = new();
}
