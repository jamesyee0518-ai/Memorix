using KnowledgeEngine.Domain.Entities;

namespace KnowledgeEngine.Application.Interfaces;

/// <summary>
/// Post-ASR text correction service.
/// Applies dictionary-based corrections (brand names, person names, terminology,
/// abbreviations, homophones, and user-defined entries) to ASR output text.
/// Corrections are case-insensitive with word-boundary awareness for Latin scripts.
/// </summary>
public interface IPostAsrCorrectionService
{
    /// <summary>
    /// Corrects transcription text using workspace dictionary entries and
    /// built-in correction rules.
    /// </summary>
    /// <param name="request">The correction request containing text and context.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The correction result with corrected text and applied changes.</returns>
    Task<CorrectionResult> CorrectAsync(CorrectionRequest request, CancellationToken ct);

    /// <summary>
    /// Adds a new entry to the correction dictionary for the given workspace.
    /// </summary>
    /// <param name="workspaceId">The workspace scope, or null for global entries.</param>
    /// <param name="original">The original (misrecognized) text to match.</param>
    /// <param name="corrected">The corrected replacement text.</param>
    /// <param name="category">The entry category (brand/person/term/abbreviation/homophone/custom).</param>
    /// <param name="createdBy">The user ID who created the entry.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The created dictionary entry.</returns>
    Task<CorrectionDictionary> AddEntryAsync(Guid? workspaceId, string original, string corrected, string? category, string? createdBy, CancellationToken ct);

    /// <summary>
    /// Lists correction dictionary entries for the given workspace, optionally
    /// filtered by category.
    /// </summary>
    /// <param name="workspaceId">The workspace scope, or null for global entries.</param>
    /// <param name="category">Optional category filter.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A list of matching dictionary entries.</returns>
    Task<List<CorrectionDictionary>> ListEntriesAsync(Guid? workspaceId, string? category, CancellationToken ct);

    /// <summary>
    /// Deletes (soft-deletes by deactivating) a dictionary entry by ID.
    /// </summary>
    /// <param name="entryId">The entry ID to delete.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if the entry was found and deleted; false otherwise.</returns>
    Task<bool> DeleteEntryAsync(Guid entryId, CancellationToken ct);
}

// ── Post-ASR Correction DTOs ──

/// <summary>
/// Request payload for post-ASR text correction.
/// </summary>
public class CorrectionRequest
{
    /// <summary>The text to correct (full segment text or concatenated transcript).</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>The workspace scope for dictionary lookup, or null for global.</summary>
    public Guid? WorkspaceId { get; set; }

    /// <summary>The language code (e.g. "zh", "en") to filter dictionary entries.</summary>
    public string? Language { get; set; }

    /// <summary>Segment UUIDs being corrected, for tracking and logging.</summary>
    public List<string>? SegmentUuids { get; set; }

    /// <summary>Additional context text (e.g. full transcript) to aid correction decisions.</summary>
    public string? Context { get; set; }
}

/// <summary>
/// Result of a post-ASR correction operation.
/// </summary>
public class CorrectionResult
{
    /// <summary>The fully corrected text.</summary>
    public string CorrectedText { get; set; } = string.Empty;

    /// <summary>List of individual corrections applied.</summary>
    public List<CorrectionChange> Changes { get; set; } = new();

    /// <summary>The number of dictionary entries that were applied at least once.</summary>
    public int AppliedDictionaryEntries { get; set; }
}

/// <summary>
/// Represents a single correction applied to the text.
/// </summary>
public class CorrectionChange
{
    /// <summary>The original matched text.</summary>
    public string Original { get; set; } = string.Empty;

    /// <summary>The corrected replacement text.</summary>
    public string Corrected { get; set; } = string.Empty;

    /// <summary>The category of the correction (brand/person/term/abbreviation/homophone/custom).</summary>
    public string Category { get; set; } = string.Empty;
}
