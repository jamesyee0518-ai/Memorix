namespace KnowledgeEngine.Domain.Entities;

public class DeviceIdentity
{
    public Guid Id { get; set; }
    public Guid InstallationId { get; set; }
    public string DeviceKey { get; set; } = string.Empty;
    public string PublicKey { get; set; } = string.Empty;
    public string PrivateKeyRef { get; set; } = string.Empty;
    public string KeyAlgorithm { get; set; } = "ed25519";
    public string Status { get; set; } = "active";
    public DateTime? LastSeenAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
