using KnowledgeEngine.Application.DTOs;

namespace KnowledgeEngine.Application.Interfaces;

/// <summary>
/// Provides MCP-compatible tool definitions and invocation for AI agents.
/// </summary>
public interface IAgentToolService
{
    /// <summary>
    /// Lists all available agent tools, optionally filtered by agent profile permissions.
    /// </summary>
    Task<List<AgentToolDefinition>> ListToolsAsync(Guid? agentProfileId = null, CancellationToken ct = default);

    /// <summary>
    /// Invokes the specified tool by name with the given input arguments.
    /// </summary>
    Task<AgentToolResult> InvokeToolAsync(
        Guid userId,
        string toolName,
        Dictionary<string, object> input,
        Guid? agentProfileId = null,
        CancellationToken ct = default);
}
