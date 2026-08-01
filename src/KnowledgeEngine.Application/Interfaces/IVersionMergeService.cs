using KnowledgeEngine.Domain.Entities;

namespace KnowledgeEngine.Application.Interfaces;

/// <summary>
/// Three-way merge service for transcription segment versions.
/// <para>
/// When the server re-transcribes an audio segment (producing a
/// <c>SERVER_RETRANSCRIBED</c> version) after a user has already manually
/// edited the text (<c>USER_EDITED</c> version), the two change sets must be
/// reconciled against the original <c>RAW_MODEL</c> baseline. This service
/// performs that three-way merge and produces a <c>MERGED</c> version.
/// </para>
/// <para>
/// Merge policy:
/// <list type="bullet">
///   <item>If the user edited a region, the user's edit is preserved.</item>
///   <item>If only the server changed a region, the server's text is taken.</item>
///   <item>If both changed a region (conflict), the user's edit wins.</item>
///   <item>If the server re-segmented the audio (different boundaries / substantially different text), the user's full edit is preserved.</item>
///   <item>If no user edit exists, the server version is used verbatim.</item>
/// </list>
/// </para>
/// </summary>
public interface IVersionMergeService
{
    /// <summary>
    /// Performs a three-way merge for a single segment within a transcription job,
    /// creating a <c>MERGED</c> version record. Idempotent: if a <c>MERGED</c>
    /// version already exists for the segment it is returned without re-computation.
    /// </summary>
    /// <param name="transcriptionJobId">The transcription job scope.</param>
    /// <param name="segmentUuid">The stable segment UUID to merge.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The merged (or pre-existing merged) version record.</returns>
    Task<TranscriptionVersion> MergeAsync(Guid transcriptionJobId, string segmentUuid, CancellationToken ct);

    /// <summary>
    /// Returns the full version history for a segment, ordered from oldest to newest.
    /// </summary>
    /// <param name="segmentUuid">The stable segment UUID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An ordered list of version records.</returns>
    Task<List<TranscriptionVersion>> GetVersionHistoryAsync(string segmentUuid, CancellationToken ct);

    /// <summary>
    /// Returns the most recent <c>USER_EDITED</c> version for a segment, or null
    /// if the user has not edited it.
    /// </summary>
    Task<TranscriptionVersion?> GetUserEditedVersionAsync(string segmentUuid, CancellationToken ct);

    /// <summary>
    /// Returns the most recent <c>SERVER_RETRANSCRIBED</c> version for a segment,
    /// or null if the segment has not been re-transcribed.
    /// </summary>
    Task<TranscriptionVersion?> GetServerRetranscribedVersionAsync(string segmentUuid, CancellationToken ct);
}
