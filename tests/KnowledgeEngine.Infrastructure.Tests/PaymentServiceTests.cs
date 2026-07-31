using KnowledgeEngine.Application.DTOs;
using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Application.Settings;
using KnowledgeEngine.Domain.Entities;
using KnowledgeEngine.Infrastructure.Db;
using KnowledgeEngine.Infrastructure.Payments;
using KnowledgeEngine.Infrastructure.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace KnowledgeEngine.Infrastructure.Tests;

public sealed class PaymentServiceTests
{
    [Fact]
    public async Task CreateOrder_WithSameIdempotencyKey_ReturnsSameOrder()
    {
        await using var fixture = await PaymentFixture.CreateAsync();
        var request = fixture.CreateRequest("client-order-001");

        var created = await fixture.Payments.CreateOrderAsync(fixture.UserId, request);
        var repeated = await fixture.Payments.CreateOrderAsync(fixture.UserId, request);

        Assert.Equal(created.Id, repeated.Id);
        Assert.Equal(RechargeOrderStatuses.Paying, created.Status);
        Assert.Equal(PaymentPayloadTypes.Fake, created.PaymentPayloadType);
        Assert.Single(await fixture.Db.RechargeOrders.ToListAsync());
        Assert.Single(await fixture.Db.PaymentAttempts.ToListAsync());
    }

    [Fact]
    public async Task ConfirmFakePayment_RepeatedConfirmation_GrantsCreditsExactlyOnce()
    {
        await using var fixture = await PaymentFixture.CreateAsync();
        var created = await fixture.Payments.CreateOrderAsync(
            fixture.UserId,
            fixture.CreateRequest("client-order-002"));

        var paid = await fixture.Payments.ConfirmFakePaymentAsync(
            fixture.UserId,
            fixture.WorkspaceId,
            created.Id);
        var repeated = await fixture.Payments.ConfirmFakePaymentAsync(
            fixture.UserId,
            fixture.WorkspaceId,
            created.Id);

        Assert.Equal(RechargeOrderStatuses.Paid, paid.Status);
        Assert.Equal(paid.Id, repeated.Id);
        Assert.NotNull(paid.FulfilledAt);

        var topUp = await fixture.Db.QuotaBuckets
            .SingleAsync(x => x.Source == QuotaBucketSources.TopUp);
        var promotion = await fixture.Db.QuotaBuckets
            .SingleAsync(x => x.Source == QuotaBucketSources.Promotion);
        Assert.Equal(50_000m, topUp.GrantedCredits);
        Assert.Equal(2_000m, promotion.GrantedCredits);
        Assert.NotNull(promotion.ExpiresAt);

        var ledgers = await fixture.Db.AccountLedger
            .Where(x => x.BusinessId == created.Id)
            .OrderBy(x => x.Sequence)
            .ToListAsync();
        Assert.Equal(2, ledgers.Count);
        Assert.Equal(50_000m, ledgers[0].Credits);
        Assert.Equal(2_000m, ledgers[1].Credits);
        Assert.Equal(2, await fixture.Db.PaymentNotifications.CountAsync());

        var summary = await fixture.Billing.GetSummaryAsync(
            fixture.UserId,
            fixture.WorkspaceId);
        Assert.Equal(52_000m, summary.AvailableCredits);
    }

    private sealed class PaymentFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private PaymentFixture(
            SqliteConnection connection,
            AppDbContext db,
            AiBillingService billing,
            PaymentService payments,
            Guid userId,
            Guid workspaceId,
            Guid productId)
        {
            _connection = connection;
            Db = db;
            Billing = billing;
            Payments = payments;
            UserId = userId;
            WorkspaceId = workspaceId;
            ProductId = productId;
        }

        public AppDbContext Db { get; }
        public AiBillingService Billing { get; }
        public PaymentService Payments { get; }
        public Guid UserId { get; }
        public Guid WorkspaceId { get; }
        public Guid ProductId { get; }

        public CreateRechargeOrderRequest CreateRequest(string idempotencyKey) =>
            new()
            {
                WorkspaceId = WorkspaceId,
                RechargeProductId = ProductId,
                PaymentChannel = PaymentChannels.Fake,
                PaymentScene = PaymentScenes.Fake,
                IdempotencyKey = idempotencyKey
            };

        public static async Task<PaymentFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var db = new AppDbContext(
                new DbContextOptionsBuilder<AppDbContext>()
                    .UseSqlite(connection)
                    .Options);
            await db.Database.EnsureCreatedAsync();

            var userId = Guid.CreateVersion7();
            var workspaceId = Guid.CreateVersion7();
            var now = DateTime.UtcNow;
            db.Users.Add(new User
            {
                Id = userId,
                Email = "payment@example.com",
                Nickname = "Payment Test",
                PasswordHash = "test",
                PlanCode = "free",
                Role = "user",
                Status = "active",
                Timezone = "UTC",
                CreatedAt = now,
                UpdatedAt = now
            });
            db.Workspaces.Add(new Workspace
            {
                Id = workspaceId,
                UserId = userId,
                Name = "Payment Workspace",
                Mode = "cloud",
                StorageProvider = "postgres",
                FileProvider = "minio",
                JobProvider = "redis",
                ModelProvider = "openai",
                CreatedAt = now,
                UpdatedAt = now
            });
            await db.SaveChangesAsync();

            var billingSettings = new BillingSettings
            {
                MeteringEnabled = true,
                EntitlementEnforcementEnabled = true,
                QuotaEnforcementEnabled = true,
                ShadowPricingEnabled = false,
                DefaultMonthlyCredits = 0m,
                Currency = "CNY",
                BaseCurrency = "CNY"
            };
            var billing = new AiBillingService(
                db,
                Options.Create(billingSettings),
                NullLogger<AiBillingService>.Instance);
            await billing.EnsureDefaultsAsync();

            var paymentSettings = new PaymentSettings
            {
                Enabled = true,
                Fake = new FakePaymentSettings { Enabled = true },
                Products =
                [
                    new RechargeProductSettings
                    {
                        Code = "TEST_CNY_50",
                        DisplayName = "测试标准包",
                        Description = "测试充值商品",
                        Currency = "CNY",
                        AmountMinor = 5_000,
                        PaidCredits = 50_000m,
                        BonusCredits = 2_000m,
                        BonusExpiresInDays = 90,
                        Enabled = true,
                        SortOrder = 1
                    }
                ]
            };
            var paymentOptions = Options.Create(paymentSettings);
            var fakeProvider = new FakePaymentProvider(paymentOptions);
            var payments = new PaymentService(
                db,
                billing,
                paymentOptions,
                new IPaymentProvider[] { fakeProvider },
                NullLogger<PaymentService>.Instance);
            await payments.EnsureDefaultsAsync();
            var productId = await db.RechargeProducts
                .Where(x => x.Code == "TEST_CNY_50")
                .Select(x => x.Id)
                .SingleAsync();

            return new PaymentFixture(
                connection,
                db,
                billing,
                payments,
                userId,
                workspaceId,
                productId);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }
}
