namespace KnowledgeEngine.Domain.Entities;

/// <summary>
/// A versioned set of meeting minutes generated from a specific transcript version.
/// Stores structured output (summary, topics, decisions, action items, risks) as JSON.
/// </summary>
public class MeetingMinutesVersion
{
    public Guid Id { get; set; }
    public Guid MeetingId { get; set; }

    /// <summary>Version number within the meeting (1-based, increments on each generation).</summary>
    public int VersionNo { get; set; }

    /// <summary>The transcript version these minutes are based on.</summary>
    public Guid? TranscriptVersionId { get; set; }

    /// <summary>Optional template used to structure the minutes.</summary>
    public Guid? TemplateId { get; set; }

    /// <summary>One-sentence summary.</summary>
    public string? Summary { get; set; }

    /// <summary>Structured minutes content stored as JSON.</summary>
    public string ContentJson { get; set; } = "{}";

    /// <summary>LLM provider used to generate these minutes.</summary>
    public string Provider { get; set; } = string.Empty;

    /// <summary>LLM model used to generate these minutes.</summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>DRAFT / READY / STALE / SUPERSEDED</summary>
    public string Status { get; set; } = MinutesVersionStatuses.Draft;

    /// <summary>Who or what created this version (user id or "system").</summary>
    public string? CreatedBy { get; set; }

    public DateTime CreatedAt { get; set; }
}

/// <summary>Meeting minutes version status values.</summary>
public static class MinutesVersionStatuses
{
    public const string Draft = "DRAFT";
    public const string Ready = "READY";
    public const string Stale = "STALE";
    public const string Superseded = "SUPERSEDED";
}
