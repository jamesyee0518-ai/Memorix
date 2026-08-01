namespace KnowledgeEngine.Domain.Entities;

/// <summary>
/// User/tenant correction dictionary entry for post-ASR text correction.
/// Each entry maps a commonly misrecognized term to its correct form.
/// Entries are scoped to a workspace (null = global) and can be filtered by language.
/// </summary>
public class CorrectionDictionary
{
    public Guid Id { get; set; }

    /// <summary>Workspace scope. Null means the entry applies globally.</summary>
    public Guid? WorkspaceId { get; set; }

    /// <summary>The original (commonly misrecognized) text to match.</summary>
    public string OriginalText { get; set; } = string.Empty;

    /// <summary>The corrected replacement text.</summary>
    public string CorrectedText { get; set; } = string.Empty;

    /// <summary>
    /// The category of the correction:
    /// brand / person / term / abbreviation / homophone / custom.
    /// </summary>
    public string Category { get; set; } = "custom";

    /// <summary>Optional language code (e.g. "zh", "en") to scope the entry.</summary>
    public string? Language { get; set; }

    /// <summary>The user ID who created this entry.</summary>
    public string? CreatedBy { get; set; }

    /// <summary>Whether this entry is active and should be applied during correction.</summary>
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
