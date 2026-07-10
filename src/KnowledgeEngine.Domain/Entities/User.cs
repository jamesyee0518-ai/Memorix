namespace KnowledgeEngine.Domain.Entities;

public class User
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string? Nickname { get; set; }
    public string PasswordHash { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
    public string PlanCode { get; set; } = "free";
    public string Status { get; set; } = "active";
    public string Timezone { get; set; } = "Asia/Shanghai";
    public DateTime? LastLoginAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
