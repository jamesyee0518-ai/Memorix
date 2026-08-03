namespace KnowledgeEngine.Domain.Entities;

/// <summary>
/// Represents a single asynchronous processing task within a meeting's processing pipeline.
/// Each task corresponds to one step from the §14.1 task breakdown (AUDIO_NORMALIZE,
/// BATCH_TRANSCRIBE, SPEAKER_DIARIZE, MEETING_SUMMARIZE, etc.) and tracks its own
/// state machine, retry count, and idempotency key.
/// </summary>
public class MeetingProcessingTask
{
    public Guid Id { get; set; }

    /// <summary>The meeting this task belongs to.</summary>
    public Guid MeetingId { get; set; }

    /// <summary>The audio asset being processed (if applicable).</summary>
    public Guid? AudioAssetId { get; set; }

    /// <summary>The transcription job associated with this task (if applicable).</summary>
    public Guid? TranscriptionJobId { get; set; }

    /// <summary>
    /// Task type from §14.1: AUDIO_NORMALIZE, VOICE_ACTIVITY_DETECTION,
    /// STREAMING_TRANSCRIBE, BATCH_TRANSCRIBE, PUNCTUATION_RESTORE,
    /// SPEAKER_DIARIZE, TRANSCRIPT_ALIGN, TEXT_NORMALIZE,
    /// MEETING_SUMMARIZE, ACTION_ITEM_EXTRACT, KNOWLEDGE_PUBLISH.
    /// </summary>
    public string TaskType { get; set; } = string.Empty;

    /// <summary>
    /// Task status from §14.2: PENDING, QUEUED, RUNNING,
    /// WAITING_USER_CONFIRMATION, SUCCEEDED, FAILED_RETRYABLE,
    /// FAILED_FINAL, CANCELED.
    /// </summary>
    public string Status { get; set; } = MeetingProcessingTaskStatuses.Pending;

    /// <summary>
    /// Stable idempotency key derived from meetingId + sourceFile + taskType + config.
    /// Prevents duplicate task creation on retries.
    /// </summary>
    public string IdempotencyKey { get; set; } = string.Empty;

    /// <summary>Execution mode: LOCAL_DEVICE, LOCAL_LAN_NODE, MEMORIX_CLOUD, THIRD_PARTY_CLOUD.</summary>
    public string ExecutionMode { get; set; } = "LOCAL_DEVICE";

    /// <summary>Credential mode: NO_CREDENTIAL, USER_BYOK, TENANT_BYOK, PLATFORM_MANAGED.</summary>
    public string CredentialMode { get; set; } = "NO_CREDENTIAL";

    /// <summary>Provider ID used for this task (e.g., funasr-local, whisper-local).</summary>
    public string? ProviderId { get; set; }

    /// <summary>Model ID used for this task.</summary>
    public string? ModelId { get; set; }

    /// <summary>Dependencies — comma-separated list of task types this task depends on.</summary>
    public string? DependsOn { get; set; }

    /// <summary>Number of retry attempts for this task.</summary>
    public int RetryCount { get; set; }

    /// <summary>Maximum retry attempts before marking as FAILED_FINAL.</summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>Error message from the last failure (if any).</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>JSON-serialized task parameters (language, hotwords, etc.).</summary>
    public string? Parameters { get; set; }

    /// <summary>JSON-serialized task result data (output paths, segment counts, etc.).</summary>
    public string? ResultData { get; set; }

    /// <summary>Estimated cost for this task.</summary>
    public decimal? EstimatedCost { get; set; }

    public Guid CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>Task type constants from §14.1.</summary>
public static class MeetingProcessingTaskTypes
{
    public const string AudioNormalize = "AUDIO_NORMALIZE";
    public const string VoiceActivityDetection = "VOICE_ACTIVITY_DETECTION";
    public const string StreamingTranscribe = "STREAMING_TRANSCRIBE";
    public const string BatchTranscribe = "BATCH_TRANSCRIBE";
    public const string PunctuationRestore = "PUNCTUATION_RESTORE";
    public const string SpeakerDiarize = "SPEAKER_DIARIZE";
    public const string TranscriptAlign = "TRANSCRIPT_ALIGN";
    public const string TextNormalize = "TEXT_NORMALIZE";
    public const string MeetingSummarize = "MEETING_SUMMARIZE";
    public const string ActionItemExtract = "ACTION_ITEM_EXTRACT";
    public const string KnowledgePublish = "KNOWLEDGE_PUBLISH";
}

/// <summary>Task status constants from §14.2.</summary>
public static class MeetingProcessingTaskStatuses
{
    public const string Pending = "PENDING";
    public const string Queued = "QUEUED";
    public const string Running = "RUNNING";
    public const string WaitingUserConfirmation = "WAITING_USER_CONFIRMATION";
    public const string Succeeded = "SUCCEEDED";
    public const string FailedRetryable = "FAILED_RETRYABLE";
    public const string FailedFinal = "FAILED_FINAL";
    public const string Canceled = "CANCELED";
}
