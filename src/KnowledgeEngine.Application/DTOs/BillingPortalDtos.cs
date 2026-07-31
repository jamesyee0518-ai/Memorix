namespace KnowledgeEngine.Application.DTOs;

public record BillingOverviewResponse(
    Guid BillingAccountId,
    Guid WorkspaceId,
    string AccountName,
    string Currency,
    decimal GrantedCredits,
    decimal ConsumedCredits,
    decimal ReservedCredits,
    decimal AvailableCredits,
    decimal PlanAvailableCredits,
    decimal TopUpAvailableCredits,
    decimal PromotionAvailableCredits,
    decimal MonthCredits,
    decimal MonthAmount,
    decimal PendingCredits,
    int MonthRequests,
    decimal MonthTokens,
    bool IsFinancialTruth,
    bool PaymentEnabled,
    DateTime AsOf);

public record BillingUsagePointResponse(
    DateTime Date,
    decimal Credits,
    decimal Amount,
    int Requests,
    decimal Tokens);

public record BillingUsageItemResponse(
    Guid JobId,
    DateTime CreatedAt,
    string JobType,
    string? Model,
    string ExecutionMode,
    string BillingMode,
    string Status,
    decimal InputTokens,
    decimal OutputTokens,
    decimal TotalTokens,
    decimal Credits,
    decimal Amount,
    string Currency);

public record BillingUsageResponse(
    Guid BillingAccountId,
    Guid WorkspaceId,
    DateTime From,
    DateTime To,
    decimal TotalCredits,
    decimal TotalAmount,
    int TotalRequests,
    decimal TotalTokens,
    string Currency,
    bool IsFinancialTruth,
    DateTime AsOf,
    IReadOnlyList<BillingUsagePointResponse> Trend,
    IReadOnlyList<BillingUsageItemResponse> Items);

public record BillingBillItemResponse(
    Guid Id,
    DateTime OccurredAt,
    string Type,
    string Title,
    string Reference,
    decimal Credits,
    long? AmountMinor,
    string Currency,
    string Status);

public record BillingBillsResponse(
    Guid BillingAccountId,
    Guid WorkspaceId,
    string Currency,
    bool IsFinancialTruth,
    DateTime AsOf,
    IReadOnlyList<BillingBillItemResponse> Items);

public record BillingPriceRuleResponse(
    string MeterType,
    string? ProviderId,
    string? ModelId,
    string Unit,
    decimal UnitSize,
    decimal CreditRate,
    decimal SaleUnitPrice,
    string Currency);

public record BillingPricingResponse(
    Guid? PricePlanVersionId,
    string PlanCode,
    int Version,
    string Currency,
    bool IsShadowPricing,
    DateTime? EffectiveFrom,
    IReadOnlyList<BillingPriceRuleResponse> Rules);
