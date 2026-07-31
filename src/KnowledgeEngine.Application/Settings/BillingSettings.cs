namespace KnowledgeEngine.Application.Settings;

public class BillingSettings
{
    public bool MeteringEnabled { get; set; } = true;
    public bool ShadowPricingEnabled { get; set; } = true;
    public bool EntitlementEnforcementEnabled { get; set; }
    public bool QuotaEnforcementEnabled { get; set; }
    public string Currency { get; set; } = "CNY";
    public string BaseCurrency { get; set; } = "CNY";
    public int ReservationTtlMinutes { get; set; } = 30;
    public int MaintenanceIntervalSeconds { get; set; } = 300;
    public decimal DefaultMonthlyCredits { get; set; }
    public string[] CloudAiEnabledPlanCodes { get; set; } = ["pro", "team", "enterprise", "internal"];
    public Dictionary<string, BillingMeterSettings> Meters { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["INPUT_TOKEN"] = new() { Unit = "token", UnitSize = 1000m, CreditRate = 1000m },
        ["OUTPUT_TOKEN"] = new() { Unit = "token", UnitSize = 1000m, CreditRate = 3000m },
        ["CACHE_READ_TOKEN"] = new() { Unit = "token", UnitSize = 1000m, CreditRate = 250m },
        ["CACHE_WRITE_TOKEN"] = new() { Unit = "token", UnitSize = 1000m, CreditRate = 1000m },
        ["REASONING_TOKEN"] = new() { Unit = "token", UnitSize = 1000m, CreditRate = 3000m },
        ["EMBEDDING_TOKEN"] = new() { Unit = "token", UnitSize = 1000m, CreditRate = 100m }
    };
}

public class BillingMeterSettings
{
    public string Unit { get; set; } = "unit";
    public decimal UnitSize { get; set; } = 1m;
    public decimal CreditRate { get; set; }
    public decimal SaleUnitPrice { get; set; }
    public decimal ProviderUnitCost { get; set; }
    public string ProviderCurrency { get; set; } = "USD";
}
