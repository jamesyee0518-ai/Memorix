namespace KnowledgeEngine.Application.Interfaces;

public interface IPushNotificationService
{
    Task SendToDeviceAsync(
        string workspaceId,
        string? clientId,
        string title,
        string body,
        Dictionary<string, string>? data = null,
        CancellationToken ct = default);
}
