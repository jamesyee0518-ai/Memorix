using System.ComponentModel;
using System.Text.Json;
using KnowledgeEngine.Application.Interfaces;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace KnowledgeEngine.Infrastructure.Mcp;

/// <summary>
/// MCP tool definitions exposed via the official ModelContextProtocol C# SDK v2.0.0
/// (2026-07-28 specification). Each method delegates to <see cref="IAgentToolService"/>
/// for permission-checked business logic.
///
/// Tools are discovered automatically by <c>WithToolsFromAssembly()</c> via the
/// <see cref="McpServerToolType"/> and <see cref="McpServerTool"/> attributes.
/// </summary>
[McpServerToolType]
public class MemorixMcpTools
{
    private readonly IAgentToolService _toolService;
    private readonly ILogger<MemorixMcpTools> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public MemorixMcpTools(IAgentToolService toolService, ILogger<MemorixMcpTools> logger)
    {
        _toolService = toolService;
        _logger = logger;
    }

    // ===== list_topics =====

    [McpServerTool(Name = "list_topics")]
    [Description("List all active knowledge topics for the user, including document and report counts.")]
    public async Task<string> ListTopicsAsync()
    {
        return await InvokeAndSerializeAsync("list_topics", new Dictionary<string, object>());
    }

    // ===== search_memory =====

    [McpServerTool(Name = "search_memory")]
    [Description("Search the knowledge base using hybrid (keyword + vector) search. Returns matching document chunks with snippets and relevance scores.")]
    public async Task<string> SearchMemoryAsync(
        string query,
        string? topicId = null,
        string? searchType = null,
        int? limit = null)
    {
        var input = new Dictionary<string, object> { ["query"] = query };
        if (topicId != null) input["topic_id"] = topicId;
        if (searchType != null) input["search_type"] = searchType;
        if (limit.HasValue) input["limit"] = limit.Value;

        return await InvokeAndSerializeAsync("search_memory", input);
    }

    // ===== ask_memory =====

    [McpServerTool(Name = "ask_memory")]
    [Description("Ask a question against the knowledge base. Uses RAG to retrieve relevant chunks and generate an answer with citations.")]
    public async Task<string> AskMemoryAsync(
        string question,
        string? topicId = null)
    {
        var input = new Dictionary<string, object> { ["question"] = question };
        if (topicId != null) input["topic_id"] = topicId;

        return await InvokeAndSerializeAsync("ask_memory", input);
    }

    // ===== get_document =====

    [McpServerTool(Name = "get_document")]
    [Description("Get full details of a specific document by ID, including summary, key points, signals, and content.")]
    public async Task<string> GetDocumentAsync(string documentId)
    {
        var input = new Dictionary<string, object> { ["document_id"] = documentId };

        return await InvokeAndSerializeAsync("get_document", input);
    }

    // ===== get_report =====

    [McpServerTool(Name = "get_report")]
    [Description("Get report details by ID, or list completed reports filtered by topic and type.")]
    public async Task<string> GetReportAsync(
        string? reportId = null,
        string? topicId = null,
        string? reportType = null)
    {
        var input = new Dictionary<string, object>();
        if (reportId != null) input["report_id"] = reportId;
        if (topicId != null) input["topic_id"] = topicId;
        if (reportType != null) input["report_type"] = reportType;

        return await InvokeAndSerializeAsync("get_report", input);
    }

    // ===== create_inbox_item =====

    [McpServerTool(Name = "create_inbox_item")]
    [Description("Add a URL or text content to the knowledge base Inbox for automatic processing and import.")]
    public async Task<string> CreateInboxItemAsync(
        string sourceType,
        string? sourceUrl = null,
        string? content = null,
        string? title = null,
        string? topicId = null)
    {
        var input = new Dictionary<string, object> { ["source_type"] = sourceType };
        if (sourceUrl != null) input["source_url"] = sourceUrl;
        if (content != null) input["content"] = content;
        if (title != null) input["title"] = title;
        if (topicId != null) input["topic_id"] = topicId;

        return await InvokeAndSerializeAsync("create_inbox_item", input);
    }

    // ===== import_url =====

    [McpServerTool(Name = "import_url")]
    [Description("Trigger a URL import flow — the system will fetch the web page content and import it into the knowledge base.")]
    public async Task<string> ImportUrlAsync(
        string url,
        string? topicId = null,
        string? title = null)
    {
        var input = new Dictionary<string, object> { ["url"] = url };
        if (topicId != null) input["topic_id"] = topicId;
        if (title != null) input["title"] = title;

        return await InvokeAndSerializeAsync("import_url", input);
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
