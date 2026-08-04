using KnowledgeEngine.Domain.Enums;

namespace KnowledgeEngine.Application.DTOs;

// ── Meeting DTOs ──

public class MeetingDto
{
    public Guid Id { get; set; }
    public Guid? WorkspaceId { get; set; }
    public Guid? TopicId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Language { get; set; } = "zh-CN";
    public string Status { get; set; } = string.Empty;
    public DateTime? StartedAt { get; set; }
    public DateTime? EndedAt { get; set; }
    public long DurationMs { get; set; }
    public Guid CreatedBy { get; set; }
    public string ProcessingPreset { get; set; } = string.Empty;
    public string DataClassification { get; set; } = string.Empty;
    public bool AllowAudioUpload { get; set; }
    public bool AllowTextUpload { get; set; }
    public Guid? OfficialTranscriptVersionId { get; set; }
    public Guid? OfficialMinutesVersionId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public List<MeetingSpeakerDto>? Speakers { get; set; }
    public List<ActionItemDto>? ActionItems { get; set; }
}

public class CreateMeetingRequest
{
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Language { get; set; } = "zh-CN";
    public Guid? TopicId { get; set; }
    public Guid? WorkspaceId { get; set; }
    public string ProcessingPreset { get; set; } = "LOCAL_FIRST";
    public string DataClassification { get; set; } = "INTERNAL";
    public bool AllowAudioUpload { get; set; } = false;
    public bool AllowTextUpload { get; set; } = true;
    public List<string>? Hotwords { get; set; }
    public int? SpeakerCountHint { get; set; }
}

public class UpdateMeetingRequest
{
    public string? Title { get; set; }
    public string? Description { get; set; }
    public string? Language { get; set; }
    public Guid? TopicId { get; set; }
    public string? ProcessingPreset { get; set; }
    public string? DataClassification { get; set; }
    public bool? AllowAudioUpload { get; set; }
    public bool? AllowTextUpload { get; set; }
}

// ── MeetingSpeaker DTOs ──

public class MeetingSpeakerDto
{
    public Guid Id { get; set; }
    public Guid MeetingId { get; set; }
    public string SpeakerKey { get; set; } = string.Empty;
    public string GlobalSpeakerId { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public Guid? ParticipantId { get; set; }
    public string IdentityStatus { get; set; } = string.Empty;
    public decimal Confidence { get; set; }
}

public class UpdateSpeakerRequest
{
    public string? DisplayName { get; set; }
    public string? IdentityStatus { get; set; }
    /// <summary>If true, apply the display name to all segments with the same speaker_key.</summary>
    public bool ApplyToAll { get; set; } = true;
}

public class MergeSpeakersRequest
{
    public List<Guid> SpeakerIds { get; set; } = new();
    public string? TargetDisplayName { get; set; }
}

public class SplitSpeakerRequest
{
    public Guid SpeakerId { get; set; }
    public List<string> SegmentUuids { get; set; } = new();
}

// ── MeetingMinutes DTOs ──

public class MeetingMinutesDto
{
    public Guid Id { get; set; }
    public Guid MeetingId { get; set; }
    public int VersionNo { get; set; }
    public Guid? TranscriptVersionId { get; set; }
    public Guid? TemplateId { get; set; }
    public string? Summary { get; set; }
    public string ContentJson { get; set; } = "{}";
    public string Provider { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class GenerateMinutesRequest
{
    public Guid? TranscriptVersionId { get; set; }
    public Guid? TemplateId { get; set; }
    /// <summary>Privacy masking mode for external LLM calls.</summary>
    public string? MaskingMode { get; set; }
}

public class UpdateMinutesRequest
{
    public string? Summary { get; set; }
    public string? ContentJson { get; set; }
}

// ── ActionItem DTOs ──

public class ActionItemDto
{
    public Guid Id { get; set; }
    public Guid MeetingId { get; set; }
    public Guid? MinutesVersionId { get; set; }
    public string TaskText { get; set; } = string.Empty;
    public string? OwnerText { get; set; }
    public Guid? OwnerUserId { get; set; }
    public DateTime? DueDate { get; set; }
    public string Priority { get; set; } = string.Empty;
    public decimal Confidence { get; set; }
    public string ConfirmationStatus { get; set; } = string.Empty;
    public Guid? TaskId { get; set; }
    public List<string>? SourceSegmentIds { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class ConfirmActionItemRequest
{
    public string? TaskText { get; set; }
    public string? OwnerText { get; set; }
    public Guid? OwnerUserId { get; set; }
    public DateTime? DueDate { get; set; }
    public string? Priority { get; set; }
    public bool CreateTask { get; set; } = false;
}

public class BatchConfirmActionItemsRequest
{
    public List<Guid> ActionItemIds { get; set; } = new();
    public bool CreateTasks { get; set; } = false;
}

// ── Recording DTOs ──

public class RecordingChunkDto
{
    public Guid Id { get; set; }
    public Guid MeetingId { get; set; }
    public int SequenceNo { get; set; }
    public long StartMs { get; set; }
    public long EndMs { get; set; }
    public string Codec { get; set; } = string.Empty;
    public int SampleRate { get; set; }
    public int Channels { get; set; }
    public long FileSize { get; set; }
    public string? Checksum { get; set; }
    public string WriteStatus { get; set; } = string.Empty;
    public string? RecoveryStatus { get; set; }
    public long TimelineGapBeforeMs { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? FinalizedAt { get; set; }
}

public class StartRecordingRequest
{
    public int ChunkDurationSeconds { get; set; } = 180;
    public string Codec { get; set; } = "pcm_s16le";
    public int SampleRate { get; set; } = 16000;
    public int Channels { get; set; } = 1;
}

// ── Meeting Asset DTOs ──

public class MeetingAssetDto
{
    public Guid Id { get; set; }
    public Guid MeetingId { get; set; }
    public string AssetType { get; set; } = string.Empty;
    public string StorageMode { get; set; } = string.Empty;
    public string Uri { get; set; } = string.Empty;
    public string MimeType { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public long DurationMs { get; set; }
    public string? Checksum { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class UploadAssetRequest
{
    public string AssetType { get; set; } = "AUDIO";
    public string MimeType { get; set; } = "audio/wav";
}

// ── Meeting Transcript DTOs ──

public class TranscriptVersionDto
{
    public Guid Id { get; set; }
    public Guid MeetingId { get; set; }
    public int VersionNo { get; set; }
    public string VersionType { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string? Language { get; set; }
    public Guid? SourceAssetId { get; set; }
    public Guid? ParentVersionId { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<TranscriptSegmentDto>? Segments { get; set; }
}

public class TranscriptSegmentDto
{
    public Guid Id { get; set; }
    public string SegmentUuid { get; set; } = string.Empty;
    public long StartMs { get; set; }
    public long EndMs { get; set; }
    public string? SpeakerKey { get; set; }
    public string? SpeakerDisplayName { get; set; }
    public string Text { get; set; } = string.Empty;
    public decimal Confidence { get; set; }
    public string Version { get; set; } = string.Empty;
    public int SegmentIndex { get; set; }
    public bool ManualEdited { get; set; }
}

public class CreateTranscriptionRequest
{
    public Guid? SourceAssetId { get; set; }
    public string? Language { get; set; }
    public bool EnableVad { get; set; } = true;
    public bool EnableSpeakerDiarization { get; set; } = true;
    public bool EnablePunctuation { get; set; } = true;
    public List<string>? Hotwords { get; set; }
    public int? SpeakerCountHint { get; set; }
}

public class UpdateSegmentRequest
{
    public string? Text { get; set; }
    public string? SpeakerKey { get; set; }
}

// ── Meeting Routing Context ──

public class MeetingRoutingContext
{
    public Guid? MeetingId { get; set; }
    public string ProcessingPreset { get; set; } = "LOCAL_FIRST";
    public string DataClassification { get; set; } = "INTERNAL";
    public bool AllowAudioUpload { get; set; } = false;
    public bool AllowTextUpload { get; set; } = true;
    public string? Language { get; set; }
    public bool EnableVad { get; set; } = true;
    public bool EnableSpeakerDiarization { get; set; } = true;
    public bool EnablePunctuation { get; set; } = true;
    public List<string>? Hotwords { get; set; }
    public int? SpeakerCountHint { get; set; }
    public long FileSizeBytes { get; set; }
    public long DurationMs { get; set; }
    public string MimeType { get; set; } = "audio/wav";
    public ExecutionMode? PreferredExecutionMode { get; set; }
    public CredentialMode? PreferredCredentialMode { get; set; }
    public string? PreferredProviderId { get; set; }
    public string? PreferredModelId { get; set; }
}

// ── Meeting Operations (Capability Contract) ──

public static class MeetingOperations
{
    public const string MeetingRecord = "MEETING_RECORD";
    public const string AudioTranscribe = "AUDIO_TRANSCRIBE";
    public const string SpeakerDiarize = "SPEAKER_DIARIZE";
    public const string TranscriptPolish = "TRANSCRIPT_POLISH";
    public const string MeetingSummarize = "MEETING_SUMMARIZE";
    public const string MeetingMinutesGenerate = "MEETING_MINUTES_GENERATE";
    public const string ActionItemExtract = "ACTION_ITEM_EXTRACT";
    public const string MeetingReprocess = "MEETING_REPROCESS";
}

public static class MeetingToolConstants
{
    public const string ToolId = "memorix.meeting.transcription";
    public const string ToolType = "OFFICIAL_CONTENT_TOOL";
    public const string DisplayName = "会议记录";

    public static readonly string[] Operations =
    {
        MeetingOperations.MeetingRecord,
        MeetingOperations.AudioTranscribe,
        MeetingOperations.SpeakerDiarize,
        MeetingOperations.TranscriptPolish,
        MeetingOperations.MeetingSummarize,
        MeetingOperations.MeetingMinutesGenerate,
        MeetingOperations.ActionItemExtract,
        MeetingOperations.MeetingReprocess,
    };

    public static readonly string[] RuntimeSupport =
    {
        "LOCAL_DEVICE",
        "LOCAL_LAN_NODE",
        "USER_BYOK",
        "TENANT_BYOK",
        "MEMORIX_CLOUD",
    };
}

// ── Meeting Publishing DTOs ──

public class PublishMinutesRequest
{
    public Guid MinutesId { get; set; }
}

public class PublishTranscriptRequest
{
    public Guid TranscriptId { get; set; }
}

public class PublishAllRequest
{
    // No additional fields needed - uses meetingId from URL
}

// ── Meeting Processing Pipeline DTOs ──

public class CreateProcessingPipelineRequest
{
    public Guid AudioAssetId { get; set; }
}
