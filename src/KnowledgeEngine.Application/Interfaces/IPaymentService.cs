using KnowledgeEngine.Application.DTOs;

namespace KnowledgeEngine.Application.Interfaces;

public interface IPaymentService
{
    Task EnsureDefaultsAsync(CancellationToken ct = default);
    Task<RechargeCatalogResponse> GetCatalogAsync(CancellationToken ct = default);
    Task<RechargeOrderResponse> CreateOrderAsync(
        Guid userId,
        CreateRechargeOrderRequest request,
        CancellationToken ct = default);
    Task<RechargeOrderListResponse> ListOrdersAsync(
        Guid userId,
        Guid workspaceId,
        CancellationToken ct = default);
    Task<RechargeOrderResponse> GetOrderAsync(
        Guid userId,
        Guid workspaceId,
        Guid orderId,
        CancellationToken ct = default);
    Task<RechargeOrderResponse> RefreshOrderAsync(
        Guid userId,
        Guid workspaceId,
        Guid orderId,
        CancellationToken ct = default);
    Task<RechargeOrderResponse> CloseOrderAsync(
        Guid userId,
        Guid workspaceId,
        Guid orderId,
        CancellationToken ct = default);
    Task ProcessNotificationAsync(
        string channel,
        string body,
        IReadOnlyDictionary<string, string> headers,
        CancellationToken ct = default);
    Task<RechargeOrderResponse> ConfirmFakePaymentAsync(
        Guid userId,
        Guid workspaceId,
        Guid orderId,
        CancellationToken ct = default);
    Task<int> RecoverPendingAsync(CancellationToken ct = default);
}
