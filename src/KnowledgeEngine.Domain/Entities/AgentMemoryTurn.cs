namespace KnowledgeEngine.Domain.Entities;

/// <summary>
/// A single user↔agent turn (round) within an <see cref="AgentMemorySession"/>.
///
/// <para>
/// A turn aggregates one user message, zero or more assistant messages, and the
/// actions (tool calls, edits, commands) taken during the assistant's response.
/// Turns are the primary unit that <c>MemoryExtractorService</c> scans for
/// memory-worthy content, because they preserve the conversational structure
/// that raw event streams lack.
/// </para>
/// </summary>
public class AgentMemoryTurn
{
    public Guid Id { get; set; }

    public Guid SessionId { get; set; }

    /// <summary>Sequence number of this turn within the session (1-based).</summary>
    public int Seq { get; set; }

    /// <summary>The user's prompt text for this turn.</summary>
    public string? UserMessage { get; set; }

    /// <summary>The assistant's response text (concatenated if multiple).</summary>
    public string? AssistantMessage { get; set; }

    /// <summary>Count of actions (tool calls/edits/commands) in this turn.</summary>
    public int ActionsCount { get; set; }

    /// <summary>Total tokens consumed in this turn (if reported by the agent).</summary>
    public int? TokensTotal { get; set; }

    /// <summary>active | completed — completed turns are candidates for extraction.</summary>
    public string Status { get; set; } = "active";

    public DateTime CreatedAt { get; set; }

    // Navigation
    public AgentMemorySession? Session { get; set; }
    public List<AgentMemoryAction> Actions { get; set; } = new();
}
