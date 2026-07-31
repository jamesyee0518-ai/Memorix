using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using KnowledgeEngine.Application.DTOs;
using KnowledgeEngine.Application.Exceptions;
using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Application.Settings;
using KnowledgeEngine.Domain.Entities;
using Microsoft.Extensions.Options;

namespace KnowledgeEngine.Infrastructure.Payments;

public sealed class FakePaymentProvider : IFakePaymentProvider
{
    private readonly ConcurrentDictionary<string, PaymentProviderStatusResult> _orders = new();
    private readonly PaymentSettings _settings;

    public FakePaymentProvider(IOptions<PaymentSettings> settings)
    {
        _settings = settings.Value;
    }

    public string Channel => PaymentChannels.Fake;
    public bool IsEnabled => _settings.Fake.Enabled;

    public Task<PaymentProviderCreateResult> CreatePaymentAsync(
        PaymentProviderOrder order,
        string scene,
        CancellationToken ct = default)
    {
        EnsureEnabled();
        _orders[order.OrderNo] = new PaymentProviderStatusResult(
            Channel,
            order.OrderNo,
            RechargeOrderStatuses.Paying,
            order.AmountMinor,
            order.Currency,
            null,
            null);
        return Task.FromResult(new PaymentProviderCreateResult(
            RechargeOrderStatuses.Paying,
            PaymentPayloadTypes.Fake,
            order.OrderNo));
    }

    public Task<PaymentProviderStatusResult> QueryPaymentAsync(
        string orderNo,
        CancellationToken ct = default)
    {
        EnsureEnabled();
        if (!_orders.TryGetValue(orderNo, out var status))
        {
            throw new AppException("payment_order_not_found", "模拟支付订单不存在。");
        }
        return Task.FromResult(status);
    }

    public Task ClosePaymentAsync(string orderNo, CancellationToken ct = default)
    {
        EnsureEnabled();
        if (_orders.TryGetValue(orderNo, out var status) &&
            status.Status != RechargeOrderStatuses.Paid)
        {
            _orders[orderNo] = status with { Status = RechargeOrderStatuses.Closed };
        }
        return Task.CompletedTask;
    }

    public Task<PaymentProviderNotification> ParseNotificationAsync(
        string body,
        IReadOnlyDictionary<string, string> headers,
        CancellationToken ct = default)
    {
        throw new AppException("payment_notification_unsupported", "模拟支付不接受公网回调。");
    }

    public PaymentProviderNotification MarkPaid(
        string orderNo,
        long amountMinor,
        string currency)
    {
        EnsureEnabled();
        var now = DateTime.UtcNow;
        var tradeNo = $"FAKE{now:yyyyMMddHHmmss}{RandomNumberGenerator.GetHexString(8)}";
        var status = new PaymentProviderStatusResult(
            Channel,
            orderNo,
            RechargeOrderStatuses.Paid,
            amountMinor,
            currency,
            tradeNo,
            now);
        _orders[orderNo] = status;
        var bodyHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{orderNo}:{tradeNo}:{amountMinor}:{currency}")));
        return new PaymentProviderNotification(
            Channel,
            tradeNo,
            orderNo,
            RechargeOrderStatuses.Paid,
            amountMinor,
            currency,
            tradeNo,
            now,
            bodyHash);
    }

    private void EnsureEnabled()
    {
        if (!IsEnabled)
        {
            throw new AppException("payment_channel_unavailable", "模拟支付未启用。");
        }
    }
}
