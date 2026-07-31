using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using KnowledgeEngine.Application.DTOs;
using KnowledgeEngine.Application.Exceptions;
using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Application.Settings;
using KnowledgeEngine.Domain.Entities;
using KnowledgeEngine.Infrastructure.Db;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KnowledgeEngine.Infrastructure.Payments;

public sealed class PaymentService : IPaymentService
{
    private readonly AppDbContext _db;
    private readonly IAiBillingService _billing;
    private readonly PaymentSettings _settings;
    private readonly IReadOnlyDictionary<string, IPaymentProvider> _providers;
    private readonly ILogger<PaymentService> _logger;

    public PaymentService(
        AppDbContext db,
        IAiBillingService billing,
        IOptions<PaymentSettings> settings,
        IEnumerable<IPaymentProvider> providers,
        ILogger<PaymentService> logger)
    {
        _db = db;
        _billing = billing;
        _settings = settings.Value;
        _providers = providers.ToDictionary(x => x.Channel, StringComparer.OrdinalIgnoreCase);
        _logger = logger;
    }

    public async Task EnsureDefaultsAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var configuredCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var configured in _settings.Products)
        {
            var code = configured.Code.Trim().ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(code) ||
                configured.AmountMinor <= 0 ||
                configured.PaidCredits <= 0 ||
                !string.Equals(configured.Currency, "CNY", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Skipping invalid recharge product configuration {Code}", configured.Code);
                continue;
            }
            configuredCodes.Add(code);
            var product = await _db.RechargeProducts.SingleOrDefaultAsync(x => x.Code == code, ct);
            if (product == null)
            {
                product = new RechargeProduct
                {
                    Id = Guid.CreateVersion7(),
                    Code = code,
                    EffectiveFrom = now,
                    CreatedAt = now
                };
                _db.RechargeProducts.Add(product);
            }
            product.DisplayName = string.IsNullOrWhiteSpace(configured.DisplayName)
                ? code
                : configured.DisplayName.Trim();
            product.Description = configured.Description.Trim();
            product.Currency = configured.Currency.Trim().ToUpperInvariant();
            product.AmountMinor = configured.AmountMinor;
            product.PaidCredits = configured.PaidCredits;
            product.BonusCredits = Math.Max(0m, configured.BonusCredits);
            product.BonusExpiresInDays = configured.BonusExpiresInDays is > 0
                ? configured.BonusExpiresInDays
                : null;
            product.IsActive = configured.Enabled;
            product.SortOrder = configured.SortOrder;
            product.Version++;
            product.UpdatedAt = now;
        }

        var staleProducts = await _db.RechargeProducts
            .Where(x => x.IsActive && !configuredCodes.Contains(x.Code))
            .ToListAsync(ct);
        foreach (var stale in staleProducts)
        {
            stale.IsActive = false;
            stale.EffectiveTo = now;
            stale.Version++;
            stale.UpdatedAt = now;
        }
        await _db.SaveChangesAsync(ct);
    }

    public async Task<RechargeCatalogResponse> GetCatalogAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var products = await _db.RechargeProducts.AsNoTracking()
            .Where(x =>
                x.IsActive &&
                x.EffectiveFrom <= now &&
                (!x.EffectiveTo.HasValue || x.EffectiveTo > now))
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.AmountMinor)
            .Select(x => new RechargeProductResponse(
                x.Id,
                x.Code,
                x.DisplayName,
                x.Description,
                x.Currency,
                x.AmountMinor,
                x.PaidCredits,
                x.BonusCredits,
                x.BonusExpiresInDays))
            .ToListAsync(ct);
        var methods = new List<PaymentMethodResponse>
        {
            Method(PaymentChannels.WeChat, PaymentScenes.Native, "微信支付"),
            Method(PaymentChannels.Alipay, PaymentScenes.Page, "支付宝")
        };
        if (_providers.TryGetValue(PaymentChannels.Fake, out var fake) && fake.IsEnabled)
        {
            methods.Add(Method(PaymentChannels.Fake, PaymentScenes.Fake, "模拟支付"));
        }
        var paymentEnabled = _settings.Enabled && products.Count > 0 && methods.Any(x => x.Enabled);
        return new RechargeCatalogResponse(paymentEnabled, methods, products);
    }

    public async Task<RechargeOrderResponse> CreateOrderAsync(
        Guid userId,
        CreateRechargeOrderRequest request,
        CancellationToken ct = default)
    {
        if (!_settings.Enabled)
        {
            throw new AppException("payment_disabled", "在线充值暂未开放。");
        }
        if (request.WorkspaceId == Guid.Empty)
        {
            throw new ValidationException("workspaceId", "Workspace 不能为空。");
        }
        if (request.RechargeProductId == Guid.Empty)
        {
            throw new ValidationException("rechargeProductId", "请选择充值商品。");
        }
        var idempotencyKey = request.IdempotencyKey.Trim();
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 160)
        {
            throw new ValidationException("idempotencyKey", "幂等键不能为空且不能超过 160 个字符。");
        }

        var summary = await _billing.GetSummaryAsync(userId, request.WorkspaceId, ct);
        var existing = await _db.RechargeOrders.AsNoTracking().SingleOrDefaultAsync(x =>
            x.BillingAccountId == summary.BillingAccountId &&
            x.IdempotencyKey == idempotencyKey, ct);
        if (existing != null)
        {
            return await MapOrderAsync(existing, true, ct);
        }

        var now = DateTime.UtcNow;
        var product = await _db.RechargeProducts.AsNoTracking().SingleOrDefaultAsync(x =>
            x.Id == request.RechargeProductId &&
            x.IsActive &&
            x.EffectiveFrom <= now &&
            (!x.EffectiveTo.HasValue || x.EffectiveTo > now), ct)
            ?? throw new NotFoundException("充值商品", request.RechargeProductId);
        var channel = NormalizeChannel(request.PaymentChannel);
        var scene = NormalizeScene(channel, request.PaymentScene);
        var provider = RequireProvider(channel);
        var order = new RechargeOrder
        {
            Id = Guid.CreateVersion7(),
            OrderNo = CreateOrderNo(),
            BillingAccountId = summary.BillingAccountId,
            WorkspaceId = request.WorkspaceId,
            InitiatedByUserId = userId,
            RechargeProductId = product.Id,
            Channel = channel,
            ChannelScene = scene,
            Currency = product.Currency,
            AmountMinor = product.AmountMinor,
            PaidCredits = product.PaidCredits,
            BonusCredits = product.BonusCredits,
            BonusExpiresInDays = product.BonusExpiresInDays,
            PricingSnapshotJson = JsonSerializer.Serialize(new
            {
                product.Code,
                product.DisplayName,
                product.Currency,
                product.AmountMinor,
                product.PaidCredits,
                product.BonusCredits,
                product.BonusExpiresInDays,
                ProductVersion = product.Version
            }),
            Status = RechargeOrderStatuses.Created,
            IdempotencyKey = idempotencyKey,
            ExpiresAt = now.AddMinutes(Math.Clamp(_settings.OrderTtlMinutes, 1, 1440)),
            CreatedAt = now,
            UpdatedAt = now
        };
        _db.RechargeOrders.Add(order);
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            _db.ChangeTracker.Clear();
            var duplicate = await _db.RechargeOrders.AsNoTracking().SingleOrDefaultAsync(x =>
                x.BillingAccountId == summary.BillingAccountId &&
                x.IdempotencyKey == idempotencyKey, ct);
            if (duplicate != null)
            {
                return await MapOrderAsync(duplicate, true, ct);
            }
            throw;
        }

        var attempt = new PaymentAttempt
        {
            Id = Guid.CreateVersion7(),
            RechargeOrderId = order.Id,
            AttemptNo = 1,
            Channel = channel,
            ChannelScene = scene,
            Status = RechargeOrderStatuses.Created,
            ExpiresAt = order.ExpiresAt,
            CreatedAt = now,
            UpdatedAt = now
        };
        _db.PaymentAttempts.Add(attempt);
        try
        {
            var created = await provider.CreatePaymentAsync(
                new PaymentProviderOrder(
                    order.OrderNo,
                    product.DisplayName,
                    order.AmountMinor,
                    order.Currency,
                    order.ExpiresAt),
                scene,
                ct);
            order.Status = created.Status;
            order.ProviderTradeNo = created.ProviderTradeNo;
            order.UpdatedAt = DateTime.UtcNow;
            attempt.Status = created.Status;
            attempt.PayloadType = created.PayloadType;
            attempt.PaymentPayload = created.PaymentPayload;
            attempt.ProviderRequestId = created.ProviderRequestId;
            attempt.ProviderTradeNo = created.ProviderTradeNo;
            attempt.UpdatedAt = order.UpdatedAt;
            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            order.Status = RechargeOrderStatuses.Paying;
            order.UpdatedAt = DateTime.UtcNow;
            attempt.Status = RechargeOrderStatuses.Paying;
            attempt.ErrorCode = ex is AppException app ? app.Code : "payment_provider_error";
            attempt.ErrorMessage = Truncate(ex.Message, 500);
            attempt.UpdatedAt = order.UpdatedAt;
            await _db.SaveChangesAsync(ct);
            throw;
        }
        return await MapOrderAsync(order, true, ct);
    }

    public async Task<RechargeOrderListResponse> ListOrdersAsync(
        Guid userId,
        Guid workspaceId,
        CancellationToken ct = default)
    {
        var summary = await _billing.GetSummaryAsync(userId, workspaceId, ct);
        var orders = await _db.RechargeOrders.AsNoTracking()
            .Where(x => x.BillingAccountId == summary.BillingAccountId)
            .OrderByDescending(x => x.CreatedAt)
            .Take(100)
            .ToListAsync(ct);
        var mapped = new List<RechargeOrderResponse>(orders.Count);
        foreach (var order in orders)
        {
            mapped.Add(await MapOrderAsync(order, false, ct));
        }
        return new RechargeOrderListResponse(mapped);
    }

    public async Task<RechargeOrderResponse> GetOrderAsync(
        Guid userId,
        Guid workspaceId,
        Guid orderId,
        CancellationToken ct = default)
    {
        var order = await RequireOwnedOrderAsync(userId, workspaceId, orderId, ct);
        return await MapOrderAsync(order, true, ct);
    }

    public async Task<RechargeOrderResponse> RefreshOrderAsync(
        Guid userId,
        Guid workspaceId,
        Guid orderId,
        CancellationToken ct = default)
    {
        var order = await RequireOwnedOrderAsync(userId, workspaceId, orderId, ct);
        if (order.Status is RechargeOrderStatuses.Paid or RechargeOrderStatuses.Closed or RechargeOrderStatuses.Refunded)
        {
            return await MapOrderAsync(order, false, ct);
        }
        var provider = RequireProvider(order.Channel);
        var status = await provider.QueryPaymentAsync(order.OrderNo, ct);
        var notification = ToQueryNotification(status);
        await ApplyProviderNotificationAsync(notification, false, ct);
        var refreshed = await _db.RechargeOrders.AsNoTracking().SingleAsync(x => x.Id == orderId, ct);
        return await MapOrderAsync(refreshed, true, ct);
    }

    public async Task<RechargeOrderResponse> CloseOrderAsync(
        Guid userId,
        Guid workspaceId,
        Guid orderId,
        CancellationToken ct = default)
    {
        var order = await RequireOwnedOrderAsync(userId, workspaceId, orderId, ct);
        if (order.Status == RechargeOrderStatuses.Paid)
        {
            return await MapOrderAsync(order, false, ct);
        }
        var provider = RequireProvider(order.Channel);
        var status = await provider.QueryPaymentAsync(order.OrderNo, ct);
        if (status.Status == RechargeOrderStatuses.Paid)
        {
            await ApplyProviderNotificationAsync(ToQueryNotification(status), false, ct);
        }
        else
        {
            await provider.ClosePaymentAsync(order.OrderNo, ct);
            var tracked = await _db.RechargeOrders.SingleAsync(x => x.Id == orderId, ct);
            tracked.Status = RechargeOrderStatuses.Closed;
            tracked.ClosedAt = DateTime.UtcNow;
            tracked.UpdatedAt = tracked.ClosedAt.Value;
            var attempts = await _db.PaymentAttempts
                .Where(x => x.RechargeOrderId == orderId)
                .ToListAsync(ct);
            foreach (var attempt in attempts)
            {
                attempt.Status = RechargeOrderStatuses.Closed;
                attempt.PaymentPayload = null;
                attempt.UpdatedAt = tracked.UpdatedAt;
            }
            await _db.SaveChangesAsync(ct);
        }
        var refreshed = await _db.RechargeOrders.AsNoTracking().SingleAsync(x => x.Id == orderId, ct);
        return await MapOrderAsync(refreshed, false, ct);
    }

    public async Task ProcessNotificationAsync(
        string channel,
        string body,
        IReadOnlyDictionary<string, string> headers,
        CancellationToken ct = default)
    {
        var provider = RequireProvider(NormalizeChannel(channel));
        var notification = await provider.ParseNotificationAsync(body, headers, ct);
        await ApplyProviderNotificationAsync(notification, true, ct);
    }

    public async Task<RechargeOrderResponse> ConfirmFakePaymentAsync(
        Guid userId,
        Guid workspaceId,
        Guid orderId,
        CancellationToken ct = default)
    {
        var order = await RequireOwnedOrderAsync(userId, workspaceId, orderId, ct);
        if (!_providers.TryGetValue(PaymentChannels.Fake, out var provider) ||
            provider is not IFakePaymentProvider fake ||
            !fake.IsEnabled ||
            order.Channel != PaymentChannels.Fake)
        {
            throw new AppException("payment_channel_unavailable", "模拟支付未启用。");
        }
        var notification = fake.MarkPaid(order.OrderNo, order.AmountMinor, order.Currency);
        await ApplyProviderNotificationAsync(notification, true, ct);
        var refreshed = await _db.RechargeOrders.AsNoTracking().SingleAsync(x => x.Id == orderId, ct);
        return await MapOrderAsync(refreshed, false, ct);
    }

    public async Task<int> RecoverPendingAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var orders = await _db.RechargeOrders.AsNoTracking()
            .Where(x =>
                x.Status == RechargeOrderStatuses.Created ||
                x.Status == RechargeOrderStatuses.Paying)
            .OrderBy(x => x.CreatedAt)
            .Take(Math.Clamp(_settings.RecoveryBatchSize, 1, 500))
            .ToListAsync(ct);
        var recovered = 0;
        foreach (var order in orders)
        {
            try
            {
                var provider = RequireProvider(order.Channel);
                var status = await provider.QueryPaymentAsync(order.OrderNo, ct);
                if (status.Status == RechargeOrderStatuses.Paid)
                {
                    await ApplyProviderNotificationAsync(ToQueryNotification(status), false, ct);
                    recovered++;
                }
                else if (order.ExpiresAt <= now)
                {
                    await provider.ClosePaymentAsync(order.OrderNo, ct);
                    var tracked = await _db.RechargeOrders.SingleAsync(x => x.Id == order.Id, ct);
                    tracked.Status = RechargeOrderStatuses.Closed;
                    tracked.ClosedAt = now;
                    tracked.UpdatedAt = now;
                    await _db.SaveChangesAsync(ct);
                    _db.ChangeTracker.Clear();
                    recovered++;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(
                    ex,
                    "Payment recovery failed for order {OrderNo} on {Channel}",
                    order.OrderNo,
                    order.Channel);
                _db.ChangeTracker.Clear();
            }
        }
        return recovered;
    }

    private async Task ApplyProviderNotificationAsync(
        PaymentProviderNotification notification,
        bool persistNotification,
        CancellationToken ct)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            var order = await _db.RechargeOrders.SingleOrDefaultAsync(
                x => x.OrderNo == notification.OrderNo,
                ct) ?? throw new NotFoundException("充值订单", notification.OrderNo);
            var attempts = await _db.PaymentAttempts
                .Where(x => x.RechargeOrderId == order.Id)
                .ToListAsync(ct);
            if (!string.Equals(order.Channel, notification.Channel, StringComparison.OrdinalIgnoreCase))
            {
                throw new AppException("payment_channel_mismatch", "支付渠道与充值订单不匹配。");
            }

            PaymentNotification? notificationEntity = null;
            if (persistNotification)
            {
                notificationEntity = await _db.PaymentNotifications.SingleOrDefaultAsync(x =>
                    x.Channel == notification.Channel &&
                    x.ProviderNotificationId == notification.ProviderNotificationId, ct);
                if (notificationEntity?.ProcessedAt != null && order.FulfilledAt != null)
                {
                    await transaction.CommitAsync(ct);
                    return;
                }
                if (notificationEntity == null)
                {
                    notificationEntity = new PaymentNotification
                    {
                        Id = Guid.CreateVersion7(),
                        Channel = notification.Channel,
                        ProviderNotificationId = notification.ProviderNotificationId,
                        OrderNo = notification.OrderNo,
                        ProviderTradeNo = notification.ProviderTradeNo,
                        SignatureValid = true,
                        BodyHash = notification.BodyHash,
                        Status = "RECEIVED",
                        ReceivedAt = DateTime.UtcNow
                    };
                    _db.PaymentNotifications.Add(notificationEntity);
                }
            }

            if (notification.Status == RechargeOrderStatuses.Paid)
            {
                ValidatePaidNotification(order, notification);
                if (order.FulfilledAt == null)
                {
                    FulfillOrder(order, notification, attempts);
                }
            }
            else if (notification.Status == RechargeOrderStatuses.Closed &&
                     order.Status != RechargeOrderStatuses.Paid)
            {
                order.Status = RechargeOrderStatuses.Closed;
                order.ClosedAt ??= DateTime.UtcNow;
                order.UpdatedAt = DateTime.UtcNow;
            }

            if (notificationEntity != null)
            {
                notificationEntity.Status = "PROCESSED";
                notificationEntity.ProcessedAt = DateTime.UtcNow;
            }
            await _db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync(ct);
            _db.ChangeTracker.Clear();
            var fulfilled = await _db.RechargeOrders.AsNoTracking().SingleOrDefaultAsync(
                x => x.OrderNo == notification.OrderNo && x.FulfilledAt != null,
                ct);
            if (fulfilled != null)
            {
                return;
            }
            throw;
        }
    }

    private void FulfillOrder(
        RechargeOrder order,
        PaymentProviderNotification notification,
        IReadOnlyList<PaymentAttempt> attempts)
    {
        var now = DateTime.UtcNow;
        order.Status = RechargeOrderStatuses.Paid;
        order.ProviderTradeNo = notification.ProviderTradeNo;
        order.PaidAt = notification.PaidAt ?? now;
        order.FulfilledAt = now;
        order.UpdatedAt = now;

        var paidBucket = new QuotaBucket
        {
            Id = Guid.CreateVersion7(),
            BillingAccountId = order.BillingAccountId,
            Source = QuotaBucketSources.TopUp,
            GrantedCredits = order.PaidCredits,
            EffectiveFrom = now,
            Priority = 300,
            CreatedAt = now,
            UpdatedAt = now
        };
        _db.QuotaBuckets.Add(paidBucket);
        _db.AccountLedger.Add(new AccountLedger
        {
            Id = Guid.CreateVersion7(),
            BillingAccountId = order.BillingAccountId,
            BusinessType = "RECHARGE",
            BusinessId = order.Id,
            Action = LedgerActions.TopUp,
            Sequence = 1,
            Credits = order.PaidCredits,
            Amount = order.AmountMinor / 100m,
            Currency = order.Currency,
            IdempotencyKey = $"recharge:{order.Id:N}:paid",
            CreatedAt = now
        });

        if (order.BonusCredits > 0m)
        {
            var bonusBucket = new QuotaBucket
            {
                Id = Guid.CreateVersion7(),
                BillingAccountId = order.BillingAccountId,
                Source = QuotaBucketSources.Promotion,
                GrantedCredits = order.BonusCredits,
                EffectiveFrom = now,
                ExpiresAt = order.BonusExpiresInDays is > 0
                    ? now.AddDays(order.BonusExpiresInDays.Value)
                    : null,
                Priority = 10,
                CreatedAt = now,
                UpdatedAt = now
            };
            _db.QuotaBuckets.Add(bonusBucket);
            _db.AccountLedger.Add(new AccountLedger
            {
                Id = Guid.CreateVersion7(),
                BillingAccountId = order.BillingAccountId,
                BusinessType = "RECHARGE",
                BusinessId = order.Id,
                Action = LedgerActions.Grant,
                Sequence = 2,
                Credits = order.BonusCredits,
                Amount = 0m,
                Currency = order.Currency,
                IdempotencyKey = $"recharge:{order.Id:N}:bonus",
                CreatedAt = now
            });
        }

        foreach (var attempt in attempts)
        {
            attempt.Status = RechargeOrderStatuses.Paid;
            attempt.ProviderTradeNo = notification.ProviderTradeNo;
            attempt.PaymentPayload = null;
            attempt.UpdatedAt = now;
        }
    }

    private static void ValidatePaidNotification(
        RechargeOrder order,
        PaymentProviderNotification notification)
    {
        if (notification.AmountMinor != order.AmountMinor)
        {
            throw new AppException(
                "payment_amount_mismatch",
                $"支付金额不匹配：订单 {order.AmountMinor}，渠道 {notification.AmountMinor}。");
        }
        if (!string.Equals(notification.Currency, order.Currency, StringComparison.OrdinalIgnoreCase))
        {
            throw new AppException("payment_currency_mismatch", "支付币种与充值订单不匹配。");
        }
        if (string.IsNullOrWhiteSpace(notification.ProviderTradeNo))
        {
            throw new AppException("payment_trade_no_missing", "支付渠道未返回交易号。");
        }
    }

    private async Task<RechargeOrder> RequireOwnedOrderAsync(
        Guid userId,
        Guid workspaceId,
        Guid orderId,
        CancellationToken ct)
    {
        var summary = await _billing.GetSummaryAsync(userId, workspaceId, ct);
        return await _db.RechargeOrders.AsNoTracking().SingleOrDefaultAsync(x =>
            x.Id == orderId &&
            x.BillingAccountId == summary.BillingAccountId, ct)
            ?? throw new NotFoundException("充值订单", orderId);
    }

    private async Task<RechargeOrderResponse> MapOrderAsync(
        RechargeOrder order,
        bool includePayload,
        CancellationToken ct)
    {
        var productName = await _db.RechargeProducts.AsNoTracking()
            .Where(x => x.Id == order.RechargeProductId)
            .Select(x => x.DisplayName)
            .SingleOrDefaultAsync(ct) ?? "算力点";
        PaymentAttempt? attempt = null;
        if (includePayload &&
            (order.Status is RechargeOrderStatuses.Created or RechargeOrderStatuses.Paying) &&
            order.ExpiresAt > DateTime.UtcNow)
        {
            attempt = await _db.PaymentAttempts.AsNoTracking()
                .Where(x => x.RechargeOrderId == order.Id)
                .OrderByDescending(x => x.AttemptNo)
                .FirstOrDefaultAsync(ct);
        }
        return new RechargeOrderResponse(
            order.Id,
            order.OrderNo,
            order.BillingAccountId,
            order.WorkspaceId,
            order.RechargeProductId,
            productName,
            order.Channel,
            order.ChannelScene,
            order.Currency,
            order.AmountMinor,
            order.PaidCredits,
            order.BonusCredits,
            order.Status,
            attempt?.PayloadType,
            attempt?.PaymentPayload,
            order.ProviderTradeNo,
            order.ExpiresAt,
            order.PaidAt,
            order.FulfilledAt,
            order.CreatedAt);
    }

    private PaymentMethodResponse Method(string channel, string scene, string displayName) =>
        new(
            channel,
            scene,
            displayName,
            _settings.Enabled &&
            _providers.TryGetValue(channel, out var provider) &&
            provider.IsEnabled);

    private IPaymentProvider RequireProvider(string channel)
    {
        if (!_providers.TryGetValue(channel, out var provider) || !provider.IsEnabled)
        {
            throw new AppException("payment_channel_unavailable", $"{channel} 支付未启用或配置不完整。");
        }
        return provider;
    }

    private static PaymentProviderNotification ToQueryNotification(PaymentProviderStatusResult status)
    {
        var tradeNo = status.ProviderTradeNo ?? $"query:{status.OrderNo}:{status.Status}";
        var hash = Convert.ToHexStringLower(SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(
                $"{status.Channel}:{status.OrderNo}:{status.Status}:{status.AmountMinor}:{status.Currency}:{tradeNo}")));
        return new PaymentProviderNotification(
            status.Channel,
            $"query:{tradeNo}",
            status.OrderNo,
            status.Status,
            status.AmountMinor,
            status.Currency,
            status.ProviderTradeNo,
            status.PaidAt,
            hash);
    }

    private static string NormalizeChannel(string value)
    {
        var normalized = value.Trim().ToUpperInvariant();
        return normalized switch
        {
            PaymentChannels.WeChat => PaymentChannels.WeChat,
            PaymentChannels.Alipay => PaymentChannels.Alipay,
            PaymentChannels.Fake => PaymentChannels.Fake,
            _ => throw new ValidationException("paymentChannel", "不支持的支付渠道。")
        };
    }

    private static string NormalizeScene(string channel, string scene)
    {
        var normalized = scene.Trim().ToUpperInvariant();
        return channel switch
        {
            PaymentChannels.WeChat when normalized == PaymentScenes.Native => PaymentScenes.Native,
            PaymentChannels.Alipay when normalized == PaymentScenes.Page => PaymentScenes.Page,
            PaymentChannels.Fake when normalized == PaymentScenes.Fake => PaymentScenes.Fake,
            _ => throw new ValidationException("paymentScene", "支付场景与支付渠道不匹配。")
        };
    }

    private static string CreateOrderNo() =>
        $"MX{DateTime.UtcNow:yyyyMMddHHmmss}{RandomNumberGenerator.GetHexString(10)}";

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];
}
