namespace KnowledgeEngine.Domain.Entities;

public class CloudAccountBinding
{
    public Guid Id { get; set; }
    public Guid LocalProfileId { get; set; }
    public string CloudUserId { get; set; } = string.Empty;
    public string CloudApiBaseUrl { get; set; } = string.Empty;
    public string? AccountDisplayName { get; set; }
    public string? AccountEmailMasked { get; set; }
    public string TokenKeyRef { get; set; } = string.Empty;
    public string BindingStatus { get; set; } = "active";
    public DateTime? LastAuthenticatedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
