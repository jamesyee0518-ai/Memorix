namespace KnowledgeEngine.Domain.Entities;

public class ApiKey
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }

    public string Name { get; set; } = string.Empty;
    public string KeyPrefix { get; set; } = string.Empty;
    public string KeyHash { get; set; } = string.Empty;

    public string PermissionScope { get; set; } = "full_read";
    public string? AllowedTopicIds { get; set; } // JSONB
    public string? AllowedActions { get; set; }  // JSONB

    public int RateLimitPerMinute { get; set; } = 60;
    public int DailyQuota { get; set; } = 1000;

    public DateTime? ExpiresAt { get; set; }
    public string Status { get; set; } = "active";

    public DateTime CreatedAt { get; set; }
    public DateTime? LastUsedAt { get; set; }
}
