using System.Text.Json.Serialization;

namespace KnowledgeEngine.Application.DTOs;

// ===== BetaUser Requests =====

public class InviteBetaUserRequest
{
    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;

    [JsonPropertyName("display_name")]
    public string? DisplayName { get; set; }

    [JsonPropertyName("beta_group")]
    public string? BetaGroup { get; set; }

    [JsonPropertyName("platform")]
    public string? Platform { get; set; }
}

public class UpdateBetaUserRequest
{
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("notes")]
    public string? Notes { get; set; }

    [JsonPropertyName("beta_group")]
    public string? BetaGroup { get; set; }
}

// ===== BetaUser Responses =====

public class BetaUserResponse
{
    public Guid Id { get; set; }
    public Guid? UserId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string? Name { get; set; }
    public string? UserType { get; set; }
    public string? InviteCode { get; set; }
    public string? BetaGroup { get; set; }
    public string? Platform { get; set; }
    public string Status { get; set; } = "invited";
    public DateTime? OnboardedAt { get; set; }
    public DateTime? LastFeedbackAt { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class BetaUserListItem
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string? Name { get; set; }
    public string? BetaGroup { get; set; }
    public string Status { get; set; } = "invited";
    public DateTime? OnboardedAt { get; set; }
    public DateTime? LastFeedbackAt { get; set; }
    public DateTime CreatedAt { get; set; }
}
