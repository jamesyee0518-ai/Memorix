using KnowledgeEngine.Application.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace KnowledgeEngine.Infrastructure.Processing;

public class DocumentProcessingBackgroundService : BackgroundService
{
    private static readonly TimeSpan PollingInterval = TimeSpan.FromSeconds(30);
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DocumentProcessingBackgroundService> _logger;

    public DocumentProcessingBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<DocumentProcessingBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("DocumentProcessingBackgroundService started. Polling every {Interval}s.",
            PollingInterval.TotalSeconds);

        // Wait a bit at startup to let the application initialize
        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PollAndProcessAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in DocumentProcessingBackgroundService polling cycle");
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

        _logger.LogInformation("DocumentProcessingBackgroundService stopped.");
    }

    private async Task PollAndProcessAsync(CancellationToken ct)
    {
        List<(Guid SourceId, Guid UserId)> queuedSources;

        using (var scope = _scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();

            // Find sources with status = "queued"
            var sources = await db.Sources
                .Where(s => s.Status == "queued")
                .Select(s => new { s.Id, s.UserId })
                .ToListAsync(ct);

            queuedSources = sources
                .Select(s => (s.Id, s.UserId))
                .ToList();
        }

        if (queuedSources.Count == 0)
        {
            return;
        }

        _logger.LogInformation("Found {Count} queued source(s) to process", queuedSources.Count);

        foreach (var (sourceId, userId) in queuedSources)
        {
            if (ct.IsCancellationRequested) break;

            using var scope = _scopeFactory.CreateScope();
            var pipeline = scope.ServiceProvider.GetRequiredService<IDocumentPipeline>();

            try
            {
                _logger.LogInformation("Processing source {SourceId} for user {UserId}", sourceId, userId);
                await pipeline.ProcessSourceAsync(sourceId, userId, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process source {SourceId}", sourceId);
            }
        }
    }
}
