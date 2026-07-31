namespace KnowledgeEngine.Domain.Entities;

public class RechargeProduct
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Currency { get; set; } = "CNY";
    public long AmountMinor { get; set; }
    public decimal PaidCredits { get; set; }
    public decimal BonusCredits { get; set; }
    public int? BonusExpiresInDays { get; set; }
    public bool IsActive { get; set; }
    public DateTime EffectiveFrom { get; set; }
    public DateTime? EffectiveTo { get; set; }
    public int SortOrder { get; set; }
    public long Version { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class RechargeOrder
{
    public Guid Id { get; set; }
    public string OrderNo { get; set; } = string.Empty;
    public Guid BillingAccountId { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid InitiatedByUserId { get; set; }
    public Guid RechargeProductId { get; set; }
    public string Channel { get; set; } = string.Empty;
    public string ChannelScene { get; set; } = string.Empty;
    public string Currency { get; set; } = "CNY";
    public long AmountMinor { get; set; }
    public decimal PaidCredits { get; set; }
    public decimal BonusCredits { get; set; }
    public int? BonusExpiresInDays { get; set; }
    public string PricingSnapshotJson { get; set; } = "{}";
    public string Status { get; set; } = RechargeOrderStatuses.Created;
    public string? ProviderTradeNo { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public DateTime? PaidAt { get; set; }
    public DateTime? FulfilledAt { get; set; }
    public DateTime? ClosedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class PaymentAttempt
{
    public Guid Id { get; set; }
    public Guid RechargeOrderId { get; set; }
    public int AttemptNo { get; set; }
    public string Channel { get; set; } = string.Empty;
    public string ChannelScene { get; set; } = string.Empty;
    public string Status { get; set; } = RechargeOrderStatuses.Created;
    public string? PayloadType { get; set; }
    public string? PaymentPayload { get; set; }
    public string? ProviderTradeNo { get; set; }
    public string? ProviderRequestId { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? LastQueriedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class PaymentNotification
{
    public Guid Id { get; set; }
    public string Channel { get; set; } = string.Empty;
    public string ProviderNotificationId { get; set; } = string.Empty;
    public string OrderNo { get; set; } = string.Empty;
    public string? ProviderTradeNo { get; set; }
    public string NotificationType { get; set; } = "PAYMENT";
    public bool SignatureValid { get; set; }
    public string BodyHash { get; set; } = string.Empty;
    public string Status { get; set; } = "RECEIVED";
    public string? FailureReason { get; set; }
    public DateTime ReceivedAt { get; set; }
    public DateTime? ProcessedAt { get; set; }
}

public class PaymentRefund
{
    public Guid Id { get; set; }
    public string RefundNo { get; set; } = string.Empty;
    public Guid RechargeOrderId { get; set; }
    public Guid RequestedByUserId { get; set; }
    public Guid? ReviewedByUserId { get; set; }
    public long AmountMinor { get; set; }
    public decimal PaidCreditsToRecover { get; set; }
    public decimal BonusCreditsToRecover { get; set; }
    public string Currency { get; set; } = "CNY";
    public string Status { get; set; } = PaymentRefundStatuses.Created;
    public string? ProviderRefundNo { get; set; }
    public string? ReasonCode { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}
