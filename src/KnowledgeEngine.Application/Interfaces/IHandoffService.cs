using KnowledgeEngine.Application.DTOs;

namespace KnowledgeEngine.Application.Interfaces;

/// <summary>
/// Manages point-to-point task handoffs between coding agents.
/// Enables the closed loop: agent A completes work → hands off to agent B →
/// B reviews and writes back the result → A continues.
/// </summary>
public interface IHandoffService
{
    /// <summary>
    /// Create a handoff from the caller's session to a target agent.
    /// The originator's AgentType is resolved from their AgentProfile.
    /// </summary>
    Task<HandoffDto> CreateHandoffAsync(
        Guid userId,
        Guid? agentProfileId,
        CreateHandoffInput input,
        CancellationToken ct = default);

    /// <summary>
    /// Retrieve handoffs available to the calling agent. Point-to-point: matches
    /// handoffs where ToAgent equals the caller's AgentType OR ToAgent is null
    /// (broadcast). Filtered by project and status.
    /// </summary>
    Task<List<HandoffDto>> GetHandoffsAsync(
        Guid userId,
        Guid? agentProfileId,
        GetHandoffsInput input,
        CancellationToken ct = default);

    /// <summary>
    /// Accept (pick up) an open handoff. Only agents whose AgentType matches
    /// the handoff's ToAgent (or any agent if ToAgent is null) may accept.
    /// Transitions status: open → in_progress.
    /// </summary>
    Task<HandoffDto> AcceptHandoffAsync(
        Guid userId,
        Guid? agentProfileId,
        Guid handoffId,
        Guid toSessionId,
        CancellationToken ct = default);

    /// <summary>
    /// Complete a handoff by writing back the result summary.
    /// Transitions status: in_progress → done.
    /// </summary>
    Task<HandoffDto> CompleteHandoffAsync(
        Guid userId,
        Guid? agentProfileId,
        Guid handoffId,
        string? resultSummary,
        CancellationToken ct = default);
}
