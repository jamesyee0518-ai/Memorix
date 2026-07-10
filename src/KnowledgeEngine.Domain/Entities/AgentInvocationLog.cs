namespace KnowledgeEngine.Domain.Entities;

public class AgentInvocationLog
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid? AgentProfileId { get; set; }
    public Guid? ApiKeyId { get; set; }

    public string Transport { get; set; } = "cloud_api"; // mcp_stdio | cloud_api
    public string ToolName { get; set; } = string.Empty; // list_topics, search_memory, etc.

    public string? InputJson { get; set; }
    public string? OutputSummary { get; set; }
    public int? ResultCount { get; set; }
    public int? PromptTokens { get; set; }
    public int? CompletionTokens { get; set; }

    public string Status { get; set; } = "success"; // success | failed | denied | rate_limited
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public int LatencyMs { get; set; }

    public string? TraceId { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }

    public DateTime CreatedAt { get; set; }
}
