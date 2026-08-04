using KnowledgeEngine.Application.DTOs;

namespace KnowledgeEngine.Application.Interfaces;

/// <summary>
/// Meeting lifecycle management service.
/// Handles meeting CRUD, speaker management, minutes generation orchestration,
/// asset upload, transcription triggering, and action item confirmation flow.
/// </summary>
public interface IMeetingService
{
    // ── Meeting CRUD ──
    Task<MeetingDto> CreateAsync(CreateMeetingRequest request, Guid userId, CancellationToken ct);
    Task<MeetingDto?> GetAsync(Guid meetingId, CancellationToken ct);
    Task<MeetingDto?> UpdateAsync(Guid meetingId, UpdateMeetingRequest request, CancellationToken ct);
    Task<bool> FinishAsync(Guid meetingId, CancellationToken ct);
    Task<bool> DeleteAsync(Guid meetingId, CancellationToken ct);
    Task<List<MeetingDto>> ListAsync(Guid? workspaceId, int limit, int offset, CancellationToken ct);

    // ── Speaker management ──
    Task<List<MeetingSpeakerDto>> GetSpeakersAsync(Guid meetingId, CancellationToken ct);
    Task<MeetingSpeakerDto?> UpdateSpeakerAsync(Guid speakerId, UpdateSpeakerRequest request, CancellationToken ct);
    Task<bool> MergeSpeakersAsync(Guid meetingId, MergeSpeakersRequest request, CancellationToken ct);
    Task<bool> SplitSpeakerAsync(Guid meetingId, SplitSpeakerRequest request, CancellationToken ct);

    // ── Asset management ──
    Task<MeetingAssetDto> UploadAssetAsync(Guid meetingId, Stream stream, string fileName, string mimeType, long fileSize, Guid userId, CancellationToken ct);
    Task<List<MeetingAssetDto>> GetAssetsAsync(Guid meetingId, CancellationToken ct);

    // ── Transcription management ──
    Task<TranscriptVersionDto> TriggerTranscriptionAsync(Guid meetingId, CreateTranscriptionRequest request, Guid userId, CancellationToken ct);
    Task<List<TranscriptVersionDto>> GetTranscriptsAsync(Guid meetingId, CancellationToken ct);
    Task<TranscriptVersionDto?> GetTranscriptAsync(Guid transcriptId, CancellationToken ct);
    Task<TranscriptSegmentDto?> UpdateSegmentAsync(Guid segmentId, UpdateSegmentRequest request, CancellationToken ct);
    Task<bool> SetOfficialTranscriptAsync(Guid meetingId, Guid transcriptId, CancellationToken ct);
    Task<bool> ReprocessAsync(Guid meetingId, CancellationToken ct);

    // ── Minutes management ──
    Task<List<MeetingMinutesDto>> GetMinutesAsync(Guid meetingId, CancellationToken ct);
    Task<MeetingMinutesDto?> GenerateMinutesAsync(Guid meetingId, GenerateMinutesRequest request, Guid userId, CancellationToken ct);
    Task<MeetingMinutesDto?> UpdateMinutesAsync(Guid minutesId, UpdateMinutesRequest request, CancellationToken ct);
    Task<MeetingMinutesDto?> SetOfficialMinutesAsync(Guid meetingId, Guid minutesId, CancellationToken ct);

    // ── Action items ──
    Task<List<ActionItemDto>> GetActionItemsAsync(Guid meetingId, CancellationToken ct);
    Task<ActionItemDto?> ConfirmActionItemAsync(Guid actionItemId, ConfirmActionItemRequest request, CancellationToken ct);
    Task<List<ActionItemDto>> BatchConfirmActionItemsAsync(BatchConfirmActionItemsRequest request, CancellationToken ct);
    Task<ActionItemDto?> CreateTaskFromActionItemAsync(Guid actionItemId, CancellationToken ct);
}
