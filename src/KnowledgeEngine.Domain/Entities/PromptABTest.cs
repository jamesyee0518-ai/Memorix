namespace KnowledgeEngine.Domain.Entities;

/// <summary>
/// A/B test configuration for comparing two prompt registry versions.
/// Traffic is split between VariantA and VariantB based on
/// <see cref="TrafficSplitPercent"/> (percentage routed to VariantB).
/// </summary>
public class PromptABTest
{
    public Guid Id { get; set; }

    /// <summary>The prompt key being tested (e.g. "summary.default").</summary>
    public string PromptKey { get; set; } = string.Empty;

    /// <summary>Human-readable name for this A/B test.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Reference to the Variant A <see cref="PromptRegistry"/> (control).</summary>
    public Guid VariantAId { get; set; }

    /// <summary>Reference to the Variant B <see cref="PromptRegistry"/> (challenger).</summary>
    public Guid VariantBId { get; set; }

    /// <summary>Percentage of traffic routed to Variant B (0-100). Remainder goes to Variant A.</summary>
    public int TrafficSplitPercent { get; set; }

    /// <summary>Lifecycle status: created / running / completed.</summary>
    public string Status { get; set; } = PromptABTestStatuses.Created;

    /// <summary>The winning variant after test completion. Null until test is completed.</summary>
    public Guid? WinnerVariantId { get; set; }

    public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }

    /// <summary>User or system that created this A/B test.</summary>
    public string CreatedBy { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// Static constants for <see cref="PromptABTest.Status"/> values.
/// </summary>
public static class PromptABTestStatuses
{
    public const string Created = "created";
    public const string Running = "running";
    public const string Completed = "completed";
}
