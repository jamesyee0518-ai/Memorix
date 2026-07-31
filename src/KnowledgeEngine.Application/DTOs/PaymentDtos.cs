namespace KnowledgeEngine.Application.DTOs;

public record PaymentMethodResponse(
    string Channel,
    string Scene,
    string DisplayName,
    bool Enabled);

public record RechargeProductResponse(
    Guid Id,
    string Code,
    string DisplayName,
    string Description,
    string Currency,
    long AmountMinor,
    decimal PaidCredits,
    decimal BonusCredits,
    int? BonusExpiresInDays);

public record RechargeCatalogResponse(
    bool PaymentEnabled,
    IReadOnlyList<PaymentMethodResponse> Methods,
    IReadOnlyList<RechargeProductResponse> Products);

public class CreateRechargeOrderRequest
{
    public Guid WorkspaceId { get; set; }
    public Guid RechargeProductId { get; set; }
    public string PaymentChannel { get; set; } = string.Empty;
    public string PaymentScene { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
}

public record RechargeOrderResponse(
    Guid Id,
    string OrderNo,
    Guid BillingAccountId,
    Guid WorkspaceId,
    Guid RechargeProductId,
    string ProductName,
    string Channel,
    string ChannelScene,
    string Currency,
    long AmountMinor,
    decimal PaidCredits,
    decimal BonusCredits,
    string Status,
    string? PaymentPayloadType,
    string? PaymentPayload,
    string? ProviderTradeNo,
    DateTime ExpiresAt,
    DateTime? PaidAt,
    DateTime? FulfilledAt,
    DateTime CreatedAt);

public record RechargeOrderListResponse(
    IReadOnlyList<RechargeOrderResponse> Items);

public record PaymentProviderOrder(
    string OrderNo,
    string Description,
    long AmountMinor,
    string Currency,
    DateTime ExpiresAt);

public record PaymentProviderCreateResult(
    string Status,
    string PayloadType,
    string PaymentPayload,
    string? ProviderRequestId = null,
    string? ProviderTradeNo = null);

public record PaymentProviderStatusResult(
    string Channel,
    string OrderNo,
    string Status,
    long AmountMinor,
    string Currency,
    string? ProviderTradeNo,
    DateTime? PaidAt);

public record PaymentProviderNotification(
    string Channel,
    string ProviderNotificationId,
    string OrderNo,
    string Status,
    long AmountMinor,
    string Currency,
    string? ProviderTradeNo,
    DateTime? PaidAt,
    string BodyHash);
