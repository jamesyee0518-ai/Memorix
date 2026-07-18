using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Application.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace KnowledgeEngine.Infrastructure.Processing;

public class MediaProcessingWorker : BackgroundService
{
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan PollingInterval = TimeSpan.FromSeconds(45);
    private const int MaxItemsPerCycle = 3;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<MediaProcessingWorker> _logger;

    public MediaProcessingWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<MediaProcessingWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("MediaProcessingWorker started. Polling every {Seconds}s.",
            PollingInterval.TotalSeconds);

        await Task.Delay(StartupDelay, stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PollAndProcessAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in MediaProcessingWorker polling cycle");
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

        _logger.LogInformation("MediaProcessingWorker stopped.");
    }

    private async Task PollAndProcessAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var config = scope.ServiceProvider.GetRequiredService<IConfigService>();
        var repo = scope.ServiceProvider.GetRequiredService<IKnowledgeRepository>();
        var mediaProcessing = scope.ServiceProvider.GetRequiredService<MediaProcessingService>();

        var workspaceId = await config.GetCurrentWorkspaceIdAsync(ct);
        if (string.IsNullOrWhiteSpace(workspaceId))
        {
            return;
        }

        var images = await repo.ListInboxItemsAsync(
            workspaceId, status: "pending", inputType: "image", limit: MaxItemsPerCycle, offset: 0, ct: ct);
        var remaining = MaxItemsPerCycle - images.Count;
        var audio = remaining > 0
            ? await repo.ListInboxItemsAsync(
                workspaceId, status: "pending", inputType: "audio", limit: remaining, offset: 0, ct: ct)
            : new();

        var items = images.Concat(audio).ToList();
        if (items.Count == 0)
        {
            return;
        }

        _logger.LogInformation("Found {Count} pending media inbox item(s).", items.Count);
        foreach (var item in items)
        {
            if (ct.IsCancellationRequested) break;

            try
            {
                await mediaProcessing.ProcessAndImportAsync(item.Id, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Media processing failed for inbox item {InboxItemId}", item.Id);
            }
        }
    }
}
