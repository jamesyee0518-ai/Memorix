using System.Data;
using System.Text.Json;
using KnowledgeEngine.Application.Exceptions;
using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Application.Settings;
using KnowledgeEngine.Domain.Entities;
using KnowledgeEngine.Infrastructure.Db;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace KnowledgeEngine.Infrastructure.Services;

public sealed class BillingMaintenanceService : IBillingMaintenanceService
{
    private readonly AppDbContext _db;
    private readonly BillingSettings _settings;

    public BillingMaintenanceService(
        AppDbContext db,
        IOptions<BillingSettings> settings)
    {
        _db = db;
        _settings = settings.Value;
    }

    public async Task<int> ReleaseExpiredReservationsAsync(
        DateTime? asOf = null,
        CancellationToken ct = default)
    {
        var now = asOf ?? DateTime.UtcNow;
        await using var transaction = await _db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            ct);
        var reservations = await _db.BalanceReservations
            .Where(x =>
                x.Status == ReservationStatuses.Active &&
                x.ExpiresAt <= now)
            .OrderBy(x => x.ExpiresAt)
            .Take(200)
            .ToListAsync(ct);

        foreach (var reservation in reservations)
        {
            var allocations =
                JsonSerializer.Deserialize<Dictionary<Guid, decimal>>(reservation.AllocationJson) ?? [];
            var bucketIds = allocations.Keys.ToList();
            var buckets = await _db.QuotaBuckets
                .Where(x => bucketIds.Contains(x.Id))
                .ToListAsync(ct);
            foreach (var allocation in allocations)
            {
                var bucket = buckets.SingleOrDefault(x => x.Id == allocation.Key)
                    ?? throw new AppException(
                        "quota_bucket_missing",
                        "A quota bucket referenced by an expired reservation no longer exists.");
                bucket.ReservedCredits = Math.Max(0m, bucket.ReservedCredits - allocation.Value);
                bucket.Version++;
                bucket.UpdatedAt = now;
            }

            reservation.Status = ReservationStatuses.Expired;
            reservation.UpdatedAt = now;
            _db.AccountLedger.Add(new AccountLedger
            {
                Id = Guid.CreateVersion7(),
                BillingAccountId = reservation.BillingAccountId,
                BusinessType = "RESERVATION",
                BusinessId = reservation.Id,
                Action = LedgerActions.Expire,
                Sequence = 2,
                Credits = reservation.ReservedCredits,
                Currency = _settings.Currency.Trim().ToUpperInvariant(),
                IdempotencyKey = $"{reservation.Id}:expire",
                CreatedAt = now
            });

            var job = await _db.AiJobs.SingleOrDefaultAsync(x => x.Id == reservation.JobId, ct);
            if (job != null && !job.FinishedAt.HasValue)
            {
                job.Status = AiJobStatuses.Failed;
                job.ErrorMessage = "billing_reservation_expired";
                job.FinishedAt = now;
            }
        }

        await _db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return reservations.Count;
    }
}
