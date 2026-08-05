namespace KnowledgeEngine.Domain.Entities;

/// <summary>
/// A point-to-point task handoff between two coding agents working on the same
/// <see cref="Project"/>.
///
/// <para>
/// Enables the closed loop: Codex completes implementation → creates a handoff
/// asking Claude to review → Claude retrieves it, reads the referenced memory +
/// commit, performs the review, writes back the result → Codex continues.
/// </para>
/// <para>
/// Status lifecycle: <c>open</c> → <c>in_progress</c> → <c>done</c>
/// (or <c>cancelled</c>). The <see cref="ToAgent"/> field uses structured
/// <c>AgentProfile.AgentType</c> values (e.g. "claude") for routing.
/// </para>
/// </summary>
public class AgentMemoryHandoff
{
    public Guid Id { get; set; }

    /// <summary>
    /// The project this handoff belongs to. Both the originating and receiving
    /// agents must be on the same project. Null only for legacy sessions.
    /// </summary>
    public Guid? ProjectId { get; set; }

    /// <summary>The session that created this handoff.</summary>
    public Guid FromSessionId { get; set; }

    /// <summary>The session that accepted (picked up) this handoff. Null until accepted.</summary>
    public Guid? ToSessionId { get; set; }

    /// <summary>
    /// The AgentType of the originator (e.g. "codex"). Copied from the
    /// originator's AgentProfile at creation time.
    /// </summary>
    public string FromAgent { get; set; } = string.Empty;

    /// <summary>
    /// The target AgentType (e.g. "claude"). Point-to-point: only agents whose
    /// AgentType matches this value can accept the handoff. Null means broadcast
    /// (any agent may accept).
    /// </summary>
    public string? ToAgent { get; set; }

    /// <summary>The task description, e.g. "请 Claude 审核多仓销售数据库设计".</summary>
    public string Task { get; set; } = string.Empty;

    /// <summary>open | in_progress | done | cancelled</summary>
    public string Status { get; set; } = "open";

    /// <summary>
    /// JSON array of context references for the receiving agent to read, e.g.
    /// ["memory://item/&lt;guid&gt;", "commit://&lt;sha&gt;"].
    /// </summary>
    public string? ContextRefsJson { get; set; }

    public string? GitBranch { get; set; }
    public string? CommitSha { get; set; }

    /// <summary>The review result / outcome written by the accepting agent on completion.</summary>
    public string? ResultSummary { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? AcceptedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    // Navigation
    public AgentMemorySession? FromSession { get; set; }

    // -----------------------------------------------------------------------
    // State transition methods
    // -----------------------------------------------------------------------

    /// <summary>Accept the handoff (open → in_progress).</summary>
    public void Accept(Guid toSessionId)
    {
        if (Status != "open")
        {
            throw new InvalidOperationException(
                $"Cannot accept handoff: current status is {Status}, expected 'open'.");
        }
        if (toSessionId == Guid.Empty)
        {
            throw new ArgumentException("toSessionId must be a non-empty GUID.", nameof(toSessionId));
        }
        ToSessionId = toSessionId;
        AcceptedAt = DateTime.UtcNow;
        Status = "in_progress";
    }

    /// <summary>Complete the handoff with a result summary (in_progress → done).</summary>
    public void Complete(string? resultSummary)
    {
        if (Status != "in_progress")
        {
            throw new InvalidOperationException(
                $"Cannot complete handoff: current status is {Status}, expected 'in_progress'.");
        }
        ResultSummary = resultSummary;
        CompletedAt = DateTime.UtcNow;
        Status = "done";
    }

    /// <summary>Cancel the handoff (open|in_progress → cancelled).</summary>
    public void Cancel()
    {
        if (Status is "done" or "cancelled")
        {
            throw new InvalidOperationException(
                $"Cannot cancel handoff: current status is {Status}.");
        }
        Status = "cancelled";
        CompletedAt = DateTime.UtcNow;
    }
}
