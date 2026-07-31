namespace KnowledgeEngine.Domain.Entities;

public class BillingAccount
{
    public Guid Id { get; set; }
    public string AccountType { get; set; } = BillingAccountTypes.Personal;
    public Guid? OwnerUserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Currency { get; set; } = "CNY";
    public string Status { get; set; } = BillingAccountStatuses.Active;
    public long Version { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class WorkspaceBillingBinding
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid BillingAccountId { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime EffectiveFrom { get; set; }
    public DateTime? EffectiveTo { get; set; }
    public Guid CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class AccountEntitlement
{
    public Guid Id { get; set; }
    public Guid BillingAccountId { get; set; }
    public string EntitlementKey { get; set; } = string.Empty;
    public string ValueJson { get; set; } = "null";
    public DateTime EffectiveFrom { get; set; }
    public DateTime? EffectiveTo { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class PricePlanVersion
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public int Version { get; set; }
    public string Currency { get; set; } = "CNY";
    public string Status { get; set; } = PriceVersionStatuses.Draft;
    public DateTime EffectiveFrom { get; set; }
    public DateTime? EffectiveTo { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class PriceRule
{
    public Guid Id { get; set; }
    public Guid PricePlanVersionId { get; set; }
    public string MeterType { get; set; } = string.Empty;
    public string? ProviderId { get; set; }
    public string? ModelId { get; set; }
    public string Unit { get; set; } = "token";
    public decimal UnitSize { get; set; } = 1000m;
    public decimal CreditRate { get; set; }
    public decimal SaleUnitPrice { get; set; }
    public decimal ProviderUnitCost { get; set; }
    public string ProviderCurrency { get; set; } = "USD";
    public DateTime CreatedAt { get; set; }
}

public class QuotaBucket
{
    public Guid Id { get; set; }
    public Guid BillingAccountId { get; set; }
    public string Source { get; set; } = QuotaBucketSources.Plan;
    public decimal GrantedCredits { get; set; }
    public decimal ConsumedCredits { get; set; }
    public decimal ReservedCredits { get; set; }
    public DateTime EffectiveFrom { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public int Priority { get; set; }
    public long Version { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class BalanceReservation
{
    public Guid Id { get; set; }
    public Guid BillingAccountId { get; set; }
    public Guid JobId { get; set; }
    public decimal ReservedCredits { get; set; }
    public decimal ConsumedCredits { get; set; }
    public string AllocationJson { get; set; } = "{}";
    public string Status { get; set; } = ReservationStatuses.Active;
    public string IdempotencyKey { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class AiTask
{
    public Guid Id { get; set; }
    public Guid JobId { get; set; }
    public string TaskType { get; set; } = string.Empty;
    public string Status { get; set; } = AiJobStatuses.Pending;
    public int Sequence { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}

public class AiRequestAttempt
{
    public Guid Id { get; set; }
    public Guid JobId { get; set; }
    public Guid? TaskId { get; set; }
    public string ProviderId { get; set; } = string.Empty;
    public string RequestedModelId { get; set; } = string.Empty;
    public string? ActualModelId { get; set; }
    public string? ProviderRequestId { get; set; }
    public int AttemptNo { get; set; } = 1;
    public string Status { get; set; } = AiJobStatuses.Pending;
    public int? HttpStatus { get; set; }
    public string? ErrorCode { get; set; }
    public bool IsChargeable { get; set; } = true;
    public string? TerminationReason { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}

public class UsageEvent
{
    public Guid Id { get; set; }
    public Guid JobId { get; set; }
    public Guid? TaskId { get; set; }
    public Guid? AttemptId { get; set; }
    public Guid? WorkspaceId { get; set; }
    public Guid? BillingAccountId { get; set; }
    public string ProviderId { get; set; } = string.Empty;
    public string ModelId { get; set; } = string.Empty;
    public string UsageType { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public string Unit { get; set; } = string.Empty;
    public string UsageSource { get; set; } = UsageSources.Provider;
    public DateTime OccurredAt { get; set; }
    public DateTime ReceivedAt { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public string? RawUsageJson { get; set; }
    public string ReconciliationStatus { get; set; } = "VERIFIED";
    public decimal CalculatedCredits { get; set; }
    public decimal CalculatedAmount { get; set; }
    public string Currency { get; set; } = "CNY";
}

public class BillingCharge
{
    public Guid Id { get; set; }
    public Guid BillingAccountId { get; set; }
    public Guid JobId { get; set; }
    public Guid PricePlanVersionId { get; set; }
    public string ChargeType { get; set; } = "AI_USAGE";
    public decimal Credits { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "CNY";
    public string Status { get; set; } = "POSTED";
    public string IdempotencyKey { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

public class ProviderCost
{
    public Guid Id { get; set; }
    public Guid JobId { get; set; }
    public Guid? AttemptId { get; set; }
    public string ProviderId { get; set; } = string.Empty;
    public string ModelId { get; set; } = string.Empty;
    public decimal ProviderAmount { get; set; }
    public string ProviderCurrency { get; set; } = "USD";
    public decimal ExchangeRateSnapshot { get; set; } = 1m;
    public string ExchangeRateSource { get; set; } = "IDENTITY";
    public DateTime ExchangeRateEffectiveAt { get; set; }
    public string BaseCurrency { get; set; } = "CNY";
    public decimal BaseCurrencyAmount { get; set; }
    public string? CostTags { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

public class AccountLedger
{
    public Guid Id { get; set; }
    public Guid BillingAccountId { get; set; }
    public string BusinessType { get; set; } = string.Empty;
    public Guid BusinessId { get; set; }
    public string Action { get; set; } = string.Empty;
    public int Sequence { get; set; }
    public decimal Credits { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "CNY";
    public string IdempotencyKey { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

