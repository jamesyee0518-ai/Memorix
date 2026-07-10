namespace KnowledgeEngine.Domain.Entities;

public class MobileDevice
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public string ClientId { get; set; } = string.Empty;
    public string? DeviceName { get; set; }
    public string? Platform { get; set; }
    public string? PushToken { get; set; }
    public string? RefreshTokenHash { get; set; }
    public DateTime? RefreshTokenExpiresAt { get; set; }
    public string Status { get; set; } = "active";
    public DateTime? LastSeenAt { get; set; }
    public DateTime BoundAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
