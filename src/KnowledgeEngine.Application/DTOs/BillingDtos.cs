namespace KnowledgeEngine.Application.DTOs;

public record BillingEntitlementsResponse(
    Guid BillingAccountId,
    Guid WorkspaceId,
    IReadOnlyDictionary<string, object?> Entitlements,
    DateTime AsOf);

public record BillingSummaryResponse(
    Guid BillingAccountId,
    Guid WorkspaceId,
    string Currency,
    decimal GrantedCredits,
    decimal ConsumedCredits,
    decimal ReservedCredits,
    decimal AvailableCredits,
    decimal ActualAmount,
    bool IsFinancialTruth,
    DateTime AsOf);

public class EstimateAiJobRequest
{
    public Guid WorkspaceId { get; set; }
    public string JobType { get; set; } = string.Empty;
    public string ExecutionMode { get; set; } = "MEMORIX_CLOUD";
    public string? ProviderId { get; set; }
    public string? ModelId { get; set; }
    public decimal InputTokens { get; set; }
    public decimal MaxOutputTokens { get; set; }
    public decimal EmbeddingTokens { get; set; }
    public decimal? BudgetLimit { get; set; }
}

public record AiJobEstimateResponse(
    Guid WorkspaceId,
    Guid? PricePlanVersionId,
    decimal EstimatedCredits,
    decimal EstimatedAmount,
    string Currency,
    bool RequiresReservation,
    bool IsShadowPricing);

public class CreateAiBillingJobRequest : EstimateAiJobRequest
{
    public string ClientJobId { get; set; } = string.Empty;
    public Guid? DeviceId { get; set; }
    public string TargetType { get; set; } = "billing";
    public Guid? TargetId { get; set; }
    public string? DataPolicy { get; set; }
    public string? ModelPolicy { get; set; }
}

public record AiBillingJobResponse(
    Guid JobId,
    string ClientJobId,
    Guid WorkspaceId,
    Guid? BillingAccountId,
    string JobType,
    string ExecutionMode,
    string BillingMode,
    string Status,
    decimal EstimatedCredits,
    decimal ActualCredits,
    decimal EstimatedAmount,
    decimal ActualAmount,
    string Currency,
    Guid? ReservationId,
    DateTime CreatedAt,
    DateTime? CompletedAt);

public class RecordUsageEventRequest
{
    public Guid JobId { get; set; }
    public Guid? TaskId { get; set; }
    public Guid? AttemptId { get; set; }
    public string ProviderId { get; set; } = string.Empty;
    public string ModelId { get; set; } = string.Empty;
    public string UsageType { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public string Unit { get; set; } = string.Empty;
    public string UsageSource { get; set; } = "PROVIDER";
    public DateTime? OccurredAt { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public string? RawUsageJson { get; set; }
    public decimal? ProviderAmount { get; set; }
    public string? ProviderCurrency { get; set; }
    public decimal? ExchangeRateSnapshot { get; set; }
    public string? ExchangeRateSource { get; set; }
    public string? BaseCurrency { get; set; }
    public string? CostTags { get; set; }
}

public class StartAiAttemptRequest
{
    public Guid JobId { get; set; }
    public Guid? TaskId { get; set; }
    public string TaskType { get; set; } = "model_call";
    public string ProviderId { get; set; } = string.Empty;
    public string RequestedModelId { get; set; } = string.Empty;
    public string? ActualModelId { get; set; }
    public string ProviderRequestId { get; set; } = string.Empty;
    public int AttemptNo { get; set; } = 1;
    public bool IsChargeable { get; set; } = true;
}

public class CompleteAiAttemptRequest
{
    public string Status { get; set; } = "completed";
    public string? ActualModelId { get; set; }
    public int? HttpStatus { get; set; }
    public string? ErrorCode { get; set; }
    public string? TerminationReason { get; set; }
    public bool? IsChargeable { get; set; }
}

public record AiAttemptResponse(
    Guid AttemptId,
    Guid JobId,
    Guid TaskId,
    string ProviderId,
    string RequestedModelId,
    string? ActualModelId,
    string ProviderRequestId,
    int AttemptNo,
    string Status,
    bool IsChargeable,
    string? TerminationReason,
    DateTime CreatedAt,
    DateTime? CompletedAt);

public record UsageEventResponse(
    Guid EventId,
    Guid JobId,
    string UsageType,
    decimal Quantity,
    decimal CalculatedCredits,
    decimal CalculatedAmount,
    string Currency,
    bool Duplicate);

public class CompleteAiJobRequest
{
    public string Status { get; set; } = "completed";
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
}
