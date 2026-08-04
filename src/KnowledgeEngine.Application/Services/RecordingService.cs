using KnowledgeEngine.Application.DTOs;
using KnowledgeEngine.Application.Exceptions;
using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.IO;
using System.Security.Cryptography;

namespace KnowledgeEngine.Application.Services;

/// <summary>
/// Recording control service implementing the short-chunk independent-encapsulation
/// strategy (§5.2.1). Each chunk is a self-contained audio file with its own header,
/// checksum and write-status. Recording and real-time STT are decoupled — recording
/// failure does not affect transcription and vice versa.
/// </summary>
public class RecordingService : IRecordingService
{
    private readonly IAppDbContext _db;
    private readonly ILogger<RecordingService> _logger;

    /// <summary>
    /// PAUSED has no constant in <see cref="MeetingStatuses"/>; a literal string is
    /// used so the state can be persisted on the meeting and queried consistently.
    /// </summary>
    private const string Paused = "PAUSED";

    public RecordingService(
        IAppDbContext db,
        ILogger<RecordingService> logger)
    {
        _db = db;
        _logger = logger;
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Recording lifecycle
    // ══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Starts recording for a meeting. Transitions the meeting to RECORDING,
    /// creates the local recording directory and the first (WRITING) chunk.
    /// </summary>
    /// <param name="meetingId">The meeting to record.</param>
    /// <param name="request">Recording parameters (codec, sample rate, chunk duration).</param>
    /// <param name="userId">The user initiating recording (ownership is verified).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The in-memory recording session state.</returns>
    public async Task<RecordingSessionDto> StartAsync(
        Guid meetingId,
        StartRecordingRequest request,
        Guid userId,
        CancellationToken ct)
    {
        var meeting = await _db.Meetings
            .FirstOrDefaultAsync(m => m.Id == meetingId, ct);

        if (meeting == null)
        {
            throw new NotFoundException("Meeting", meetingId);
        }

        // Verify ownership.
        if (meeting.CreatedBy != userId)
        {
            throw new UnauthorizedException("User does not own this meeting");
        }

        // Only a CREATED meeting can start recording.
        if (meeting.Status != MeetingStatuses.Created)
        {
            throw new ValidationException(
                "status",
                $"Meeting must be in CREATED status to start recording (current: {meeting.Status}).");
        }

        var now = DateTime.UtcNow;
        meeting.Status = MeetingStatuses.Recording;
        meeting.StartedAt = now;
        meeting.UpdatedAt = now;

        // Create the local recording directory: ~/.knowledge-engine/recordings/{meetingId}/
        var recordingDir = GetRecordingDirectory(meetingId);
        Directory.CreateDirectory(recordingDir);

        // Create the first chunk (WRITING, sequence 0, timeline start 0).
        var chunk = new RecordingChunk
        {
            Id = Guid.NewGuid(),
            MeetingId = meetingId,
            SequenceNo = 0,
            StartMs = 0,
            EndMs = 0,
            LocalUri = GetChunkFilePath(meetingId, 0),
            Codec = string.IsNullOrWhiteSpace(request.Codec) ? "pcm_s16le" : request.Codec,
            SampleRate = request.SampleRate > 0 ? request.SampleRate : 16000,
            Channels = request.Channels > 0 ? request.Channels : 1,
            FileSize = 0,
            WriteStatus = ChunkWriteStatuses.Writing,
            CreatedAt = now
        };
        _db.RecordingChunks.Add(chunk);

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Recording started: meeting={MeetingId}, chunk={ChunkId}, seq={SequenceNo}, dir={Dir}",
            meetingId, chunk.Id, chunk.SequenceNo, recordingDir);

        return BuildSessionDto(meeting, new List<RecordingChunk> { chunk });
    }

    /// <summary>
    /// Pauses recording. The current in-progress (WRITING) chunk is finalized
    /// and the meeting enters the PAUSED state. The pause creates a timeline gap
    /// that is recorded on the next chunk.
    /// </summary>
    /// <param name="meetingId">The meeting to pause.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The current recording session state.</returns>
    public async Task<RecordingSessionDto> PauseAsync(Guid meetingId, CancellationToken ct)
    {
        var meeting = await _db.Meetings
            .FirstOrDefaultAsync(m => m.Id == meetingId, ct);

        if (meeting == null)
        {
            throw new NotFoundException("Meeting", meetingId);
        }

        if (meeting.Status != MeetingStatuses.Recording)
        {
            throw new ValidationException(
                "status",
                $"Meeting must be in RECORDING status to pause (current: {meeting.Status}).");
        }

        var now = DateTime.UtcNow;
        var chunks = await LoadChunksAsync(meetingId, ct);

        // Finalize the current WRITING chunk (if any).
        var writingChunk = chunks.LastOrDefault(c => c.WriteStatus == ChunkWriteStatuses.Writing);
        if (writingChunk != null)
        {
            FinalizeChunk(writingChunk, now);
        }

        meeting.Status = Paused;
        meeting.UpdatedAt = now;

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Recording paused: meeting={MeetingId}, finalizedChunk={ChunkId}",
            meetingId, writingChunk?.Id);

        return BuildSessionDto(meeting, chunks);
    }

    /// <summary>
    /// Resumes recording after a pause. A new chunk is started whose timeline
    /// position accounts for all previous chunk durations and gaps.
    /// </summary>
    /// <param name="meetingId">The meeting to resume.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The current recording session state.</returns>
    public async Task<RecordingSessionDto> ResumeAsync(Guid meetingId, CancellationToken ct)
    {
        var meeting = await _db.Meetings
            .FirstOrDefaultAsync(m => m.Id == meetingId, ct);

        if (meeting == null)
        {
            throw new NotFoundException("Meeting", meetingId);
        }

        if (meeting.Status != Paused)
        {
            throw new ValidationException(
                "status",
                $"Meeting must be in PAUSED status to resume (current: {meeting.Status}).");
        }

        var now = DateTime.UtcNow;
        var chunks = await LoadChunksAsync(meetingId, ct);
        var lastChunk = chunks.LastOrDefault();

        // Next sequence number.
        var nextSeq = (lastChunk?.SequenceNo ?? -1) + 1;

        // StartMs = sum of previous chunks' (EndMs - StartMs) + gaps.
        // Because each chunk's EndMs already incorporates all prior durations and
        // gaps, the new chunk's timeline position is lastChunk.EndMs + this pause gap.
        long startMs = lastChunk?.EndMs ?? 0;

        // The gap before this chunk = pause duration (time since the previous chunk
        // was finalized). A gap of 0 means continuous recording.
        long gapMs = 0;
        if (lastChunk?.FinalizedAt != null)
        {
            gapMs = (long)(now - lastChunk.FinalizedAt.Value).TotalMilliseconds;
            if (gapMs < 0)
            {
                gapMs = 0;
            }
        }

        // Inherit audio format from the previous chunk (or request defaults).
        var codec = !string.IsNullOrWhiteSpace(lastChunk?.Codec) ? lastChunk!.Codec : "pcm_s16le";
        var sampleRate = (lastChunk?.SampleRate ?? 0) > 0 ? lastChunk!.SampleRate : 16000;
        var channels = (lastChunk?.Channels ?? 0) > 0 ? lastChunk!.Channels : 1;

        var chunk = new RecordingChunk
        {
            Id = Guid.NewGuid(),
            MeetingId = meetingId,
            SequenceNo = nextSeq,
            StartMs = startMs + gapMs,
            EndMs = 0,
            LocalUri = GetChunkFilePath(meetingId, nextSeq),
            Codec = codec,
            SampleRate = sampleRate,
            Channels = channels,
            FileSize = 0,
            WriteStatus = ChunkWriteStatuses.Writing,
            TimelineGapBeforeMs = gapMs,
            CreatedAt = now
        };
        _db.RecordingChunks.Add(chunk);

        meeting.Status = MeetingStatuses.Recording;
        meeting.UpdatedAt = now;

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Recording resumed: meeting={MeetingId}, chunk={ChunkId}, seq={SequenceNo}, gap={GapMs}ms",
            meetingId, chunk.Id, nextSeq, gapMs);

        chunks.Add(chunk);
        return BuildSessionDto(meeting, chunks);
    }

    /// <summary>
    /// Stops recording and finalizes the meeting. Transitions the meeting to
    /// FINALIZING, finalizes any in-progress chunk (computing checksums), then
    /// transitions to COMPLETED.
    /// </summary>
    /// <param name="meetingId">The meeting to stop.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The final recording session state.</returns>
    public async Task<RecordingSessionDto> StopAsync(Guid meetingId, CancellationToken ct)
    {
        var meeting = await _db.Meetings
            .FirstOrDefaultAsync(m => m.Id == meetingId, ct);

        if (meeting == null)
        {
            throw new NotFoundException("Meeting", meetingId);
        }

        if (meeting.Status != MeetingStatuses.Recording && meeting.Status != Paused)
        {
            throw new ValidationException(
                "status",
                $"Meeting must be in RECORDING or PAUSED status to stop (current: {meeting.Status}).");
        }

        var now = DateTime.UtcNow;

        // Transition to FINALIZING first so a crash here is recoverable.
        meeting.Status = MeetingStatuses.Finalizing;
        meeting.UpdatedAt = now;
        await _db.SaveChangesAsync(ct);

        // Finalize any WRITING chunk and compute checksums.
        var chunks = await LoadChunksAsync(meetingId, ct);
        foreach (var chunk in chunks.Where(c => c.WriteStatus == ChunkWriteStatuses.Writing))
        {
            FinalizeChunk(chunk, now);
        }

        // Transition to COMPLETED.
        meeting.EndedAt = now;
        meeting.DurationMs = meeting.StartedAt.HasValue
            ? (long)(now - meeting.StartedAt.Value).TotalMilliseconds
            : 0;
        meeting.Status = MeetingStatuses.Completed;
        meeting.UpdatedAt = now;

        await _db.SaveChangesAsync(ct);

        var totalBytes = chunks.Sum(c => c.FileSize);
        _logger.LogInformation(
            "Recording stopped: meeting={MeetingId}, duration={DurationMs}ms, chunks={ChunkCount}, bytes={TotalBytes}",
            meetingId, meeting.DurationMs, chunks.Count, totalBytes);

        return BuildSessionDto(meeting, chunks);
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Status & chunk queries
    // ══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Gets the current recording status for a meeting, reconstructed from the
    /// persisted meeting and chunk records.
    /// </summary>
    /// <param name="meetingId">The meeting id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The recording session state, or null if the meeting does not exist.</returns>
    public async Task<RecordingSessionDto?> GetStatusAsync(Guid meetingId, CancellationToken ct)
    {
        var meeting = await _db.Meetings
            .FirstOrDefaultAsync(m => m.Id == meetingId, ct);

        if (meeting == null)
        {
            return null;
        }

        var chunks = await LoadChunksAsync(meetingId, ct);
        return BuildSessionDto(meeting, chunks);
    }

    /// <summary>
    /// Lists all recording chunks for a meeting, ordered by sequence number.
    /// </summary>
    /// <param name="meetingId">The meeting id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The ordered list of recording chunk DTOs.</returns>
    public async Task<List<RecordingChunkDto>> GetChunksAsync(Guid meetingId, CancellationToken ct)
    {
        var chunks = await LoadChunksAsync(meetingId, ct);
        return chunks.Select(ToChunkDto).ToList();
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Crash recovery
    // ══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Scans for meetings left in RECORDING/PAUSED/FINALIZING status after a
    /// crash or abnormal exit. Validates completed chunks, attempts to repair the
    /// last in-progress chunk, rebuilds the chunk index, and marks unrecoverable
    /// gaps.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A recovery result for each meeting that was repaired.</returns>
    public async Task<List<RecoveryResultDto>> RecoverIncompleteMeetingsAsync(CancellationToken ct)
    {
        var incompleteStatuses = new[]
        {
            MeetingStatuses.Recording,
            Paused,
            MeetingStatuses.Finalizing
        };

        var meetings = await _db.Meetings
            .Where(m => incompleteStatuses.Contains(m.Status))
            .ToListAsync(ct);

        var results = new List<RecoveryResultDto>();

        foreach (var meeting in meetings)
        {
            ct.ThrowIfCancellationRequested();
            var result = await RecoverMeetingAsync(meeting, ct);
            results.Add(result);
        }

        if (results.Count > 0)
        {
            _logger.LogInformation(
                "Crash recovery complete: recovered {Count} incomplete meeting(s).",
                results.Count);
        }
        else
        {
            _logger.LogInformation("Crash recovery scan: no incomplete meetings found.");
        }

        return results;
    }

    /// <summary>
    /// Validates and repairs a single incomplete meeting: validates completed
    /// chunks against their on-disk files, finalizes usable in-progress chunks,
    /// marks missing/corrupted chunks, records timeline gaps, and transitions
    /// the meeting to COMPLETED (or CREATED when no chunks exist).
    /// </summary>
    private async Task<RecoveryResultDto> RecoverMeetingAsync(Meeting meeting, CancellationToken ct)
    {
        var previousStatus = meeting.Status;
        var chunks = await LoadChunksAsync(meeting.Id, ct);
        var now = DateTime.UtcNow;

        int valid = 0, corrupted = 0, missing = 0;
        var gaps = new List<string>();

        foreach (var chunk in chunks)
        {
            ct.ThrowIfCancellationRequested();
            var fileExists = !string.IsNullOrEmpty(chunk.LocalUri) && File.Exists(chunk.LocalUri);

            switch (chunk.WriteStatus)
            {
                case ChunkWriteStatuses.Complete:
                case ChunkWriteStatuses.Recovered:
                    ValidateCompleteChunk(chunk, fileExists, ref valid, ref corrupted, ref missing, gaps);
                    break;

                case ChunkWriteStatuses.Writing:
                    RecoverWritingChunk(chunk, fileExists, now, ref valid, ref corrupted, ref missing, gaps);
                    break;

                case ChunkWriteStatuses.Corrupted:
                    chunk.RecoveryStatus = ChunkRecoveryStatuses.Unrecoverable;
                    corrupted++;
                    gaps.Add($"Chunk {chunk.SequenceNo}: previously CORRUPTED.");
                    break;

                case ChunkWriteStatuses.Missing:
                    chunk.RecoveryStatus = ChunkRecoveryStatuses.Unrecoverable;
                    missing++;
                    gaps.Add($"Chunk {chunk.SequenceNo}: previously MISSING.");
                    break;

                default:
                    chunk.RecoveryStatus = ChunkRecoveryStatuses.Unrecoverable;
                    corrupted++;
                    gaps.Add($"Chunk {chunk.SequenceNo}: unknown write status '{chunk.WriteStatus}'.");
                    break;
            }

            // Record any timeline gap that preceded this chunk.
            if (chunk.TimelineGapBeforeMs > 0)
            {
                gaps.Add($"Chunk {chunk.SequenceNo}: timeline gap of {chunk.TimelineGapBeforeMs}ms before chunk.");
            }
        }

        var recoveredDurationMs = chunks
            .Where(c => c.WriteStatus == ChunkWriteStatuses.Complete
                     || c.WriteStatus == ChunkWriteStatuses.Recovered)
            .Sum(c => Math.Max(0, c.EndMs - c.StartMs));

        // A meeting with no chunks reverts to CREATED; otherwise it is finalized.
        if (chunks.Count == 0)
        {
            meeting.Status = MeetingStatuses.Created;
        }
        else
        {
            meeting.Status = MeetingStatuses.Completed;
            meeting.EndedAt = now;
            meeting.DurationMs = recoveredDurationMs;
        }
        meeting.UpdatedAt = now;

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Recovered meeting {MeetingId}: {Previous} -> {New}, valid={Valid}, corrupted={Corrupted}, missing={Missing}, recoveredMs={RecoveredMs}",
            meeting.Id, previousStatus, meeting.Status, valid, corrupted, missing, recoveredDurationMs);

        return new RecoveryResultDto
        {
            MeetingId = meeting.Id,
            MeetingTitle = meeting.Title,
            PreviousStatus = previousStatus,
            NewStatus = meeting.Status,
            ValidChunks = valid,
            CorruptedChunks = corrupted,
            MissingChunks = missing,
            RecoveredDurationMs = recoveredDurationMs,
            Gaps = gaps
        };
    }

    /// <summary>
    /// Validates a previously COMPLETE chunk by checking its on-disk file and
    /// recomputing the checksum. Mismatches or missing files downgrade the chunk.
    /// </summary>
    private void ValidateCompleteChunk(
        RecordingChunk chunk,
        bool fileExists,
        ref int valid,
        ref int corrupted,
        ref int missing,
        List<string> gaps)
    {
        if (!fileExists)
        {
            chunk.WriteStatus = ChunkWriteStatuses.Missing;
            chunk.RecoveryStatus = ChunkRecoveryStatuses.Unrecoverable;
            missing++;
            gaps.Add($"Chunk {chunk.SequenceNo}: file missing ({chunk.LocalUri}).");
            return;
        }

        try
        {
            chunk.FileSize = new FileInfo(chunk.LocalUri).Length;
            var computed = ComputeChecksum(chunk.LocalUri);

            if (!string.IsNullOrEmpty(chunk.Checksum))
            {
                if (string.Equals(computed, chunk.Checksum, StringComparison.OrdinalIgnoreCase))
                {
                    chunk.RecoveryStatus = ChunkRecoveryStatuses.Validated;
                    valid++;
                }
                else
                {
                    chunk.WriteStatus = ChunkWriteStatuses.Corrupted;
                    chunk.RecoveryStatus = ChunkRecoveryStatuses.Unrecoverable;
                    corrupted++;
                    gaps.Add($"Chunk {chunk.SequenceNo}: checksum mismatch (file corrupted).");
                }
            }
            else
            {
                // No prior checksum — accept the file and record one.
                chunk.Checksum = computed;
                chunk.RecoveryStatus = ChunkRecoveryStatuses.Validated;
                valid++;
            }
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex,
                "Failed to validate chunk {ChunkId} file {Path}.",
                chunk.Id, chunk.LocalUri);
            chunk.WriteStatus = ChunkWriteStatuses.Corrupted;
            chunk.RecoveryStatus = ChunkRecoveryStatuses.Unrecoverable;
            corrupted++;
            gaps.Add($"Chunk {chunk.SequenceNo}: unreadable file ({ex.Message}).");
        }
    }

    /// <summary>
    /// Attempts to recover a WRITING (in-progress) chunk. If the partial file on
    /// disk has content it is treated as usable and finalized; otherwise it is
    /// marked CORRUPTED or MISSING.
    /// </summary>
    private void RecoverWritingChunk(
        RecordingChunk chunk,
        bool fileExists,
        DateTime now,
        ref int valid,
        ref int corrupted,
        ref int missing,
        List<string> gaps)
    {
        if (!fileExists)
        {
            chunk.WriteStatus = ChunkWriteStatuses.Missing;
            chunk.RecoveryStatus = ChunkRecoveryStatuses.Unrecoverable;
            missing++;
            gaps.Add($"Chunk {chunk.SequenceNo}: file missing ({chunk.LocalUri}).");
            return;
        }

        try
        {
            var length = new FileInfo(chunk.LocalUri).Length;
            if (length <= 0)
            {
                chunk.WriteStatus = ChunkWriteStatuses.Corrupted;
                chunk.RecoveryStatus = ChunkRecoveryStatuses.Unrecoverable;
                corrupted++;
                gaps.Add($"Chunk {chunk.SequenceNo}: empty file (no usable content).");
                return;
            }

            // The partial file is usable — finalize it as a recovered chunk.
            chunk.FileSize = length;
            chunk.Checksum = ComputeChecksum(chunk.LocalUri);
            chunk.WriteStatus = ChunkWriteStatuses.Complete;
            chunk.RecoveryStatus = ChunkRecoveryStatuses.Repaired;
            chunk.FinalizedAt = now;

            if (chunk.EndMs <= chunk.StartMs)
            {
                chunk.EndMs = chunk.StartMs + Math.Max(0, (long)(now - chunk.CreatedAt).TotalMilliseconds);
            }

            valid++;
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex,
                "Failed to recover WRITING chunk {ChunkId} file {Path}.",
                chunk.Id, chunk.LocalUri);
            chunk.WriteStatus = ChunkWriteStatuses.Corrupted;
            chunk.RecoveryStatus = ChunkRecoveryStatuses.Unrecoverable;
            corrupted++;
            gaps.Add($"Chunk {chunk.SequenceNo}: unreadable file ({ex.Message}).");
        }
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Private helpers
    // ══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Loads all recording chunks for a meeting, ordered by sequence number.
    /// </summary>
    private async Task<List<RecordingChunk>> LoadChunksAsync(Guid meetingId, CancellationToken ct)
    {
        return await _db.RecordingChunks
            .Where(c => c.MeetingId == meetingId)
            .OrderBy(c => c.SequenceNo)
            .ToListAsync(ct);
    }

    /// <summary>
    /// Finalizes a WRITING chunk: sets the end time, write status to COMPLETE and
    /// the finalization timestamp. If the on-disk file exists, its size and
    /// SHA-256 checksum are recorded.
    /// </summary>
    private void FinalizeChunk(RecordingChunk chunk, DateTime now)
    {
        var duration = Math.Max(0, (long)(now - chunk.CreatedAt).TotalMilliseconds);
        chunk.EndMs = chunk.StartMs + duration;
        chunk.FinalizedAt = now;
        chunk.WriteStatus = ChunkWriteStatuses.Complete;

        if (string.IsNullOrEmpty(chunk.LocalUri) || !File.Exists(chunk.LocalUri))
        {
            // The capture layer has not written the file yet; leave size/checksum empty.
            return;
        }

        try
        {
            chunk.FileSize = new FileInfo(chunk.LocalUri).Length;
            chunk.Checksum = ComputeChecksum(chunk.LocalUri);
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex,
                "Failed to read chunk file {Path} during finalization.",
                chunk.LocalUri);
        }
    }

    /// <summary>
    /// Computes the SHA-256 checksum of a file and returns it as a lowercase hex string.
    /// </summary>
    private static string ComputeChecksum(string path)
    {
        var hash = SHA256.HashData(File.ReadAllBytes(path));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// Returns the local recording directory for a meeting:
    /// <c>~/.knowledge-engine/recordings/{meetingId}/</c>
    /// </summary>
    private static string GetRecordingDirectory(Guid meetingId)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".knowledge-engine", "recordings", meetingId.ToString());
    }

    /// <summary>
    /// Returns the local file path for a chunk within a meeting's recording directory.
    /// </summary>
    private static string GetChunkFilePath(Guid meetingId, int sequenceNo)
    {
        return Path.Combine(GetRecordingDirectory(meetingId), $"chunk_{sequenceNo:000}.wav");
    }

    // ══════════════════════════════════════════════════════════════════════
    //  DTO mapping
    // ══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Builds the in-memory recording session state from the persisted meeting
    /// and chunk records. The active WRITING chunk's elapsed time is estimated
    /// from the wall clock so callers see a live progress figure.
    /// </summary>
    private static RecordingSessionDto BuildSessionDto(Meeting meeting, List<RecordingChunk> chunks)
    {
        var now = DateTime.UtcNow;
        var ordered = chunks.OrderBy(c => c.SequenceNo).ToList();

        var totalBytes = ordered.Sum(c => c.FileSize);

        // Elapsed audio time = sum of finalized chunk durations, plus a live
        // estimate for any currently-WRITING chunk.
        long elapsed = 0;
        foreach (var c in ordered)
        {
            if (c.WriteStatus == ChunkWriteStatuses.Writing && c.EndMs <= c.StartMs)
            {
                elapsed += Math.Max(0, (long)(now - c.CreatedAt).TotalMilliseconds);
            }
            else
            {
                elapsed += Math.Max(0, c.EndMs - c.StartMs);
            }
        }

        var first = ordered.FirstOrDefault();
        var current = ordered.LastOrDefault(c => c.WriteStatus == ChunkWriteStatuses.Writing)
                   ?? ordered.LastOrDefault();

        return new RecordingSessionDto
        {
            MeetingId = meeting.Id,
            Status = meeting.Status,
            StartedAt = meeting.StartedAt,
            ElapsedMs = elapsed,
            ChunkCount = ordered.Count,
            CurrentChunkSequence = current?.SequenceNo ?? 0,
            Codec = first?.Codec ?? "pcm_s16le",
            SampleRate = (first?.SampleRate ?? 0) > 0 ? first!.SampleRate : 16000,
            Channels = (first?.Channels ?? 0) > 0 ? first!.Channels : 1,
            TotalBytesWritten = totalBytes,
            Chunks = ordered.Select(ToChunkDto).ToList()
        };
    }

    /// <summary>
    /// Maps a <see cref="RecordingChunk"/> entity to a <see cref="RecordingChunkDto"/>.
    /// </summary>
    private static RecordingChunkDto ToChunkDto(RecordingChunk c)
    {
        return new RecordingChunkDto
        {
            Id = c.Id,
            MeetingId = c.MeetingId,
            SequenceNo = c.SequenceNo,
            StartMs = c.StartMs,
            EndMs = c.EndMs,
            Codec = c.Codec,
            SampleRate = c.SampleRate,
            Channels = c.Channels,
            FileSize = c.FileSize,
            Checksum = c.Checksum,
            WriteStatus = c.WriteStatus,
            RecoveryStatus = c.RecoveryStatus,
            TimelineGapBeforeMs = c.TimelineGapBeforeMs,
            CreatedAt = c.CreatedAt,
            FinalizedAt = c.FinalizedAt
        };
    }
}
