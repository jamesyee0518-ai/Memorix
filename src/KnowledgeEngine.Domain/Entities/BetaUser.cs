namespace KnowledgeEngine.Domain.Entities;

public class BetaUser
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
