using KnowledgeEngine.Application.DTOs;
using KnowledgeEngine.Application.Settings;
using KnowledgeEngine.Domain.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KnowledgeEngine.Infrastructure.Audio;

/// <summary>
/// Estimates audio provider costs based on pricing unit, audio duration, and configured rates.
/// Rate configuration is sourced from <see cref="AudioSettings.ProviderPricingRates"/>,
/// which maps composite keys (e.g. "whisper-cpp:SECOND") or bare pricing-unit keys
/// (e.g. "SECOND") to a decimal rate per unit.
/// </summary>
public class CostEstimator
{
    private readonly IOptions<AudioSettings> _settings;
    private readonly ILogger<CostEstimator> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="CostEstimator"/> class.
    /// </summary>
    /// <param name="settings">Audio configuration settings containing pricing rates.</param>
    /// <param name="logger">The logger for diagnostic output.</param>
    public CostEstimator(
        IOptions<AudioSettings> settings,
        ILogger<CostEstimator> logger)
    {
        _settings = settings;
        _logger = logger;
    }

    /// <summary>
    /// Estimates the cost for a single provider invocation based on the pricing unit,
    /// audio duration, and the configured rate for the given provider.
    /// </summary>
    /// <param name="pricingUnit">The pricing unit type (REQUEST, SECOND, MINUTE, TOKEN).</param>
    /// <param name="durationMs">The audio duration in milliseconds.</param>
    /// <param name="providerId">The provider identifier for rate lookup.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A <see cref="CostEstimate"/> containing the computed units and estimated cost.</returns>
    public Task<CostEstimate> EstimateAsync(
        string pricingUnit, long durationMs, string providerId, CancellationToken ct)
    {
        var rate = GetRateInternal(providerId, pricingUnit);

        // Compute the number of billable units based on the pricing unit.
        var units = pricingUnit switch
        {
            PricingUnits.Request => 1m,
            PricingUnits.Second => durationMs / 1000m,
            PricingUnits.Minute => durationMs / 60000m,
            PricingUnits.Token => 1m,
            _ => 1m,
        };

        // Delegate to the static cost computation for consistency with the metering service.
        var cost = ProviderUsageMeteringService.ComputeCost(pricingUnit, durationMs, rate);

        var estimate = new CostEstimate
        {
            ProviderId = providerId,
            PricingUnit = pricingUnit,
            Units = units,
            EstimatedCost = cost,
            Currency = "CNY"
        };

        _logger.LogDebug(
            "Cost estimate: provider={ProviderId}, unit={PricingUnit}, duration={DurationMs}ms, " +
            "units={Units}, rate={Rate}, cost={Cost}",
            providerId, pricingUnit, durationMs, units, rate, cost);

        return Task.FromResult(estimate);
    }

    /// <summary>
    /// Gets the configured rate for a specific provider and pricing unit.
    /// Looks up the composite key "{providerId}:{pricingUnit}" first, then falls back
    /// to the bare pricing-unit key, and finally to zero.
    /// </summary>
    /// <param name="providerId">The provider identifier.</param>
    /// <param name="pricingUnit">The pricing unit type (REQUEST, SECOND, MINUTE, TOKEN).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The configured rate per unit as a decimal.</returns>
    public Task<decimal> GetRateAsync(
        string providerId, string pricingUnit, CancellationToken ct)
    {
        return Task.FromResult(GetRateInternal(providerId, pricingUnit));
    }

    /// <summary>
    /// Internal synchronous rate lookup with composite-key fallback.
    /// </summary>
    private decimal GetRateInternal(string providerId, string pricingUnit)
    {
        var rates = _settings.Value.ProviderPricingRates;

        // 1. Provider-specific rate: "providerId:PRICING_UNIT"
        var compositeKey = $"{providerId}:{pricingUnit}";
        if (rates.TryGetValue(compositeKey, out var providerRate))
        {
            return providerRate;
        }

        // 2. Default rate for this pricing unit: "PRICING_UNIT"
        if (rates.TryGetValue(pricingUnit, out var defaultRate))
        {
            return defaultRate;
        }

        // 3. No rate configured; cost will be zero.
        _logger.LogWarning(
            "No pricing rate configured for provider={ProviderId}, unit={PricingUnit}",
            providerId, pricingUnit);

        return 0m;
    }
}
