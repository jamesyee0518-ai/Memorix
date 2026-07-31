namespace KnowledgeEngine.Application.Interfaces;

public interface IBillingMaintenanceService
{
    Task<int> ReleaseExpiredReservationsAsync(
        DateTime? asOf = null,
        CancellationToken ct = default);
}

