using KnowledgeEngine.Application.DTOs;
using KnowledgeEngine.Application.Exceptions;
using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Application.Settings;
using KnowledgeEngine.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace KnowledgeEngine.Infrastructure.Services;

public sealed class BillingPortalService : IBillingPortalService
{
    private static readonly HashSet<string> TokenUsageTypes =
    [
        UsageTypes.InputToken,
        UsageTypes.OutputToken,
        UsageTypes.CacheReadToken,
        UsageTypes.CacheWriteToken,
        UsageTypes.ReasoningToken,
        UsageTypes.EmbeddingToken
    ];

    private readonly IAppDbContext _db;
    private readonly IAiBillingService _billing;
    private readonly BillingSettings _billingSettings;
    private readonly PaymentSettings _paymentSettings;

    public BillingPortalService(
        IAppDbContext db,
        IAiBillingService billing,
        IOptions<BillingSettings> billingSettings,
        IOptions<PaymentSettings> paymentSettings)
    {
        _db = db;
        _billing = billing;
        _billingSettings = billingSettings.Value;
        _paymentSettings = paymentSettings.Value;
    }

    public async Task<BillingOverviewResponse> GetOverviewAsync(
        Guid userId,
        Guid workspaceId,
        CancellationToken ct = default)
    {
        var summary = await _billing.GetSummaryAsync(userId, workspaceId, ct);
        var now = DateTime.UtcNow;
        var monthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var buckets = await _db.QuotaBuckets.AsNoTracking()
            .Where(x =>
                x.BillingAccountId == summary.BillingAccountId &&
                x.EffectiveFrom <= now &&
                (!x.ExpiresAt.HasValue || x.ExpiresAt > now))
            .ToListAsync(ct);
        var monthJobs = await _db.AiJobs.AsNoTracking()
            .Where(x =>
                x.BillingAccountId == summary.BillingAccountId &&
                x.CreatedAt >= monthStart &&
                x.CreatedAt <= now)
            .ToListAsync(ct);
        var monthTokens = await _db.UsageEvents.AsNoTracking()
            .Where(x =>
                x.BillingAccountId == summary.BillingAccountId &&
                x.OccurredAt >= monthStart &&
                x.OccurredAt <= now &&
                TokenUsageTypes.Contains(x.UsageType))
            .SumAsync(x => (decimal?)x.Quantity, ct) ?? 0m;
        var account = await _db.BillingAccounts.AsNoTracking()
            .SingleAsync(x => x.Id == summary.BillingAccountId, ct);

        decimal Available(string source) => buckets
            .Where(x => x.Source == source)
            .Sum(x => Math.Max(0m, x.GrantedCredits - x.ConsumedCredits - x.ReservedCredits));

        return new BillingOverviewResponse(
            summary.BillingAccountId,
            workspaceId,
            account.Name,
            summary.Currency,
            summary.GrantedCredits,
            summary.ConsumedCredits,
            summary.ReservedCredits,
            summary.AvailableCredits,
            Available(QuotaBucketSources.Plan),
            Available(QuotaBucketSources.TopUp),
            Available(QuotaBucketSources.Promotion),
            monthJobs.Sum(x => x.ActualCredits),
            monthJobs.Sum(x => x.ActualAmount),
            monthJobs.Where(x => x.Status is AiJobStatuses.Pending or AiJobStatuses.Reserved or AiJobStatuses.Running)
                .Sum(x => Math.Max(0m, x.EstimatedCredits - x.ActualCredits)),
            monthJobs.Count,
            monthTokens,
            true,
            _paymentSettings.Enabled,
            now);
    }

    public async Task<BillingUsageResponse> GetUsageAsync(
        Guid userId,
        Guid workspaceId,
        DateTime? from,
        DateTime? to,
        CancellationToken ct = default)
    {
        var summary = await _billing.GetSummaryAsync(userId, workspaceId, ct);
        var (rangeFrom, rangeTo) = NormalizeRange(from, to);
        var jobs = await _db.AiJobs.AsNoTracking()
            .Where(x =>
                x.BillingAccountId == summary.BillingAccountId &&
                x.WorkspaceId == workspaceId &&
                x.CreatedAt >= rangeFrom &&
                x.CreatedAt < rangeTo)
            .OrderByDescending(x => x.CreatedAt)
            .Take(500)
            .ToListAsync(ct);
        var jobIds = jobs.Select(x => x.Id).ToArray();
        var usageEvents = jobIds.Length == 0
            ? []
            : await _db.UsageEvents.AsNoTracking()
                .Where(x => jobIds.Contains(x.JobId) && x.OccurredAt >= rangeFrom && x.OccurredAt < rangeTo)
                .ToListAsync(ct);
        var tokensByJob = usageEvents
            .Where(x => TokenUsageTypes.Contains(x.UsageType))
            .GroupBy(x => x.JobId)
            .ToDictionary(x => x.Key, x => x.Sum(v => v.Quantity));
        var inputByJob = usageEvents
            .Where(x => x.UsageType == UsageTypes.InputToken)
            .GroupBy(x => x.JobId)
            .ToDictionary(x => x.Key, x => x.Sum(v => v.Quantity));
        var outputByJob = usageEvents
            .Where(x => x.UsageType == UsageTypes.OutputToken)
            .GroupBy(x => x.JobId)
            .ToDictionary(x => x.Key, x => x.Sum(v => v.Quantity));

        var trend = jobs
            .GroupBy(x => x.CreatedAt.Date)
            .ToDictionary(
                x => x.Key,
                x => new
                {
                    Credits = x.Sum(v => v.ActualCredits),
                    Amount = x.Sum(v => v.ActualAmount),
                    Requests = x.Count()
                });
        var tokensByDate = usageEvents
            .Where(x => TokenUsageTypes.Contains(x.UsageType))
            .GroupBy(x => x.OccurredAt.Date)
            .ToDictionary(x => x.Key, x => x.Sum(v => v.Quantity));
        var points = new List<BillingUsagePointResponse>();
        var lastDate = rangeTo.AddTicks(-1).Date;
        for (var date = rangeFrom.Date; date <= lastDate; date = date.AddDays(1))
        {
            trend.TryGetValue(date, out var day);
            tokensByDate.TryGetValue(date, out var tokens);
            points.Add(new BillingUsagePointResponse(
                date,
                day?.Credits ?? 0m,
                day?.Amount ?? 0m,
                day?.Requests ?? 0,
                tokens));
        }

        var items = jobs.Select(job =>
        {
            inputByJob.TryGetValue(job.Id, out var input);
            outputByJob.TryGetValue(job.Id, out var output);
            tokensByJob.TryGetValue(job.Id, out var total);
            return new BillingUsageItemResponse(
                job.Id,
                job.CreatedAt,
                job.JobType,
                job.Model,
                job.ExecutionMode,
                job.BillingMode,
                job.Status,
                input,
                output,
                total,
                job.ActualCredits,
                job.ActualAmount,
                job.Currency);
        }).ToList();

        return new BillingUsageResponse(
            summary.BillingAccountId,
            workspaceId,
            rangeFrom,
            rangeTo,
            jobs.Sum(x => x.ActualCredits),
            jobs.Sum(x => x.ActualAmount),
            jobs.Count,
            usageEvents.Where(x => TokenUsageTypes.Contains(x.UsageType)).Sum(x => x.Quantity),
            summary.Currency,
            true,
            DateTime.UtcNow,
            points,
            items);
    }

    public async Task<BillingBillsResponse> GetBillsAsync(
        Guid userId,
        Guid workspaceId,
        DateTime? from,
        DateTime? to,
        CancellationToken ct = default)
    {
        var summary = await _billing.GetSummaryAsync(userId, workspaceId, ct);
        var (rangeFrom, rangeTo) = NormalizeRange(from, to);
        var charges = await _db.BillingCharges.AsNoTracking()
            .Where(x =>
                x.BillingAccountId == summary.BillingAccountId &&
                x.CreatedAt >= rangeFrom &&
                x.CreatedAt < rangeTo)
            .OrderByDescending(x => x.CreatedAt)
            .Take(500)
            .ToListAsync(ct);
        var orders = await _db.RechargeOrders.AsNoTracking()
            .Where(x =>
                x.BillingAccountId == summary.BillingAccountId &&
                x.CreatedAt >= rangeFrom &&
                x.CreatedAt < rangeTo)
            .OrderByDescending(x => x.CreatedAt)
            .Take(500)
            .ToListAsync(ct);

        var items = new List<BillingBillItemResponse>(charges.Count + orders.Count);
        items.AddRange(charges.Select(x => new BillingBillItemResponse(
            x.Id,
            x.CreatedAt,
            "CHARGE",
            "云算力消费",
            x.JobId.ToString("N"),
            -x.Credits,
            checked((long)Math.Round(x.Amount * 100m, MidpointRounding.AwayFromZero)),
            x.Currency,
            x.Status)));
        items.AddRange(orders.Select(x => new BillingBillItemResponse(
            x.Id,
            x.PaidAt ?? x.CreatedAt,
            "RECHARGE",
            "购买算力点",
            x.OrderNo,
            x.Status == RechargeOrderStatuses.Paid ? x.PaidCredits + x.BonusCredits : 0m,
            x.AmountMinor,
            x.Currency,
            x.Status)));

        return new BillingBillsResponse(
            summary.BillingAccountId,
            workspaceId,
            summary.Currency,
            true,
            DateTime.UtcNow,
            items.OrderByDescending(x => x.OccurredAt).Take(500).ToList());
    }

    public async Task<BillingPricingResponse> GetPricingAsync(
        Guid userId,
        Guid workspaceId,
        CancellationToken ct = default)
    {
        await _billing.GetSummaryAsync(userId, workspaceId, ct);
        var now = DateTime.UtcNow;
        var version = await _db.PricePlanVersions.AsNoTracking()
            .Where(x =>
                x.Status == PriceVersionStatuses.Published &&
                x.EffectiveFrom <= now &&
                (!x.EffectiveTo.HasValue || x.EffectiveTo > now))
            .OrderByDescending(x => x.EffectiveFrom)
            .FirstOrDefaultAsync(ct);
        if (version == null)
        {
            return new BillingPricingResponse(
                null,
                "unpublished",
                0,
                _billingSettings.Currency,
                _billingSettings.ShadowPricingEnabled,
                null,
                []);
        }

        var rules = await _db.PriceRules.AsNoTracking()
            .Where(x => x.PricePlanVersionId == version.Id)
            .OrderBy(x => x.ModelId)
            .ThenBy(x => x.MeterType)
            .Select(x => new BillingPriceRuleResponse(
                x.MeterType,
                x.ProviderId,
                x.ModelId,
                x.Unit,
                x.UnitSize,
                x.CreditRate,
                x.SaleUnitPrice,
                version.Currency))
            .ToListAsync(ct);
        return new BillingPricingResponse(
            version.Id,
            version.Code,
            version.Version,
            version.Currency,
            _billingSettings.ShadowPricingEnabled,
            version.EffectiveFrom,
            rules);
    }

    private static (DateTime From, DateTime To) NormalizeRange(DateTime? from, DateTime? to)
    {
        var rangeTo = EnsureUtc(to ?? DateTime.UtcNow).AddTicks(1);
        var rangeFrom = EnsureUtc(from ?? rangeTo.AddDays(-30));
        if (rangeFrom >= rangeTo)
        {
            throw new ValidationException("from", "开始时间必须早于结束时间。");
        }
        if (rangeTo - rangeFrom > TimeSpan.FromDays(366))
        {
            throw new ValidationException("from", "单次查询时间范围不能超过 366 天。");
        }
        return (rangeFrom, rangeTo);
    }

    private static DateTime EnsureUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };
}
