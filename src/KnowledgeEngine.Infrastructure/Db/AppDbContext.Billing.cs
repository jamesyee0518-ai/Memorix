using KnowledgeEngine.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeEngine.Infrastructure.Db;

public partial class AppDbContext
{
    private static void ConfigureBilling(ModelBuilder modelBuilder)
    {
        ConfigureBillingAccount(modelBuilder);
        ConfigureWorkspaceBillingBinding(modelBuilder);
        ConfigureAccountEntitlement(modelBuilder);
        ConfigurePricePlanVersion(modelBuilder);
        ConfigurePriceRule(modelBuilder);
        ConfigureQuotaBucket(modelBuilder);
        ConfigureBalanceReservation(modelBuilder);
        ConfigureAiTask(modelBuilder);
        ConfigureAiRequestAttempt(modelBuilder);
        ConfigureUsageEvent(modelBuilder);
        ConfigureBillingCharge(modelBuilder);
        ConfigureProviderCost(modelBuilder);
        ConfigureAccountLedger(modelBuilder);
        ConfigureRechargeProduct(modelBuilder);
        ConfigureRechargeOrder(modelBuilder);
        ConfigurePaymentAttempt(modelBuilder);
        ConfigurePaymentNotification(modelBuilder);
        ConfigurePaymentRefund(modelBuilder);
    }

    private static void ConfigureBillingAccount(ModelBuilder modelBuilder)
    {
        var e = modelBuilder.Entity<BillingAccount>();
        e.ToTable("billing_accounts");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).HasColumnType("uuid");
        e.Property(x => x.OwnerUserId).HasColumnType("uuid");
        e.Property(x => x.AccountType).IsRequired().HasMaxLength(30);
        e.Property(x => x.Name).IsRequired().HasMaxLength(200);
        e.Property(x => x.Currency).IsRequired().HasMaxLength(3);
        e.Property(x => x.Status).IsRequired().HasMaxLength(30);
        e.Property(x => x.Version).IsConcurrencyToken();
        e.HasIndex(x => new { x.OwnerUserId, x.AccountType, x.Status });
    }

    private static void ConfigureWorkspaceBillingBinding(ModelBuilder modelBuilder)
    {
        var e = modelBuilder.Entity<WorkspaceBillingBinding>();
        e.ToTable("workspace_billing_bindings");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).HasColumnType("uuid");
        e.Property(x => x.WorkspaceId).HasColumnType("uuid");
        e.Property(x => x.BillingAccountId).HasColumnType("uuid");
        e.Property(x => x.CreatedByUserId).HasColumnType("uuid");
        e.HasIndex(x => new { x.WorkspaceId, x.IsActive });
        e.HasIndex(x => x.BillingAccountId);
    }

    private static void ConfigureAccountEntitlement(ModelBuilder modelBuilder)
    {
        var e = modelBuilder.Entity<AccountEntitlement>();
        e.ToTable("account_entitlements");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).HasColumnType("uuid");
        e.Property(x => x.BillingAccountId).HasColumnType("uuid");
        e.Property(x => x.EntitlementKey).IsRequired().HasMaxLength(120);
        e.Property(x => x.ValueJson).IsRequired();
        e.HasIndex(x => new { x.BillingAccountId, x.EntitlementKey, x.EffectiveFrom });
    }

    private static void ConfigurePricePlanVersion(ModelBuilder modelBuilder)
    {
        var e = modelBuilder.Entity<PricePlanVersion>();
        e.ToTable("price_plan_versions");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).HasColumnType("uuid");
        e.Property(x => x.Code).IsRequired().HasMaxLength(80);
        e.Property(x => x.Currency).IsRequired().HasMaxLength(3);
        e.Property(x => x.Status).IsRequired().HasMaxLength(30);
        e.HasIndex(x => new { x.Code, x.Version }).IsUnique();
        e.HasIndex(x => new { x.Status, x.EffectiveFrom });
    }

    private static void ConfigurePriceRule(ModelBuilder modelBuilder)
    {
        var e = modelBuilder.Entity<PriceRule>();
        e.ToTable("price_rules");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).HasColumnType("uuid");
        e.Property(x => x.PricePlanVersionId).HasColumnType("uuid");
        e.Property(x => x.MeterType).IsRequired().HasMaxLength(50);
        e.Property(x => x.ProviderId).HasMaxLength(80);
        e.Property(x => x.ModelId).HasMaxLength(160);
        e.Property(x => x.Unit).IsRequired().HasMaxLength(30);
        e.Property(x => x.UnitSize).HasPrecision(20, 6);
        e.Property(x => x.CreditRate).HasPrecision(20, 6);
        e.Property(x => x.SaleUnitPrice).HasPrecision(20, 8);
        e.Property(x => x.ProviderUnitCost).HasPrecision(20, 8);
        e.Property(x => x.ProviderCurrency).IsRequired().HasMaxLength(3);
        e.HasIndex(x => new { x.PricePlanVersionId, x.MeterType, x.ProviderId, x.ModelId });
    }

    private static void ConfigureQuotaBucket(ModelBuilder modelBuilder)
    {
        var e = modelBuilder.Entity<QuotaBucket>();
        e.ToTable("quota_buckets");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).HasColumnType("uuid");
        e.Property(x => x.BillingAccountId).HasColumnType("uuid");
        e.Property(x => x.Source).IsRequired().HasMaxLength(30);
        e.Property(x => x.GrantedCredits).HasPrecision(20, 6);
        e.Property(x => x.ConsumedCredits).HasPrecision(20, 6);
        e.Property(x => x.ReservedCredits).HasPrecision(20, 6);
        e.Property(x => x.Version).IsConcurrencyToken();
        e.HasIndex(x => new { x.BillingAccountId, x.ExpiresAt, x.Priority });
    }

    private static void ConfigureBalanceReservation(ModelBuilder modelBuilder)
    {
        var e = modelBuilder.Entity<BalanceReservation>();
        e.ToTable("balance_reservations");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).HasColumnType("uuid");
        e.Property(x => x.BillingAccountId).HasColumnType("uuid");
        e.Property(x => x.JobId).HasColumnType("uuid");
        e.Property(x => x.ReservedCredits).HasPrecision(20, 6);
        e.Property(x => x.ConsumedCredits).HasPrecision(20, 6);
        e.Property(x => x.Status).IsRequired().HasMaxLength(30);
        e.Property(x => x.IdempotencyKey).IsRequired().HasMaxLength(200);
        e.HasIndex(x => x.IdempotencyKey).IsUnique();
        e.HasIndex(x => new { x.JobId, x.Status });
        e.HasIndex(x => new { x.ExpiresAt, x.Status });
    }

    private static void ConfigureAiTask(ModelBuilder modelBuilder)
    {
        var e = modelBuilder.Entity<AiTask>();
        e.ToTable("ai_tasks");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).HasColumnType("uuid");
        e.Property(x => x.JobId).HasColumnType("uuid");
        e.Property(x => x.TaskType).IsRequired().HasMaxLength(80);
        e.Property(x => x.Status).IsRequired().HasMaxLength(30);
        e.HasIndex(x => new { x.JobId, x.Sequence }).IsUnique();
    }

    private static void ConfigureAiRequestAttempt(ModelBuilder modelBuilder)
    {
        var e = modelBuilder.Entity<AiRequestAttempt>();
        e.ToTable("ai_request_attempts");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).HasColumnType("uuid");
        e.Property(x => x.JobId).HasColumnType("uuid");
        e.Property(x => x.TaskId).HasColumnType("uuid");
        e.Property(x => x.ProviderId).IsRequired().HasMaxLength(80);
        e.Property(x => x.RequestedModelId).IsRequired().HasMaxLength(160);
        e.Property(x => x.ActualModelId).HasMaxLength(160);
        e.Property(x => x.ProviderRequestId).HasMaxLength(200);
        e.Property(x => x.Status).IsRequired().HasMaxLength(40);
        e.Property(x => x.ErrorCode).HasMaxLength(120);
        e.Property(x => x.TerminationReason).HasMaxLength(80);
        e.HasIndex(x => new { x.JobId, x.TaskId, x.AttemptNo });
        e.HasIndex(x => new { x.ProviderId, x.ProviderRequestId, x.AttemptNo }).IsUnique();
    }

    private static void ConfigureUsageEvent(ModelBuilder modelBuilder)
    {
        var e = modelBuilder.Entity<UsageEvent>();
        e.ToTable("usage_events");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).HasColumnType("uuid");
        e.Property(x => x.JobId).HasColumnType("uuid");
        e.Property(x => x.TaskId).HasColumnType("uuid");
        e.Property(x => x.AttemptId).HasColumnType("uuid");
        e.Property(x => x.WorkspaceId).HasColumnType("uuid");
        e.Property(x => x.BillingAccountId).HasColumnType("uuid");
        e.Property(x => x.ProviderId).IsRequired().HasMaxLength(80);
        e.Property(x => x.ModelId).IsRequired().HasMaxLength(160);
        e.Property(x => x.UsageType).IsRequired().HasMaxLength(50);
        e.Property(x => x.Quantity).HasPrecision(20, 6);
        e.Property(x => x.Unit).IsRequired().HasMaxLength(30);
        e.Property(x => x.UsageSource).IsRequired().HasMaxLength(50);
        e.Property(x => x.IdempotencyKey).IsRequired().HasMaxLength(200);
        e.Property(x => x.ReconciliationStatus).IsRequired().HasMaxLength(50);
        e.Property(x => x.CalculatedCredits).HasPrecision(20, 6);
        e.Property(x => x.CalculatedAmount).HasPrecision(20, 8);
        e.Property(x => x.Currency).IsRequired().HasMaxLength(3);
        e.HasIndex(x => x.IdempotencyKey).IsUnique();
        e.HasIndex(x => new { x.JobId, x.OccurredAt });
        e.HasIndex(x => new { x.BillingAccountId, x.OccurredAt });
    }

    private static void ConfigureBillingCharge(ModelBuilder modelBuilder)
    {
        var e = modelBuilder.Entity<BillingCharge>();
        e.ToTable("billing_charges");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).HasColumnType("uuid");
        e.Property(x => x.BillingAccountId).HasColumnType("uuid");
        e.Property(x => x.JobId).HasColumnType("uuid");
        e.Property(x => x.PricePlanVersionId).HasColumnType("uuid");
        e.Property(x => x.ChargeType).IsRequired().HasMaxLength(50);
        e.Property(x => x.Credits).HasPrecision(20, 6);
        e.Property(x => x.Amount).HasPrecision(20, 8);
        e.Property(x => x.Currency).IsRequired().HasMaxLength(3);
        e.Property(x => x.Status).IsRequired().HasMaxLength(30);
        e.Property(x => x.IdempotencyKey).IsRequired().HasMaxLength(200);
        e.HasIndex(x => x.IdempotencyKey).IsUnique();
        e.HasIndex(x => new { x.BillingAccountId, x.CreatedAt });
    }

    private static void ConfigureProviderCost(ModelBuilder modelBuilder)
    {
        var e = modelBuilder.Entity<ProviderCost>();
        e.ToTable("provider_costs");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).HasColumnType("uuid");
        e.Property(x => x.JobId).HasColumnType("uuid");
        e.Property(x => x.AttemptId).HasColumnType("uuid");
        e.Property(x => x.ProviderId).IsRequired().HasMaxLength(80);
        e.Property(x => x.ModelId).IsRequired().HasMaxLength(160);
        e.Property(x => x.ProviderAmount).HasPrecision(20, 8);
        e.Property(x => x.ProviderCurrency).IsRequired().HasMaxLength(3);
        e.Property(x => x.ExchangeRateSnapshot).HasPrecision(20, 8);
        e.Property(x => x.ExchangeRateSource).IsRequired().HasMaxLength(80);
        e.Property(x => x.BaseCurrency).IsRequired().HasMaxLength(3);
        e.Property(x => x.BaseCurrencyAmount).HasPrecision(20, 8);
        e.Property(x => x.CostTags).HasMaxLength(500);
        e.Property(x => x.IdempotencyKey).IsRequired().HasMaxLength(200);
        e.HasIndex(x => x.IdempotencyKey).IsUnique();
        e.HasIndex(x => new { x.ProviderId, x.CreatedAt });
    }

    private static void ConfigureAccountLedger(ModelBuilder modelBuilder)
    {
        var e = modelBuilder.Entity<AccountLedger>();
        e.ToTable("account_ledger");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).HasColumnType("uuid");
        e.Property(x => x.BillingAccountId).HasColumnType("uuid");
        e.Property(x => x.BusinessType).IsRequired().HasMaxLength(50);
        e.Property(x => x.BusinessId).HasColumnType("uuid");
        e.Property(x => x.Action).IsRequired().HasMaxLength(30);
        e.Property(x => x.Credits).HasPrecision(20, 6);
        e.Property(x => x.Amount).HasPrecision(20, 8);
        e.Property(x => x.Currency).IsRequired().HasMaxLength(3);
        e.Property(x => x.IdempotencyKey).IsRequired().HasMaxLength(200);
        e.HasIndex(x => x.IdempotencyKey).IsUnique();
        e.HasIndex(x => new { x.BillingAccountId, x.CreatedAt });
        e.HasIndex(x => new { x.BusinessType, x.BusinessId, x.Action, x.Sequence }).IsUnique();
    }

    private static void ConfigureRechargeProduct(ModelBuilder modelBuilder)
    {
        var e = modelBuilder.Entity<RechargeProduct>();
        e.ToTable("recharge_products");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).HasColumnType("uuid");
        e.Property(x => x.Code).IsRequired().HasMaxLength(80);
        e.Property(x => x.DisplayName).IsRequired().HasMaxLength(160);
        e.Property(x => x.Description).IsRequired().HasMaxLength(500);
        e.Property(x => x.Currency).IsRequired().HasMaxLength(3);
        e.Property(x => x.PaidCredits).HasPrecision(20, 6);
        e.Property(x => x.BonusCredits).HasPrecision(20, 6);
        e.Property(x => x.Version).IsConcurrencyToken();
        e.HasIndex(x => x.Code).IsUnique();
        e.HasIndex(x => new { x.IsActive, x.EffectiveFrom, x.SortOrder });
    }

    private static void ConfigureRechargeOrder(ModelBuilder modelBuilder)
    {
        var e = modelBuilder.Entity<RechargeOrder>();
        e.ToTable("recharge_orders");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).HasColumnType("uuid");
        e.Property(x => x.BillingAccountId).HasColumnType("uuid");
        e.Property(x => x.WorkspaceId).HasColumnType("uuid");
        e.Property(x => x.InitiatedByUserId).HasColumnType("uuid");
        e.Property(x => x.RechargeProductId).HasColumnType("uuid");
        e.Property(x => x.OrderNo).IsRequired().HasMaxLength(32);
        e.Property(x => x.Channel).IsRequired().HasMaxLength(30);
        e.Property(x => x.ChannelScene).IsRequired().HasMaxLength(30);
        e.Property(x => x.Currency).IsRequired().HasMaxLength(3);
        e.Property(x => x.PaidCredits).HasPrecision(20, 6);
        e.Property(x => x.BonusCredits).HasPrecision(20, 6);
        e.Property(x => x.PricingSnapshotJson).IsRequired();
        e.Property(x => x.Status).IsRequired().HasMaxLength(30);
        e.Property(x => x.ProviderTradeNo).HasMaxLength(128);
        e.Property(x => x.IdempotencyKey).IsRequired().HasMaxLength(160);
        e.HasIndex(x => x.OrderNo).IsUnique();
        e.HasIndex(x => new { x.BillingAccountId, x.IdempotencyKey }).IsUnique();
        e.HasIndex(x => new { x.BillingAccountId, x.CreatedAt });
        e.HasIndex(x => new { x.Status, x.ExpiresAt });
        e.HasIndex(x => new { x.Channel, x.ProviderTradeNo }).IsUnique();
    }

    private static void ConfigurePaymentAttempt(ModelBuilder modelBuilder)
    {
        var e = modelBuilder.Entity<PaymentAttempt>();
        e.ToTable("payment_attempts");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).HasColumnType("uuid");
        e.Property(x => x.RechargeOrderId).HasColumnType("uuid");
        e.Property(x => x.Channel).IsRequired().HasMaxLength(30);
        e.Property(x => x.ChannelScene).IsRequired().HasMaxLength(30);
        e.Property(x => x.Status).IsRequired().HasMaxLength(30);
        e.Property(x => x.PayloadType).HasMaxLength(30);
        e.Property(x => x.ProviderTradeNo).HasMaxLength(128);
        e.Property(x => x.ProviderRequestId).HasMaxLength(160);
        e.Property(x => x.ErrorCode).HasMaxLength(120);
        e.Property(x => x.ErrorMessage).HasMaxLength(500);
        e.HasIndex(x => new { x.RechargeOrderId, x.AttemptNo }).IsUnique();
        e.HasIndex(x => new { x.Channel, x.ProviderTradeNo });
    }

    private static void ConfigurePaymentNotification(ModelBuilder modelBuilder)
    {
        var e = modelBuilder.Entity<PaymentNotification>();
        e.ToTable("payment_notifications");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).HasColumnType("uuid");
        e.Property(x => x.Channel).IsRequired().HasMaxLength(30);
        e.Property(x => x.ProviderNotificationId).IsRequired().HasMaxLength(160);
        e.Property(x => x.OrderNo).IsRequired().HasMaxLength(32);
        e.Property(x => x.ProviderTradeNo).HasMaxLength(128);
        e.Property(x => x.NotificationType).IsRequired().HasMaxLength(40);
        e.Property(x => x.BodyHash).IsRequired().HasMaxLength(64);
        e.Property(x => x.Status).IsRequired().HasMaxLength(30);
        e.Property(x => x.FailureReason).HasMaxLength(500);
        e.HasIndex(x => new { x.Channel, x.ProviderNotificationId }).IsUnique();
        e.HasIndex(x => new { x.OrderNo, x.ReceivedAt });
    }

    private static void ConfigurePaymentRefund(ModelBuilder modelBuilder)
    {
        var e = modelBuilder.Entity<PaymentRefund>();
        e.ToTable("payment_refunds");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).HasColumnType("uuid");
        e.Property(x => x.RechargeOrderId).HasColumnType("uuid");
        e.Property(x => x.RequestedByUserId).HasColumnType("uuid");
        e.Property(x => x.ReviewedByUserId).HasColumnType("uuid");
        e.Property(x => x.RefundNo).IsRequired().HasMaxLength(64);
        e.Property(x => x.PaidCreditsToRecover).HasPrecision(20, 6);
        e.Property(x => x.BonusCreditsToRecover).HasPrecision(20, 6);
        e.Property(x => x.Currency).IsRequired().HasMaxLength(3);
        e.Property(x => x.Status).IsRequired().HasMaxLength(30);
        e.Property(x => x.ProviderRefundNo).HasMaxLength(128);
        e.Property(x => x.ReasonCode).HasMaxLength(120);
        e.Property(x => x.IdempotencyKey).IsRequired().HasMaxLength(160);
        e.HasIndex(x => x.RefundNo).IsUnique();
        e.HasIndex(x => new { x.RechargeOrderId, x.IdempotencyKey }).IsUnique();
        e.HasIndex(x => new { x.Status, x.UpdatedAt });
    }
}
