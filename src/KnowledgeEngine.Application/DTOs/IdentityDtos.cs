namespace KnowledgeEngine.Application.DTOs;

public class LocalIdentityDto
{
    public Guid InstallationId { get; set; }
    public Guid LocalProfileId { get; set; }
    public Guid DeviceId { get; set; }
    public string InstallationKey { get; set; } = string.Empty;
    public string DeviceKey { get; set; } = string.Empty;
    public string Platform { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
}
