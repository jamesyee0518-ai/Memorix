namespace KnowledgeEngine.Domain.Entities;

/// <summary>
/// A meeting record that ties together audio assets, transcriptions, speakers,
/// minutes and action items within the Memorix knowledge engine.
/// </summary>
public class Meeting
{
    public Guid Id { get; set; }

    public Guid? WorkspaceId { get; set; }

    /// <summary>Associated topic for cross-meeting tracking.</summary>
    public Guid? TopicId { get; set; }

    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>Primary language code, e.g. zh-CN, en-US.</summary>
    public string Language { get; set; } = "zh-CN";

    /// <summary>
    /// CREATED / RECORDING / FINALIZING / PROCESSING / COMPLETED / ARCHIVED / DELETED
    /// </summary>
    public string Status { get; set; } = MeetingStatuses.Created;

    public DateTime? StartedAt { get; set; }
    public DateTime? EndedAt { get; set; }

    /// <summary>Meeting duration in milliseconds.</summary>
    public long DurationMs { get; set; }

    public Guid CreatedBy { get; set; }

    /// <summary>STRICT_LOCAL / LOCAL_FIRST / CLOUD_ENHANCED</summary>
    public string ProcessingPreset { get; set; } = "LOCAL_FIRST";

    /// <summary>PUBLIC / INTERNAL / CONFIDENTIAL / STRICT_CONFIDENTIAL</summary>
    public string DataClassification { get; set; } = "INTERNAL";

    /// <summary>Whether raw audio may be uploaded to cloud providers.</summary>
    public bool AllowAudioUpload { get; set; } = false;

    /// <summary>Whether transcript text may be uploaded to cloud LLM providers.</summary>
    public bool AllowTextUpload { get; set; } = true;

    /// <summary>Pointer to the official transcript version for this meeting.</summary>
    public Guid? OfficialTranscriptVersionId { get; set; }

    /// <summary>Pointer to the official minutes version for this meeting.</summary>
    public Guid? OfficialMinutesVersionId { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>Meeting lifecycle status values.</summary>
public static class MeetingStatuses
{
    public const string Created = "CREATED";
    public const string Recording = "RECORDING";
    public const string Finalizing = "FINALIZING";
    public const string Processing = "PROCESSING";
    public const string Completed = "COMPLETED";
    public const string Archived = "ARCHIVED";
    public const string Deleted = "DELETED";
}

/// <summary>Processing preset values exposed to users.</summary>
public static class ProcessingPresets
{
    public const string StrictLocal = "STRICT_LOCAL";
    public const string LocalFirst = "LOCAL_FIRST";
    public const string CloudEnhanced = "CLOUD_ENHANCED";
}

/// <summary>Meeting-specific data classification (extends the base set).</summary>
public static class MeetingDataClassification
{
    public const string Public = "PUBLIC";
    public const string Internal = "INTERNAL";
    public const string Confidential = "CONFIDENTIAL";
    public const string StrictConfidential = "STRICT_CONFIDENTIAL";
}
