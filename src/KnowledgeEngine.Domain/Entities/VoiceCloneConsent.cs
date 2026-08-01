namespace KnowledgeEngine.Domain.Entities;

/// <summary>
/// Tracks user consent for voice cloning operations.
/// Voice cloning consent must be explicit — the default status is "pending"
/// until the user grants it. Consent can be revoked at any time.
/// </summary>
public class VoiceCloneConsent
{
    public Guid Id { get; set; }

    /// <summary>The user who owns this consent record.</summary>
    public Guid UserId { get; set; }

    /// <summary>The voice profile identifier that consent applies to.</summary>
    public string VoiceId { get; set; } = string.Empty;

    /// <summary>
    /// Consent status: "pending" (default), "granted", or "revoked".
    /// Cloning is only permitted when status is "granted".
    /// </summary>
    public string ConsentStatus { get; set; } = VoiceCloneConsentStatuses.Pending;

    /// <summary>
    /// How consent was obtained (e.g. "web_ui", "api", "voice_signature").
    /// </summary>
    public string? ConsentMethod { get; set; }

    /// <summary>When consent was granted. Null if not yet granted.</summary>
    public DateTime? GrantedAt { get; set; }

    /// <summary>When consent was revoked. Null if never revoked.</summary>
    public DateTime? RevokedAt { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// Voice clone consent status constants.
/// </summary>
public static class VoiceCloneConsentStatuses
{
    public const string Pending = "pending";
    public const string Granted = "granted";
    public const string Revoked = "revoked";
}
