namespace KnowledgeEngine.Domain.Entities;

public class AgentProfile
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    // 权限配置
    public string? AllowedToolNames { get; set; } // JSONB: ["list_topics","search_memory","ask_memory","get_document","get_report"]
    public string? AllowedTopicIds { get; set; }  // JSONB: Guid[]
    public bool AllowSensitiveDocuments { get; set; } = false;
    public int MaxResultsPerCall { get; set; } = 20;
    public int RateLimitPerMinute { get; set; } = 60;
    public int DailyQuota { get; set; } = 1000;

    // Phase 7: Scopes (JSON array, e.g. ["workspace:read","search:read","rag:read"])
    public string? Scopes { get; set; }

    // 关联 API Key
    public Guid? ApiKeyId { get; set; }

    // MCP 配置
    public string Transport { get; set; } = "stdio"; // stdio | http
    public string? McpServerPath { get; set; }

    public string Status { get; set; } = "active"; // active | disabled
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
