using System.Text.Json;
using System.Text.Json.Nodes;
using KnowledgeEngine.Application.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace KnowledgeEngine.Infrastructure.Mcp;

/// <summary>
/// MCP (Model Context Protocol) Server that communicates via stdio using JSON-RPC 2.0.
/// Exposes agent tools (list_topics, search_memory, ask_memory, get_document, get_report)
/// to AI clients through the MCP protocol.
/// </summary>
public class McpServer
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<McpServer> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public McpServer(IServiceProvider serviceProvider, ILogger<McpServer> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    /// <summary>
    /// Runs the MCP server loop, reading JSON-RPC requests from stdin and writing
    /// responses to stdout. One JSON message per line.
    /// </summary>
    public async Task RunAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("MCP Server started, waiting for JSON-RPC requests on stdin...");

        while (!ct.IsCancellationRequested)
        {
            string? line = null;
            JsonNode? request = null;

            try
            {
                line = await Console.In.ReadLineAsync(ct);
                if (line == null)
                    break; // EOF — client disconnected

                if (string.IsNullOrWhiteSpace(line))
                    continue;

                request = JsonNode.Parse(line);
                if (request == null)
                    continue;

                var method = request["method"]?.GetValue<string>();
                var id = request["id"];

                // Skip notifications (JSON-RPC requests without an id)
                if (id == null)
                    continue;

                var response = await HandleRequestAsync(method, request, ct);
                if (response != null)
                {
                    response["id"] = id;
                    Console.WriteLine(response.ToJsonString(JsonOptions));
                    Console.Out.Flush();
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MCP Server error processing request: {Line}", line);

                // Return a JSON-RPC error response if we have the request id
                var errorId = request?["id"];
                if (errorId != null)
                {
                    var errorResponse = new JsonObject
                    {
                        ["jsonrpc"] = "2.0",
                        ["id"] = errorId.DeepClone(),
                        ["error"] = new JsonObject
                        {
                            ["code"] = -32603,
                            ["message"] = ex.Message
                        }
                    };
                    Console.WriteLine(errorResponse.ToJsonString(JsonOptions));
                    Console.Out.Flush();
                }
            }
        }

        _logger.LogInformation("MCP Server shutting down.");
    }

    private async Task<JsonObject?> HandleRequestAsync(string? method, JsonNode request, CancellationToken ct)
    {
        return method switch
        {
            "initialize" => HandleInitialize(),
            "tools/list" => await HandleToolsListAsync(ct),
            "tools/call" => await HandleToolsCallAsync(request, ct),
            _ => CreateErrorResponse(-32601, $"Method not found: {method}")
        };
    }

    private JsonObject HandleInitialize()
    {
        return new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["result"] = new JsonObject
            {
                ["protocolVersion"] = "2024-11-05",
                ["serverInfo"] = new JsonObject
                {
                    ["name"] = "memorix-knowledge-engine",
                    ["version"] = "1.0.0"
                },
                ["capabilities"] = new JsonObject
                {
                    ["tools"] = new JsonObject()
                }
            }
        };
    }

    private async Task<JsonObject> HandleToolsListAsync(CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var toolService = scope.ServiceProvider.GetRequiredService<IAgentToolService>();

        var agentProfileId = GetMcpAgentProfileId();
        var tools = await toolService.ListToolsAsync(agentProfileId, ct);

        var toolsArray = new JsonArray();
        foreach (var tool in tools)
        {
            var toolObj = new JsonObject
            {
                ["name"] = tool.Name,
                ["description"] = tool.Description,
                ["inputSchema"] = JsonSerializer.SerializeToNode(tool.InputSchema, JsonOptions)
            };
            toolsArray.Add(toolObj);
        }

        return new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["result"] = new JsonObject
            {
                ["tools"] = toolsArray
            }
        };
    }

    private async Task<JsonObject> HandleToolsCallAsync(JsonNode request, CancellationToken ct)
    {
        var toolName = request["params"]?["name"]?.GetValue<string>();
        if (string.IsNullOrEmpty(toolName))
            return CreateErrorResponse(-32602, "Missing tool name in params.name");

        // Parse arguments into a Dictionary<string, object>
        var input = ParseArguments(request["params"]?["arguments"]);

        using var scope = _serviceProvider.CreateScope();
        var toolService = scope.ServiceProvider.GetRequiredService<IAgentToolService>();
        var userId = GetMcpUserId();
        var agentProfileId = GetMcpAgentProfileId();

        var result = await toolService.InvokeToolAsync(userId, toolName, input, agentProfileId, ct);

        // Build the MCP content array (text content)
        var content = new JsonArray();
        var textPayload = result.Success
            ? JsonSerializer.Serialize(result.Data, JsonOptions)
            : JsonSerializer.Serialize(new { error = result.Error }, JsonOptions);

        content.Add(new JsonObject
        {
            ["type"] = "text",
            ["text"] = textPayload
        });

        return new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["result"] = new JsonObject
            {
                ["content"] = content,
                ["isError"] = !result.Success
            }
        };
    }

    /// <summary>
    /// Parses the JSON-RPC arguments node into a dictionary of .NET primitives.
    /// Handles string, number, boolean, and null JSON values.
    /// </summary>
    private static Dictionary<string, object> ParseArguments(JsonNode? argumentsNode)
    {
        var input = new Dictionary<string, object>();
        if (argumentsNode == null)
            return input;

        var argsJson = argumentsNode.ToJsonString();
        using var doc = JsonDocument.Parse(argsJson);

        if (doc.RootElement.ValueKind != JsonValueKind.Object)
            return input;

        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            input[prop.Name] = prop.Value.ValueKind switch
            {
                JsonValueKind.String => (object)(prop.Value.GetString() ?? string.Empty),
                JsonValueKind.Number => prop.Value.TryGetInt32(out var i) ? (object)i : (object)prop.Value.GetDouble(),
                JsonValueKind.True => (object)true,
                JsonValueKind.False => (object)false,
                _ => (object)(prop.Value.GetRawText())
            };
        }

        return input;
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

    private static JsonObject CreateErrorResponse(int code, string message)
    {
        return new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["error"] = new JsonObject
            {
                ["code"] = code,
                ["message"] = message
            }
        };
    }
}
