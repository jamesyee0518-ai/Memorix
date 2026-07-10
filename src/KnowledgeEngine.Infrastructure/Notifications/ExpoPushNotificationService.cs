using System.Text.Json;
using KnowledgeEngine.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace KnowledgeEngine.Infrastructure.Notifications;

public class ExpoPushNotificationService : IPushNotificationService
{
    private readonly IKnowledgeRepository _repo;
    private readonly ILogger<ExpoPushNotificationService> _logger;

    public ExpoPushNotificationService(
        IKnowledgeRepository repo,
        ILogger<ExpoPushNotificationService> logger)
    {
        _repo = repo;
        _logger = logger;
    }

    public async Task SendToDeviceAsync(
        string workspaceId,
        string? clientId,
        string title,
        string body,
        Dictionary<string, string>? data = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(clientId))
        {
            return;
        }

        try
        {
            var device = await _repo.GetMobileDeviceAsync(workspaceId, clientId, ct);
            if (device == null ||
                !string.Equals(device.Status, "active", StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(device.PushToken))
            {
                return;
            }

            var notification = await _repo.CreatePushNotificationAsync(new CreatePushNotificationInput
            {
                WorkspaceId = workspaceId,
                ClientId = device.ClientId,
                PushToken = device.PushToken,
                Title = title,
                Body = body,
                DataJson = data == null ? null : JsonSerializer.Serialize(data, JsonOptions),
                MaxAttempts = 3
            }, ct);

            _logger.LogInformation("Queued push notification {NotificationId} for device {ClientId}", notification.Id, clientId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to queue push notification to device {ClientId}", clientId);
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

}
