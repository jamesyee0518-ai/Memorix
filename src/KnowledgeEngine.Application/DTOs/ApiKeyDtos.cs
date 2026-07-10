using System.Text.Json.Serialization;

namespace KnowledgeEngine.Application.DTOs;

// ===== API Key Requests =====

public class CreateApiKeyRequest
{
    public string Name { get; set; } = string.Empty;
    public string PermissionScope { get; set; } = "full_read";
    public List<Guid>? AllowedTopicIds { get; set; }
    public List<string>? AllowedActions { get; set; }
    public int? RateLimitPerMinute { get; set; }
    public int? DailyQuota { get; set; }
    public DateTime? ExpiresAt { get; set; }
}

// ===== API Key Responses =====

public class CreateApiKeyResponse
{
    public Guid Id { get; set; }

    [JsonPropertyName("api_key")]
    public string ApiKey { get; set; } = string.Empty;

    public string KeyPrefix { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string PermissionScope { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = "API Key created. Save it now - it won't be shown again.";
}

public class ApiKeyListItem
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string KeyPrefix { get; set; } = string.Empty;
    public string PermissionScope { get; set; } = string.Empty;
    public List<Guid>? AllowedTopicIds { get; set; }
    public List<string>? AllowedActions { get; set; }
    public int RateLimitPerMinute { get; set; }
    public int DailyQuota { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? LastUsedAt { get; set; }
}
