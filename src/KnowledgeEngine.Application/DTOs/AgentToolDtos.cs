namespace KnowledgeEngine.Application.DTOs;

/// <summary>
/// Represents a tool definition exposed to MCP clients and AI agents.
/// </summary>
public class AgentToolDefinition
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// JSON Schema object describing the tool's input parameters.
    /// </summary>
    public Dictionary<string, object> InputSchema { get; set; } = new();
}

/// <summary>
/// Result of invoking an agent tool.
/// </summary>
public class AgentToolResult
{
    public bool Success { get; set; }
    public object? Data { get; set; }
    public string? Error { get; set; }
    public int ResultCount { get; set; }
    public int? PromptTokens { get; set; }
    public int? CompletionTokens { get; set; }
}
