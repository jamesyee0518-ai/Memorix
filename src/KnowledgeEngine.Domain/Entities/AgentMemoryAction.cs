namespace KnowledgeEngine.Domain.Entities;

/// <summary>
/// A single discrete action taken by an agent during a turn — a tool call,
/// a file edit, or a shell command. These are the raw events collected by the
/// external ingest shim and stored structurally for the Memory Extractor.
/// </summary>
public class AgentMemoryAction
{
    public Guid Id { get; set; }

    public Guid TurnId { get; set; }

    /// <summary>tool | edit | command — the category of this action.</summary>
    public string ActionKind { get; set; } = string.Empty;

    /// <summary>Tool name (e.g. "Edit", "Bash", "search_memory"). Null for edits/commands.</summary>
    public string? ToolName { get; set; }

    /// <summary>JSON-serialized tool input arguments.</summary>
    public string? ToolInputJson { get; set; }

    /// <summary>Tool result/output (truncated).</summary>
    public string? ToolResult { get; set; }

    /// <summary>File path affected (for edit actions).</summary>
    public string? FilePath { get; set; }

    /// <summary>Command executed (for command actions).</summary>
    public string? Command { get; set; }

    /// <summary>Whether the action succeeded.</summary>
    public bool Success { get; set; }

    public DateTime CreatedAt { get; set; }

    // Navigation
    public AgentMemoryTurn? Turn { get; set; }
}
