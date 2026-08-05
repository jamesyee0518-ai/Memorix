using System.ComponentModel;
using System.Text.Json;
using KnowledgeEngine.Application.DTOs;
using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Domain.Entities;
using KnowledgeEngine.Infrastructure.AgentMemory;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace KnowledgeEngine.Infrastructure.Mcp;

/// <summary>
/// MCP tool definitions for the Agent Memory subsystem, exposed via the
/// official ModelContextProtocol C# SDK. Each tool delegates directly to
/// <see cref="IAgentMemoryService"/>, <see cref="IHandoffService"/>, or
/// <see cref="CheckpointService"/>.
///
/// <para>
/// These tools are discovered automatically by <c>WithToolsFromAssembly()</c> via
/// the <see cref="McpServerToolType"/> and <see cref="McpServerTool"/> attributes.
/// </para>
/// <para>
/// Agent identity is resolved from environment variables set in the MCP client
/// config: <c>MEMORIX_MCP_USER_ID</c> and <c>MEMORIX_AGENT_PROFILE_ID</c>.
/// </para>
/// </summary>
[McpServerToolType]
public class AgentMemoryMcpTools
{
    private readonly IAgentMemoryService _memoryService;
    private readonly IHandoffService _handoffService;
    private readonly CheckpointService _checkpointService;
    private readonly IAppDbContext _db;
    private readonly ILogger<AgentMemoryMcpTools> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public AgentMemoryMcpTools(
        IAgentMemoryService memoryService,
        IHandoffService handoffService,
        CheckpointService checkpointService,
        IAppDbContext db,
        ILogger<AgentMemoryMcpTools> logger)
    {
        _memoryService = memoryService;
        _handoffService = handoffService;
        _checkpointService = checkpointService;
        _db = db;
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
        var userId = GetMcpUserId();
        var profileId = GetMcpAgentProfileId();
        var workspaceId = await ResolveWorkspaceIdAsync(userId);

        var session = await _memoryService.StartSessionAsync(
            userId, workspaceId, profileId, externalSessionKey, taskTitle,
            ParseGuid(topicId));

        return JsonSerializer.Serialize(session, JsonOptions);
    }

    // ===== memory_get_context =====

    [McpServerTool(Name = "memory_get_context")]
    [Description("Retrieve the assembled context pack for a session, including L1 (immediate), L2 (related), and L3 (background) memory layers within the token budget.")]
    public async Task<string> GetContextAsync(
        string sessionId,
        int? maxTokens = null)
    {
        try
        {
            var context = await _memoryService.GetContextAsync(ParseGuid(sessionId), maxTokens);
            return JsonSerializer.Serialize(context, JsonOptions);
        }
        catch (Exception ex)
        {
            return ErrorJson(ex.Message);
        }
    }

    // ===== memory_capture =====

    [McpServerTool(Name = "memory_capture")]
    [Description("Submit a memory candidate (observation, task state, preference, or fact) to the agent memory system for admission processing.")]
    public async Task<string> CaptureAsync(
        string kind,
        string title,
        string? sessionId = null,
        string? content = null,
        string? summary = null,
        int? importance = null)
    {
        var userId = GetMcpUserId();
        var profileId = GetMcpAgentProfileId();
        var workspaceId = await ResolveWorkspaceIdAsync(userId);

        var input = new CaptureMemoryInput
        {
            SessionId = ParseGuid(sessionId),
            Kind = kind,
            Title = title,
            Content = content,
            Summary = summary,
            Importance = importance ?? 5
        };

        var item = await _memoryService.CaptureMemoryAsync(userId, workspaceId, input);
        return JsonSerializer.Serialize(item, JsonOptions);
    }

    // ===== memory_search =====

    [McpServerTool(Name = "memory_search")]
    [Description("Search the agent memory store for items matching the query. Supports filtering by session, project, memory types (e.g. decision,fact,constraint), and agent type. Returns ranked memory items.")]
    public async Task<string> SearchAsync(
        string query,
        string? sessionId = null,
        string? projectId = null,
        string? types = null,
        string? agent = null,
        int? limit = null)
    {
        var userId = GetMcpUserId();
        var profileId = GetMcpAgentProfileId();
        var workspaceId = await ResolveWorkspaceIdAsync(userId);

        List<string>? typeList = null;
        if (!string.IsNullOrWhiteSpace(types))
        {
            try { typeList = JsonSerializer.Deserialize<List<string>>(types); }
            catch { /* leave null */ }
        }

        var input = new SearchMemoryInput
        {
            Query = query,
            SessionId = ParseGuid(sessionId),
            ProjectId = ParseGuid(projectId),
            Types = typeList,
            Agent = agent,
            Limit = limit ?? 20
        };

        var results = await _memoryService.SearchMemoryAsync(userId, workspaceId, input);
        return JsonSerializer.Serialize(results, JsonOptions);
    }

    // ===== memory_get (single item by ID) =====

    [McpServerTool(Name = "memory_get")]
    [Description("Retrieve a single memory item by its ID, including full content and evidence.")]
    public async Task<string> GetAsync(string memoryItemId)
    {
        var item = await _memoryService.GetMemoryItemAsync(ParseGuid(memoryItemId));
        if (item == null)
        {
            return ErrorJson($"Memory item {memoryItemId} not found.");
        }
        return JsonSerializer.Serialize(item, JsonOptions);
    }

    // ===== memory_confirm =====

    [McpServerTool(Name = "memory_confirm")]
    [Description("Confirm or reject a memory item. Requires agent_memory:confirm scope. Use action='confirm' to promote a qualified item to confirmed state, or action='reject' to reject it.")]
    public async Task<string> ConfirmAsync(
        string memoryItemId,
        string action,
        string? note = null)
    {
        // Delegate to AgentMemoryService via controller-level logic.
        // For MCP, we resolve and call the domain entity directly.
        var userId = GetMcpUserId();
        var profileId = GetMcpAgentProfileId();

        var item = await _db.AgentMemoryItems
            .FirstOrDefaultAsync(i => i.Id == ParseGuid(memoryItemId));
        if (item == null)
        {
            return ErrorJson($"Memory item {memoryItemId} not found.");
        }

        try
        {
            if (action == "confirm")
            {
                item.Confirm();
            }
            else if (action == "reject")
            {
                item.Reject();
            }
            else
            {
                return ErrorJson($"Unknown action '{action}'. Use 'confirm' or 'reject'.");
            }

            await _db.SaveChangesAsync();
            return JsonSerializer.Serialize(new { id = item.Id, admission_state = item.AdmissionState.ToString().ToLowerInvariant(), status = item.Status.ToString().ToLowerInvariant() }, JsonOptions);
        }
        catch (Exception ex)
        {
            return ErrorJson(ex.Message);
        }
    }

    // ===== memory_forget =====

    [McpServerTool(Name = "memory_forget")]
    [Description("Forget (soft-delete) a confirmed memory item. Requires agent_memory:delete scope. The 'confirm' parameter must be set to true as an explicit safety confirmation.")]
    public async Task<string> ForgetAsync(
        string memoryItemId,
        bool confirm)
    {
        if (!confirm)
        {
            return ErrorJson("The 'confirm' parameter must be true to forget a memory item.");
        }

        var item = await _db.AgentMemoryItems
            .FirstOrDefaultAsync(i => i.Id == ParseGuid(memoryItemId));
        if (item == null)
        {
            return ErrorJson($"Memory item {memoryItemId} not found.");
        }

        try
        {
            item.Forget();
            await _db.SaveChangesAsync();
            return JsonSerializer.Serialize(new { id = item.Id, status = item.Status.ToString().ToLowerInvariant() }, JsonOptions);
        }
        catch (Exception ex)
        {
            return ErrorJson(ex.Message);
        }
    }

    // ===== memory_checkpoint =====

    [McpServerTool(Name = "memory_checkpoint")]
    [Description("Create a checkpoint for the current session, capturing a snapshot of active memory items for context preservation and recovery.")]
    public async Task<string> CheckpointAsync(string sessionId)
    {
        try
        {
            var checkpoint = await _checkpointService.CreateCheckpointAsync(ParseGuid(sessionId));
            return JsonSerializer.Serialize(new
            {
                id = checkpoint.Id,
                session_id = checkpoint.SessionId,
                from_sequence = checkpoint.FromSequence,
                to_sequence = checkpoint.ToSequence,
                token_estimate = checkpoint.TokenEstimate,
                delivery_state = checkpoint.DeliveryState
            }, JsonOptions);
        }
        catch (Exception ex)
        {
            return ErrorJson(ex.Message);
        }
    }

    // ===== memory_handoff (create) =====

    [McpServerTool(Name = "memory_handoff")]
    [Description("Create a point-to-point task handoff to another agent. The originator's agent type is resolved from their profile. Use to_agent to target a specific agent (e.g. 'claude'), or omit for broadcast.")]
    public async Task<string> HandoffAsync(
        string fromSessionId,
        string task,
        string? toAgent = null,
        string? contextRefs = null,
        string? gitBranch = null,
        string? commitSha = null)
    {
        var userId = GetMcpUserId();
        var profileId = GetMcpAgentProfileId();

        List<string>? refs = null;
        if (!string.IsNullOrWhiteSpace(contextRefs))
        {
            try
            {
                refs = JsonSerializer.Deserialize<List<string>>(contextRefs);
            }
            catch
            {
                return ErrorJson("contextRefs must be a JSON array of strings.");
            }
        }

        var input = new CreateHandoffInput
        {
            FromSessionId = ParseGuid(fromSessionId),
            ToAgent = toAgent,
            Task = task,
            ContextRefs = refs,
            GitBranch = gitBranch,
            CommitSha = commitSha
        };

        try
        {
            var handoff = await _handoffService.CreateHandoffAsync(userId, profileId, input);
            return JsonSerializer.Serialize(handoff, JsonOptions);
        }
        catch (Exception ex)
        {
            return ErrorJson(ex.Message);
        }
    }

    // ===== memory_get_handoff =====

    [McpServerTool(Name = "memory_get_handoff")]
    [Description("Retrieve handoffs available to the calling agent. Point-to-point: matches handoffs addressed to the caller's agent type or broadcast (to_agent=null). Defaults to status='open'.")]
    public async Task<string> GetHandoffAsync(
        string? projectId = null,
        string? toAgent = null,
        string? status = null,
        int? limit = null)
    {
        var userId = GetMcpUserId();
        var profileId = GetMcpAgentProfileId();

        var input = new GetHandoffsInput
        {
            ProjectId = ParseGuid(projectId),
            ToAgent = toAgent,
            Status = status ?? "open",
            Limit = limit ?? 20
        };

        var handoffs = await _handoffService.GetHandoffsAsync(userId, profileId, input);
        return JsonSerializer.Serialize(handoffs, JsonOptions);
    }

    // ===== memory_accept_handoff =====

    [McpServerTool(Name = "memory_accept_handoff")]
    [Description("Accept (pick up) an open handoff, linking it to the accepting session. Only the targeted agent type may accept a point-to-point handoff.")]
    public async Task<string> AcceptHandoffAsync(
        string handoffId,
        string toSessionId)
    {
        var userId = GetMcpUserId();
        var profileId = GetMcpAgentProfileId();

        try
        {
            var handoff = await _handoffService.AcceptHandoffAsync(
                userId, profileId, ParseGuid(handoffId), ParseGuid(toSessionId));
            return JsonSerializer.Serialize(handoff, JsonOptions);
        }
        catch (Exception ex)
        {
            return ErrorJson(ex.Message);
        }
    }

    // ===== memory_complete_handoff =====

    [McpServerTool(Name = "memory_complete_handoff")]
    [Description("Complete a handoff by writing back the result summary. Transitions the handoff to 'done' status so the originator can continue.")]
    public async Task<string> CompleteHandoffAsync(
        string handoffId,
        string resultSummary)
    {
        var userId = GetMcpUserId();
        var profileId = GetMcpAgentProfileId();

        try
        {
            var handoff = await _handoffService.CompleteHandoffAsync(
                userId, profileId, ParseGuid(handoffId), resultSummary);
            return JsonSerializer.Serialize(handoff, JsonOptions);
        }
        catch (Exception ex)
        {
            return ErrorJson(ex.Message);
        }
    }

    // ===== memory_timeline =====

    [McpServerTool(Name = "memory_timeline")]
    [Description("Retrieve the supersession timeline for a memory item — the chain of decisions that superseded each other over time. Useful for understanding how a topic evolved.")]
    public async Task<string> TimelineAsync(string memoryItemId)
    {
        var targetId = ParseGuid(memoryItemId);
        if (targetId == Guid.Empty)
        {
            return ErrorJson("Invalid memory item ID.");
        }

        var item = await _db.AgentMemoryItems
            .FirstOrDefaultAsync(i => i.Id == targetId);
        if (item == null)
        {
            return ErrorJson($"Memory item {memoryItemId} not found.");
        }

        // Walk the supersede chain: find the root (oldest), then follow forward
        var timeline = new List<object>();

        // Find root: walk back via SupersedesId
        var current = item;
        var visited = new HashSet<Guid>();
        while (current.SupersedesId.HasValue && visited.Add(current.Id))
        {
            var older = await _db.AgentMemoryItems
                .FirstOrDefaultAsync(i => i.Id == current.SupersedesId.Value);
            if (older == null) break;
            current = older;
        }

        // Now walk forward from root via SupersededById
        visited.Clear();
        current = (current.SupersedesId == null) ? current : item;
        // Re-find the root properly
        var rootId = current.Id;
        while (current.SupersedesId.HasValue)
        {
            var older = await _db.AgentMemoryItems
                .FirstOrDefaultAsync(i => i.Id == current.SupersedesId.Value);
            if (older == null) break;
            current = older;
            rootId = current.Id;
        }

        // Walk forward
        current = await _db.AgentMemoryItems.FirstOrDefaultAsync(i => i.Id == rootId);
        while (current != null && visited.Add(current.Id))
        {
            var contentPreview = current.Content ?? string.Empty;
            timeline.Add(new
            {
                id = current.Id,
                title = current.Title,
                content = contentPreview.Length > 200
                    ? contentPreview[..200] + "..." : contentPreview,
                status = current.Status.ToString().ToLowerInvariant(),
                created_at = current.CreatedAt,
                superseded_by = current.SupersededById
            });

            if (!current.SupersededById.HasValue) break;
            current = await _db.AgentMemoryItems
                .FirstOrDefaultAsync(i => i.Id == current.SupersededById.Value);
        }

        return JsonSerializer.Serialize(new { timeline }, JsonOptions);
    }

    // ===== memory_supersede =====

    [McpServerTool(Name = "memory_supersede")]
    [Description("Mark a new memory item as superseding an older one. The old item is marked 'superseded' (not deleted) and its content is retained for historical traceability. Both items must be in 'confirmed' state.")]
    public async Task<string> SupersedeAsync(string oldId, string newId)
    {
        var oldGuid = ParseGuid(oldId);
        var newGuid = ParseGuid(newId);

        if (oldGuid == Guid.Empty || newGuid == Guid.Empty)
        {
            return ErrorJson("Both oldId and newId must be valid GUIDs.");
        }

        var oldItem = await _db.AgentMemoryItems.FirstOrDefaultAsync(i => i.Id == oldGuid);
        var newItem = await _db.AgentMemoryItems.FirstOrDefaultAsync(i => i.Id == newGuid);

        if (oldItem == null) return ErrorJson($"Old item {oldId} not found.");
        if (newItem == null) return ErrorJson($"New item {newId} not found.");

        try
        {
            oldItem.Supersede(newGuid);
            newItem.SupersedesId = oldGuid;
            await _db.SaveChangesAsync();

            return JsonSerializer.Serialize(new
            {
                old_item_id = oldGuid,
                new_item_id = newGuid,
                old_status = oldItem.Status.ToString().ToLowerInvariant(),
                message = $"Item {oldId} superseded by {newId}. Old content retained for history."
            }, JsonOptions);
        }
        catch (Exception ex)
        {
            return ErrorJson(ex.Message);
        }
    }

    // ===== Shared helpers =====

    /// <summary>
    /// Resolve the workspace ID for the MCP user. Uses the user's first workspace
    /// (local mode typically has one per user). Returns Guid.Empty if none found.
    /// </summary>
    private async Task<Guid> ResolveWorkspaceIdAsync(Guid userId)
    {
        if (userId == Guid.Empty) return Guid.Empty;

        var workspace = await _db.Workspaces
            .FirstOrDefaultAsync(w => w.UserId == userId);

        return workspace?.Id ?? Guid.Empty;
    }

    private static Guid ParseGuid(string? value)
    {
        if (Guid.TryParse(value, out var result)) return result;
        return Guid.Empty;
    }

    private static string ErrorJson(string message)
    {
        return JsonSerializer.Serialize(new { error = message }, JsonOptions);
    }

    /// <summary>
    /// Gets the MCP user ID from the MEMORIX_MCP_USER_ID environment variable.
    /// Returns Guid.Empty if not set (the services will resolve the first user).
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
    /// Returns null if not set.
    /// </summary>
    private static Guid? GetMcpAgentProfileId()
    {
        var envProfileId = Environment.GetEnvironmentVariable("MEMORIX_AGENT_PROFILE_ID");
        if (Guid.TryParse(envProfileId, out var profileId))
            return profileId;
        return null;
    }
}
