using System.ComponentModel;
using System.Text.Json;
using KnowledgeEngine.Application.Interfaces;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace KnowledgeEngine.Infrastructure.Mcp;

/// <summary>
/// MCP tool definitions for the Agent Memory subsystem, exposed via the
/// official ModelContextProtocol C# SDK v2.0.0. Each method delegates to
/// <see cref="IAgentToolService"/> for permission-checked business logic,
/// following the same pattern as <see cref="MemorixMcpTools"/>.
///
/// Tools are discovered automatically by <c>WithToolsFromAssembly()</c> via
/// the <see cref="McpServerToolType"/> and <see cref="McpServerTool"/> attributes.
/// </summary>
[McpServerToolType]
public class AgentMemoryMcpTools
{
    private readonly IAgentToolService _toolService;
    private readonly ILogger<AgentMemoryMcpTools> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public AgentMemoryMcpTools(IAgentToolService toolService, ILogger<AgentMemoryMcpTools> logger)
    {
        _toolService = toolService;
        _logger = logger;
    }

    // ===== memory_start_session =====

    [McpServerTool(Name = "memory_start_session")]
    [Description("Create or resume an agent memory session. Returns session details including the session ID needed for subsequent memory operations.")]
    public async Task<string> StartSessionAsync(
        string externalSessionKey,
        string taskTitle,
        string? agentProfileId = null,
        string? topicId = null)
    {
        var input = new Dictionary<string, object>
        {
            ["external_session_key"] = externalSessionKey,
            ["task_title"] = taskTitle
        };
        if (agentProfileId != null) input["agent_profile_id"] = agentProfileId;
        if (topicId != null) input["topic_id"] = topicId;

        return await InvokeAndSerializeAsync("memory_start_session", input);
    }

    // ===== memory_get_context =====

    [McpServerTool(Name = "memory_get_context")]
    [Description("Retrieve the assembled context pack for a session, including L1 (immediate), L2 (related), and L3 (background) memory layers within the token budget.")]
    public async Task<string> GetContextAsync(
        string sessionId,
        int? maxTokens = null)
    {
        var input = new Dictionary<string, object> { ["session_id"] = sessionId };
        if (maxTokens.HasValue) input["max_tokens"] = maxTokens.Value;

        return await InvokeAndSerializeAsync("memory_get_context", input);
    }

    // ===== memory_capture =====

    [McpServerTool(Name = "memory_capture")]
    [Description("Submit a memory candidate (observation, task state, preference, or fact) to the agent memory system for admission processing.")]
    public async Task<string> CaptureAsync(
        string kind,
        string title,
        string? sessionId = null,
        string? content = null,
        string? summary = null)
    {
        var input = new Dictionary<string, object>
        {
            ["kind"] = kind,
            ["title"] = title
        };
        if (sessionId != null) input["session_id"] = sessionId;
        if (content != null) input["content"] = content;
        if (summary != null) input["summary"] = summary;

        return await InvokeAndSerializeAsync("memory_capture", input);
    }

    // ===== memory_search =====

    [McpServerTool(Name = "memory_search")]
    [Description("Search the agent memory store for items matching the query, optionally filtered by session. Returns ranked memory items with relevance scores.")]
    public async Task<string> SearchAsync(
        string query,
        string? sessionId = null,
        int? limit = null)
    {
        var input = new Dictionary<string, object> { ["query"] = query };
        if (sessionId != null) input["session_id"] = sessionId;
        if (limit.HasValue) input["limit"] = limit.Value;

        return await InvokeAndSerializeAsync("memory_search", input);
    }

    // ===== memory_confirm (P2.API-01) =====

    [McpServerTool(Name = "memory_confirm")]
    [Description("Confirm or reject a memory item. Requires agent_memory:confirm scope. Use action='confirm' to promote a qualified item to confirmed state, or action='reject' to reject it.")]
    public async Task<string> ConfirmAsync(
        string memoryItemId,
        string action,
        string? note = null)
    {
        var input = new Dictionary<string, object>
        {
            ["memory_item_id"] = memoryItemId,
            ["action"] = action
        };
        if (note != null) input["note"] = note;

        return await InvokeAndSerializeAsync("memory_confirm", input);
    }

    // ===== memory_forget (P2.API-01) =====

    [McpServerTool(Name = "memory_forget")]
    [Description("Forget (soft-delete) a confirmed memory item. Requires agent_memory:delete scope. The 'confirm' parameter must be set to true as an explicit safety confirmation.")]
    public async Task<string> ForgetAsync(
        string memoryItemId,
        bool confirm)
    {
        if (!confirm)
        {
            return JsonSerializer.Serialize(new { error = "The 'confirm' parameter must be true to forget a memory item." }, JsonOptions);
        }

        var input = new Dictionary<string, object>
        {
            ["memory_item_id"] = memoryItemId,
            ["confirm"] = true
        };

        return await InvokeAndSerializeAsync("memory_forget", input);
    }

    // ===== memory_checkpoint (P2.API-02) =====

    [McpServerTool(Name = "memory_checkpoint")]
    [Description("Create a checkpoint for the current session, capturing a snapshot of active memory items for context preservation and recovery.")]
    public async Task<string> CheckpointAsync(string sessionId)
    {
        var input = new Dictionary<string, object> { ["session_id"] = sessionId };
        return await InvokeAndSerializeAsync("memory_checkpoint", input);
    }

    // ===== memory_handoff (P2.API-02) =====

    [McpServerTool(Name = "memory_handoff")]
    [Description("Get the latest delivered checkpoint for a session, enabling context handoff to a new agent or session recovery.")]
    public async Task<string> HandoffAsync(string sessionId)
    {
        var input = new Dictionary<string, object> { ["session_id"] = sessionId };
        return await InvokeAndSerializeAsync("memory_handoff", input);
    }

    // ===== Shared helpers =====

    private async Task<string> InvokeAndSerializeAsync(
        string toolName,
        Dictionary<string, object> input)
    {
        var userId = GetMcpUserId();
        var agentProfileId = GetMcpAgentProfileId();

        _logger.LogDebug("MCP tool invoked: {ToolName}", toolName);

        var result = await _toolService.InvokeToolAsync(userId, toolName, input, agentProfileId);

        var payload = result.Success
            ? JsonSerializer.Serialize(result.Data, JsonOptions)
            : JsonSerializer.Serialize(new { error = result.Error }, JsonOptions);

        return payload;
    }

    /// <summary>
    /// Gets the MCP user ID from the MEMORIX_MCP_USER_ID environment variable.
    /// Returns Guid.Empty if not set (the AgentToolService will resolve the first user).
    /// </summary>
    private static Guid GetMcpUserId()
    {
        var envUserId = Environment.GetEnvironmentVariable("MEMORIX_MCP_USER_ID");
        if (Guid.TryParse(envUserId, out var userId))
            return userId;
        return Guid.Empty;
    }

    /// <summary>
    /// Gets the MCP agent profile ID from the MEMORIX_AGENT_PROFILE_ID environment variable.
    /// Returns null if not set (all default tools are allowed without profile restrictions).
    /// </summary>
    private static Guid? GetMcpAgentProfileId()
    {
        var envProfileId = Environment.GetEnvironmentVariable("MEMORIX_AGENT_PROFILE_ID");
        if (Guid.TryParse(envProfileId, out var profileId))
            return profileId;
        return null;
    }
}
