using System.Net.Http.Json;
using System.Text.Json;
using KnowledgeEngine.Application.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace KnowledgeEngine.Infrastructure.Notifications;

public class PushNotificationWorker : BackgroundService
{
    private const string ExpoPushEndpoint = "https://exp.host/--/api/v2/push/send";
    private static readonly TimeSpan PollingInterval = TimeSpan.FromSeconds(15);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<PushNotificationWorker> _logger;

    public PushNotificationWorker(
        IServiceScopeFactory scopeFactory,
        IHttpClientFactory httpClientFactory,
        ILogger<PushNotificationWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("PushNotificationWorker started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessPendingAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in PushNotificationWorker polling cycle");
            }

            try
            {
                await Task.Delay(PollingInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("PushNotificationWorker stopped.");
    }

    private async Task ProcessPendingAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IKnowledgeRepository>();
        var notifications = await repo.ListPendingPushNotificationsAsync(20, ct);
        if (notifications.Count == 0)
        {
            return;
        }

        var client = _httpClientFactory.CreateClient("ExpoPush");
        foreach (var notification in notifications)
        {
            try
            {
                var responseText = await SendOneAsync(client, notification, ct);
                await repo.MarkPushNotificationSentAsync(notification.Id, responseText, ct);
            }
            catch (Exception ex)
            {
                var nextAttempt = CalculateNextAttempt(notification);
                await repo.MarkPushNotificationFailedAsync(notification.Id, ex.Message, nextAttempt, ct);
                _logger.LogWarning(ex, "Push notification {NotificationId} failed", notification.Id);
            }
        }
    }

    private static async Task<string> SendOneAsync(HttpClient client, PushNotificationDto notification, CancellationToken ct)
    {
        var payload = new ExpoPushMessage
        {
            To = notification.PushToken,
            Title = notification.Title,
            Body = notification.Body,
            Data = ParseData(notification.DataJson)
        };

        using var response = await client.PostAsJsonAsync(ExpoPushEndpoint, payload, JsonOptions, ct);
        var responseText = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Expo push failed: {(int)response.StatusCode} {responseText}");
        }

        return responseText;
    }

    private static DateTime? CalculateNextAttempt(PushNotificationDto notification)
    {
        var nextAttempt = notification.Attempt + 1;
        if (nextAttempt >= notification.MaxAttempts)
        {
            return null;
        }

        var delaySeconds = Math.Min(300, Math.Pow(2, nextAttempt) * 15);
        return DateTime.UtcNow.AddSeconds(delaySeconds);
    }

    private static Dictionary<string, string> ParseData(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new Dictionary<string, string>();
        }

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonOptions)
                ?? new Dictionary<string, string>();
        }
        catch
        {
            return new Dictionary<string, string>();
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private sealed class ExpoPushMessage
    {
        public string To { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Body { get; set; } = string.Empty;
        public string Sound { get; set; } = "default";
        public Dictionary<string, string> Data { get; set; } = new();
    }
}
