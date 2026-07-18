namespace KnowledgeEngine.Domain.Entities;

public class PushNotification
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public string ClientId { get; set; } = string.Empty;
    public string PushToken { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string? DataJson { get; set; }
    public string Status { get; set; } = "pending";
    public int Attempt { get; set; }
    public int MaxAttempts { get; set; } = 3;
    public string? ProviderResponse { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime? NextAttemptAt { get; set; }
    public DateTime? SentAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
