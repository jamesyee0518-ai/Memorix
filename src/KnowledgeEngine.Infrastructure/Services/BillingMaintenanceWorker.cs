using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Application.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KnowledgeEngine.Infrastructure.Services;

public sealed class BillingMaintenanceWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly BillingSettings _settings;
    private readonly ILogger<BillingMaintenanceWorker> _logger;

    public BillingMaintenanceWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<BillingSettings> settings,
        ILogger<BillingMaintenanceWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _settings = settings.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(30, _settings.MaintenanceIntervalSeconds));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var released = await scope.ServiceProvider
                    .GetRequiredService<IBillingMaintenanceService>()
                    .ReleaseExpiredReservationsAsync(ct: stoppingToken);
                if (released > 0)
                {
                    _logger.LogInformation(
                        "Released {ReservationCount} expired AI billing reservations",
                        released);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to release expired AI billing reservations");
            }

            await Task.Delay(interval, stoppingToken);
        }
    }
}

