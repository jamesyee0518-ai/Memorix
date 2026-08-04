using KnowledgeEngine.Domain.Entities;

namespace KnowledgeEngine.Application.Interfaces;

/// <summary>
/// Privacy Transformation Layer for external LLM calls.
/// Identifies sensitive entities in transcript text, replaces them with
/// type-safe placeholders before sending to external providers, and
/// restores the originals from encrypted mappings after receiving the response.
/// </summary>
public interface IPrivacyTransformationService
{
    /// <summary>
    /// Scans text for sensitive entities and replaces them with stable placeholders.
    /// Creates PseudonymMapping records in the local trusted domain.
    /// </summary>
    Task<PrivacyTransformResult> MaskAsync(
        Guid meetingId,
        string text,
        string maskingMode,
        CancellationToken ct);

    /// <summary>
    /// Restores original values by replacing placeholders back with their
    /// decrypted originals. Only restores at exact, validated positions.
    /// Tampered or fictional placeholders are marked RESTORE_FAILED.
    /// </summary>
    Task<string> RestoreAsync(
        Guid meetingId,
        string text,
        CancellationToken ct);

    /// <summary>
    /// Gets all pseudonym mappings for a meeting (for audit/admin purposes).
    /// </summary>
    Task<List<PseudonymMapping>> GetMappingsAsync(Guid meetingId, CancellationToken ct);

    /// <summary>
    /// Purges all mappings for a meeting after processing is complete.
    /// </summary>
    Task PurgeAsync(Guid meetingId, CancellationToken ct);
}

/// <summary>
/// Result of a privacy transformation (masking) operation.
/// </summary>
public class PrivacyTransformResult
{
    /// <summary>Text with placeholders substituted in.</summary>
    public string MaskedText { get; set; } = string.Empty;

    /// <summary>Number of entities that were masked.</summary>
    public int MaskedCount { get; set; }

    /// <summary>Details of each replacement for audit logging.</summary>
    public List<MaskRecord> Masks { get; set; } = new();

    /// <summary>True if the masking mode was LOCAL_ONLY and the text could not be safely masked.</summary>
    public bool Blocked { get; set; }
}

public class MaskRecord
{
    public string EntityType { get; set; } = string.Empty;
    public string Placeholder { get; set; } = string.Empty;
    public int StartIndex { get; set; }
    public int Length { get; set; }
}
