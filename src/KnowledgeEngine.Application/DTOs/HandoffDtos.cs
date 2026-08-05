namespace KnowledgeEngine.Application.DTOs;

/// <summary>
/// Input for creating a point-to-point handoff between agents.
/// </summary>
public class CreateHandoffInput
{
    /// <summary>The session creating the handoff (the originator).</summary>
    public Guid FromSessionId { get; set; }

    /// <summary>
    /// Target AgentType (e.g. "claude"). Only agents with matching AgentType
    /// can accept. Null = broadcast (any agent may accept).
    /// </summary>
    public string? ToAgent { get; set; }

    /// <summary>The task description for the receiving agent.</summary>
    public string Task { get; set; } = string.Empty;

    /// <summary>
    /// Context references for the receiver to read, e.g.
    /// ["memory://item/&lt;guid&gt;", "commit://&lt;sha&gt;"].
    /// </summary>
    public List<string>? ContextRefs { get; set; }

    public string? GitBranch { get; set; }
    public string? CommitSha { get; set; }
}

/// <summary>
/// Query parameters for retrieving handoffs available to an agent.
/// </summary>
public class GetHandoffsInput
{
    /// <summary>Filter by project. If null, uses the caller's session project.</summary>
    public Guid? ProjectId { get; set; }

    /// <summary>
    /// Filter by target agent. If null, matches handoffs where ToAgent is null
    /// (broadcast) OR ToAgent equals the caller's AgentType.
    /// </summary>
    public string? ToAgent { get; set; }

    /// <summary>Filter by status. Defaults to "open".</summary>
    public string? Status { get; set; }

    public int Limit { get; set; } = 20;
}

public class HandoffDto
{
    public Guid Id { get; set; }
    public Guid? ProjectId { get; set; }
    public Guid FromSessionId { get; set; }
    public Guid? ToSessionId { get; set; }
    public string FromAgent { get; set; } = string.Empty;
    public string? ToAgent { get; set; }
    public string Task { get; set; } = string.Empty;
    public string Status { get; set; } = "open";
    public List<string>? ContextRefs { get; set; }
    public string? GitBranch { get; set; }
    public string? CommitSha { get; set; }
    public string? ResultSummary { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? AcceptedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}
