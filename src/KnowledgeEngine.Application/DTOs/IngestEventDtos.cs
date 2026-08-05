namespace KnowledgeEngine.Application.DTOs;

/// <summary>
/// A batch of normalized events from an external collection shim, posted to the
/// ingest endpoint. The shim handles agent-specific format normalization
/// (Claude hooks, Codex rollout JSONL, etc.) and delivers a canonical batch.
/// </summary>
public class IngestEventBatch
{
    /// <summary>Source agent type: codex | claude | trae | ...</summary>
    public string Agent { get; set; } = string.Empty;

    /// <summary>The agent's own session identifier (external key for idempotency).</summary>
    public string SessionId { get; set; } = string.Empty;

    /// <summary>Git remote URL for project resolution (null for local-only repos).</summary>
    public string? GitRemote { get; set; }

    /// <summary>Repository name for project resolution.</summary>
    public string? RepoName { get; set; }

    public string? GitBranch { get; set; }
    public string? CommitSha { get; set; }

    /// <summary>Human-readable task title for the session.</summary>
    public string? TaskTitle { get; set; }

    /// <summary>The normalized events to ingest.</summary>
    public List<NormalizedEvent> Events { get; set; } = new();

    /// <summary>
    /// Source cursor for idempotent ingestion (e.g. "claude:~/.claude/projects/slug/abc.jsonl:line 342").
    /// If set, the ingest service checks IngestOffset to skip already-processed events.
    /// </summary>
    public string? SourceCursor { get; set; }

    /// <summary>Checksum of the batch for dedup verification.</summary>
    public string? Checksum { get; set; }
}

/// <summary>
/// A single normalized agent event, agent-agnostic. This is the canonical schema
/// that all adapters (Claude/Codex/Trae) convert their native events into.
/// </summary>
public class NormalizedEvent
{
    /// <summary>
    /// Event type: session_start | user_prompt | post_tool | post_edit |
    /// post_command | post_response | session_end | ...
    /// </summary>
    public string EventType { get; set; } = string.Empty;

    /// <summary>ISO-8601 timestamp of the event.</summary>
    public string Timestamp { get; set; } = string.Empty;

    /// <summary>User prompt text (for user_prompt events).</summary>
    public string? UserPrompt { get; set; }

    /// <summary>AI response text (for post_response events).</summary>
    public string? AiResponse { get; set; }

    public string? ToolName { get; set; }
    public object? ToolInput { get; set; }
    public string? ToolResult { get; set; }
    public string? FilePath { get; set; }
    public string? Command { get; set; }
    public string? CommandOutput { get; set; }
    public int? TokensTotal { get; set; }
}

/// <summary>Result of ingesting an event batch.</summary>
public class IngestResult
{
    public string SessionId { get; set; } = string.Empty;
    public int TurnsCreated { get; set; }
    public int ActionsCreated { get; set; }
    public int EventsSkipped { get; set; }
    public Guid? ProjectId { get; set; }
    public string? Message { get; set; }
}
