namespace KnowledgeEngine.Domain.Entities;

public class LocalInstallation
{
    public Guid Id { get; set; }
    public string InstallationKey { get; set; } = string.Empty;
    public string Platform { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public string AppVersion { get; set; } = string.Empty;
    public string Status { get; set; } = "active";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
