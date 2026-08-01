using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Domain.Entities;
using KnowledgeEngine.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KnowledgeEngine.Infrastructure.Audio;

/// <summary>
/// Standalone service for recording and querying audio provider usage metrics.
/// Each provider invocation is persisted as a <see cref="ProviderUsageRecord"/> for
/// billing, audit, and cost analysis. Cost computation is handled by the static
/// <see cref="ComputeCost"/> helper based on the provider's pricing unit.
/// </summary>
public class ProviderUsageMeteringService
{
    private readonly IAppDbContext _db;
    private readonly ILogger<ProviderUsageMeteringService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProviderUsageMeteringService"/> class.
    /// </summary>
    /// <param name="db">The application database context for usage record persistence.</param>
    /// <param name="logger">The logger for diagnostic output.</param>
    public ProviderUsageMeteringService(
        IAppDbContext db,
        ILogger<ProviderUsageMeteringService> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// Records a single provider usage entry to the database.
    /// </summary>
    /// <param name="record">The usage record to persist. <see cref="ProviderUsageRecord.Id"/> and
    /// <see cref="ProviderUsageRecord.CreatedAt"/> are set automatically if empty.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task RecordUsageAsync(ProviderUsageRecord record, CancellationToken ct)
    {
        if (record.Id == Guid.Empty)
        {
            record.Id = Guid.NewGuid();
        }

        if (record.CreatedAt == default)
        {
            record.CreatedAt = DateTime.UtcNow;
        }

        _db.ProviderUsageRecords.Add(record);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Recorded usage {RecordId}: user={UserId}, capability={Capability}, " +
            "provider={ProviderId}, duration={DurationMs}ms, cost={EstimatedCost}",
            record.Id, record.UserId, record.Capability,
            record.ProviderId, record.DurationMs, record.EstimatedCost);
    }

    /// <summary>
    /// Retrieves all usage records for a user within the specified time range.
    /// Results are ordered by creation time ascending.
    /// </summary>
    /// <param name="userId">The user ID to filter by.</param>
    /// <param name="from">The start of the time range (inclusive).</param>
    /// <param name="to">The end of the time range (inclusive).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A list of usage records for the given user and time range.</returns>
    public async Task<List<ProviderUsageRecord>> GetUsageAsync(
        Guid userId, DateTime from, DateTime to, CancellationToken ct)
    {
        return await _db.ProviderUsageRecords
            .Where(r => r.UserId == userId && r.CreatedAt >= from && r.CreatedAt <= to)
            .OrderBy(r => r.CreatedAt)
            .ToListAsync(ct);
    }

    /// <summary>
    /// Computes the total cost across all usage records for a user within the specified time range.
    /// Uses <see cref="ProviderUsageRecord.ActualCost"/> when available, falling back to
    /// <see cref="ProviderUsageRecord.EstimatedCost"/>.
    /// </summary>
    /// <param name="userId">The user ID to filter by.</param>
    /// <param name="from">The start of the time range (inclusive).</param>
    /// <param name="to">The end of the time range (inclusive).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The total cost as a decimal.</returns>
    public async Task<decimal> GetTotalCostAsync(
        Guid userId, DateTime from, DateTime to, CancellationToken ct)
    {
        var records = await _db.ProviderUsageRecords
            .Where(r => r.UserId == userId && r.CreatedAt >= from && r.CreatedAt <= to)
            .Select(r => new { r.ActualCost, r.EstimatedCost })
            .ToListAsync(ct);

        return records.Sum(r => r.ActualCost ?? r.EstimatedCost ?? 0m);
    }

    /// <summary>
    /// Computes the cost for a single provider invocation based on the pricing unit,
    /// audio duration, and rate per unit.
    /// </summary>
    /// <param name="pricingUnit">The pricing unit type (REQUEST, SECOND, MINUTE, TOKEN).</param>
    /// <param name="durationMs">The audio duration in milliseconds.</param>
    /// <param name="ratePerUnit">The cost rate per unit.</param>
    /// <returns>The computed cost as a decimal.</returns>
    public static decimal ComputeCost(string pricingUnit, long durationMs, decimal ratePerUnit)
    {
        return pricingUnit switch
        {
            // Per-request pricing: flat rate regardless of duration.
            PricingUnits.Request => ratePerUnit,

            // Per-second pricing: convert milliseconds to seconds.
            PricingUnits.Second => (durationMs / 1000m) * ratePerUnit,

            // Per-minute pricing: convert milliseconds to minutes.
            PricingUnits.Minute => (durationMs / 60000m) * ratePerUnit,

            // Per-token pricing: ratePerUnit is the cost per token.
            // Duration-based cost is not applicable; return the rate as-is.
            PricingUnits.Token => ratePerUnit,

            // Unknown pricing unit: default to flat rate.
            _ => ratePerUnit,
        };
    }
}
