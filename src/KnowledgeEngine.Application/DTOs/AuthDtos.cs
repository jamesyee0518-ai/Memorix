namespace KnowledgeEngine.Application.DTOs;

public class RegisterRequest
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string? Nickname { get; set; }
}

public class RegisterResponse
{
    public Guid UserId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string? Nickname { get; set; }
    public string Token { get; set; } = string.Empty;
}

public class LoginRequest
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public class LoginResponse
{
    public Guid UserId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string? Nickname { get; set; }
    public string? AvatarUrl { get; set; }
    public string PlanCode { get; set; } = "free";
    public string Token { get; set; } = string.Empty;
}

public class UserInfoResponse
{
    public Guid UserId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string? Nickname { get; set; }
    public string? AvatarUrl { get; set; }
    public string PlanCode { get; set; } = "free";
    public string Status { get; set; } = "active";
    public string Timezone { get; set; } = "Asia/Shanghai";
    public DateTime? LastLoginAt { get; set; }
    public DateTime CreatedAt { get; set; }
}
