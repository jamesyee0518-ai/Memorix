namespace KnowledgeEngine.Domain.Entities;

/// <summary>
/// Versioned prompt registry entry for AI capabilities (summary, entity extraction, etc.).
/// Supports semantic versioning, provider compatibility filtering, and lifecycle management
/// (draft -> published -> archived) with optional A/B testing and evaluation scores.
/// </summary>
public class PromptRegistry
{
    public Guid Id { get; set; }

    /// <summary>Logical prompt key, e.g. "summary.default", "entity.extract".</summary>
    public string PromptKey { get; set; } = string.Empty;

    /// <summary>Semantic version string, e.g. "1.0.0", "1.1.0".</summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>Human-readable title for this prompt version.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Description of what this prompt does and when to use it.</summary>
    public string? Description { get; set; }

    /// <summary>The system prompt text sent to the LLM.</summary>
    public string SystemPrompt { get; set; } = string.Empty;

    /// <summary>User prompt template with placeholders (e.g. {{title}}, {{content}}).</summary>
    public string UserPromptTemplate { get; set; } = string.Empty;

    /// <summary>Optional language code filter (e.g. "zh-CN", "en"). Null = language-agnostic.</summary>
    public string? Language { get; set; }

    /// <summary>
    /// Comma-separated list of provider IDs this prompt is compatible with
    /// (e.g. "openai,azure"). Empty string means compatible with all providers.
    /// </summary>
    public string ProviderCompatibility { get; set; } = string.Empty;

    /// <summary>Optional evaluation score (0-100) from automated or manual evaluation.</summary>
    public double? EvaluationScore { get; set; }

    /// <summary>Whether this prompt version is the active one for its key.</summary>
    public bool IsActive { get; set; }

    /// <summary>Lifecycle status: draft / published / archived.</summary>
    public string Status { get; set; } = PromptRegistryStatuses.Draft;

    /// <summary>Timestamp when this prompt was published. Null while in draft.</summary>
    public DateTime? PublishedAt { get; set; }

    /// <summary>User or system that created this prompt version.</summary>
    public string CreatedBy { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// Static constants for <see cref="PromptRegistry.Status"/> values.
/// </summary>
public static class PromptRegistryStatuses
{
    public const string Draft = "draft";
    public const string Published = "published";
    public const string Archived = "archived";
}
