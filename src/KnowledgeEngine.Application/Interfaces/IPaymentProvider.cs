using KnowledgeEngine.Application.DTOs;

namespace KnowledgeEngine.Application.Interfaces;

public interface IPaymentProvider
{
    string Channel { get; }
    bool IsEnabled { get; }
    Task<PaymentProviderCreateResult> CreatePaymentAsync(
        PaymentProviderOrder order,
        string scene,
        CancellationToken ct = default);
    Task<PaymentProviderStatusResult> QueryPaymentAsync(
        string orderNo,
        CancellationToken ct = default);
    Task ClosePaymentAsync(
        string orderNo,
        CancellationToken ct = default);
    Task<PaymentProviderNotification> ParseNotificationAsync(
        string body,
        IReadOnlyDictionary<string, string> headers,
        CancellationToken ct = default);
}

public interface IFakePaymentProvider : IPaymentProvider
{
    PaymentProviderNotification MarkPaid(
        string orderNo,
        long amountMinor,
        string currency);
}
