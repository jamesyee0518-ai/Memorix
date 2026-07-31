using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Application.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KnowledgeEngine.Infrastructure.Payments;

public sealed class PaymentRecoveryWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly PaymentSettings _settings;
    private readonly ILogger<PaymentRecoveryWorker> _logger;

    public PaymentRecoveryWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<PaymentSettings> settings,
        ILogger<PaymentRecoveryWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _settings = settings.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(
            Math.Clamp(_settings.RecoveryIntervalSeconds, 10, 3600)));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<IPaymentService>();
                var recovered = await service.RecoverPendingAsync(stoppingToken);
                if (recovered > 0)
                {
                    _logger.LogInformation("Recovered {Count} pending payment orders", recovered);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Payment recovery cycle failed");
            }
        }
    }
}
