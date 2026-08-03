namespace KnowledgeEngine.Domain.Entities;

/// <summary>
/// A speaker identified within a meeting. Combines local model output
/// (speaker_key) with the globally stable identifier (global_speaker_id)
/// produced by two-stage re-clustering across the entire meeting.
/// </summary>
public class MeetingSpeaker
{
    public Guid Id { get; set; }
    public Guid MeetingId { get; set; }

    /// <summary>Local label from the diarization model (e.g. "SPEAKER_00").</summary>
    public string SpeakerKey { get; set; } = string.Empty;

    /// <summary>
    /// Stable identifier assigned after global re-clustering.
    /// LLM and minutes must only use this or the user-confirmed display name.
    /// </summary>
    public string GlobalSpeakerId { get; set; } = string.Empty;

    /// <summary>User-assigned or auto-generated display name.</summary>
    public string? DisplayName { get; set; }

    /// <summary>Optional link to a Memorix participant entity.</summary>
    public Guid? ParticipantId { get; set; }

    /// <summary>UNCONFIRMED / USER_CONFIRMED / VERIFIED</summary>
    public string IdentityStatus { get; set; } = SpeakerIdentityStatuses.Unconfirmed;

    /// <summary>Reference to speaker embedding vector (encrypted, stored separately).</summary>
    public string? EmbeddingRef { get; set; }

    /// <summary>Model confidence score for this speaker cluster (0-1).</summary>
    public decimal Confidence { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>Speaker identity confirmation states.</summary>
public static class SpeakerIdentityStatuses
{
    public const string Unconfirmed = "UNCONFIRMED";
    public const string UserConfirmed = "USER_CONFIRMED";
    public const string Verified = "VERIFIED";
}
