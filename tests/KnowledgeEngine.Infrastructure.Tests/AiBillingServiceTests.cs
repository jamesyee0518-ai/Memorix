using KnowledgeEngine.Application.DTOs;
using KnowledgeEngine.Application.Exceptions;
using KnowledgeEngine.Application.Settings;
using KnowledgeEngine.Domain.Entities;
using KnowledgeEngine.Infrastructure.Db;
using KnowledgeEngine.Infrastructure.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Text.Json;
using Xunit;

namespace KnowledgeEngine.Infrastructure.Tests;

public sealed class AiBillingServiceTests
{
    [Fact]
    public async Task CloudJob_ReservesMetersAndSettlesExactlyOnce()
    {
        await using var fixture = await BillingFixture.CreateAsync(
            defaultCredits: 10_000m,
            quotaEnforcement: true,
            shadowPricing: false);
        var createRequest = new CreateAiBillingJobRequest
        {
            WorkspaceId = fixture.WorkspaceId,
            ClientJobId = "qa-001",
            JobType = "qa",
            ExecutionMode = AiExecutionModes.MemorixCloud,
            ProviderId = "openai",
            ModelId = "test-model",
            InputTokens = 1000m,
            MaxOutputTokens = 1000m
        };

        var created = await fixture.Service.CreateJobAsync(fixture.UserId, createRequest);
        var repeated = await fixture.Service.CreateJobAsync(fixture.UserId, createRequest);

        Assert.Equal(created.JobId, repeated.JobId);
        Assert.Equal(4000m, created.EstimatedCredits);
        Assert.NotNull(created.ReservationId);
        Assert.Single(await fixture.Db.AiJobs.Where(x => x.ClientJobId == "qa-001").ToListAsync());

        var attempt = await fixture.Service.StartAttemptAsync(new StartAiAttemptRequest
        {
            JobId = created.JobId,
            TaskType = "qa_completion",
            ProviderId = "openai",
            RequestedModelId = "test-model",
            ProviderRequestId = "provider-request-1"
        });
        var repeatedAttempt = await fixture.Service.StartAttemptAsync(new StartAiAttemptRequest
        {
            JobId = created.JobId,
            TaskType = "qa_completion",
            ProviderId = "openai",
            RequestedModelId = "test-model",
            ProviderRequestId = "provider-request-1"
        });
        Assert.Equal(attempt.AttemptId, repeatedAttempt.AttemptId);

        var input = await fixture.Service.RecordUsageAsync(new RecordUsageEventRequest
        {
            JobId = created.JobId,
            TaskId = attempt.TaskId,
            AttemptId = attempt.AttemptId,
            ProviderId = "openai",
            ModelId = "test-model",
            UsageType = UsageTypes.InputToken,
            Quantity = 500m,
            IdempotencyKey = "provider-request-1:input",
            ProviderAmount = 1m,
            ProviderCurrency = "USD",
            ExchangeRateSnapshot = 7.2m,
            ExchangeRateSource = "test-fx",
            BaseCurrency = "CNY",
            RawUsageJson = """{"prompt_tokens":500,"prompt":"must-not-be-stored"}"""
        });
        var outputRequest = new RecordUsageEventRequest
        {
            JobId = created.JobId,
            ProviderId = "openai",
            ModelId = "test-model",
            UsageType = UsageTypes.OutputToken,
            Quantity = 500m,
            IdempotencyKey = "provider-request-1:output"
        };
        var output = await fixture.Service.RecordUsageAsync(outputRequest);
        var duplicateOutput = await fixture.Service.RecordUsageAsync(outputRequest);
        var completedAttempt = await fixture.Service.CompleteAttemptAsync(
            attempt.AttemptId,
            new CompleteAiAttemptRequest
            {
                Status = AiJobStatuses.Completed,
                ActualModelId = "test-model-2026",
                HttpStatus = 200
            });

        Assert.Equal(500m, input.CalculatedCredits);
        Assert.Equal(1500m, output.CalculatedCredits);
        Assert.True(duplicateOutput.Duplicate);
        Assert.Equal(2, await fixture.Db.UsageEvents.CountAsync());
        Assert.Equal("test-model-2026", completedAttempt.ActualModelId);
        Assert.Single(await fixture.Db.AiTasks.ToListAsync());
        Assert.Single(await fixture.Db.AiRequestAttempts.ToListAsync());

        var completed = await fixture.Service.CompleteJobAsync(
            created.JobId,
            new CompleteAiJobRequest { Status = AiJobStatuses.Completed });
        var completedAgain = await fixture.Service.CompleteJobAsync(
            created.JobId,
            new CompleteAiJobRequest { Status = AiJobStatuses.Completed });

        Assert.Equal(2000m, completed.ActualCredits);
        Assert.Equal(completed.JobId, completedAgain.JobId);
        var bucket = await fixture.Db.QuotaBuckets.SingleAsync();
        Assert.Equal(2000m, bucket.ConsumedCredits);
        Assert.Equal(0m, bucket.ReservedCredits);
        Assert.Single(await fixture.Db.BillingCharges.ToListAsync());
        Assert.Equal(3, await fixture.Db.AccountLedger.CountAsync());

        var cost = await fixture.Db.ProviderCosts.SingleAsync();
        Assert.Equal(7.2m, cost.BaseCurrencyAmount);
        Assert.Equal("test-fx", cost.ExchangeRateSource);
        var storedUsage = await fixture.Db.UsageEvents.SingleAsync(x => x.Id == input.EventId);
        using var rawUsage = JsonDocument.Parse(storedUsage.RawUsageJson ?? "{}");
        Assert.True(rawUsage.RootElement.TryGetProperty("prompt_tokens", out _));
        Assert.False(rawUsage.RootElement.TryGetProperty("prompt", out _));
    }

    [Fact]
    public async Task LocalAndByokJobs_NeverReserveOrCreateCharges()
    {
        await using var fixture = await BillingFixture.CreateAsync(
            defaultCredits: 10_000m,
            quotaEnforcement: true,
            shadowPricing: false);

        foreach (var mode in new[] { AiExecutionModes.Local, AiExecutionModes.UserByok })
        {
            var job = await fixture.Service.CreateJobAsync(
                fixture.UserId,
                new CreateAiBillingJobRequest
                {
                    WorkspaceId = fixture.WorkspaceId,
                    ClientJobId = $"job-{mode}",
                    JobType = "summary",
                    ExecutionMode = mode,
                    InputTokens = 1000m,
                    MaxOutputTokens = 1000m
                });
            await fixture.Service.RecordUsageAsync(new RecordUsageEventRequest
            {
                JobId = job.JobId,
                ProviderId = mode == AiExecutionModes.UserByok ? "user-provider" : "local",
                ModelId = "test",
                UsageType = UsageTypes.InputToken,
                Quantity = 1000m,
                IdempotencyKey = $"{mode}:input"
            });
            var completed = await fixture.Service.CompleteJobAsync(
                job.JobId,
                new CompleteAiJobRequest { Status = AiJobStatuses.Completed });

            Assert.Equal(0m, completed.ActualCredits);
            Assert.Null(completed.ReservationId);
        }

        Assert.Empty(await fixture.Db.BalanceReservations.ToListAsync());
        Assert.Empty(await fixture.Db.BillingCharges.ToListAsync());
        Assert.Empty(await fixture.Db.ProviderCosts.ToListAsync());
    }

    [Fact]
    public async Task CloudJob_WithInsufficientQuota_IsRejectedBeforeJobPersistence()
    {
        await using var fixture = await BillingFixture.CreateAsync(
            defaultCredits: 1000m,
            quotaEnforcement: true,
            shadowPricing: false);

        var exception = await Assert.ThrowsAsync<AppException>(() =>
            fixture.Service.CreateJobAsync(
                fixture.UserId,
                new CreateAiBillingJobRequest
                {
                    WorkspaceId = fixture.WorkspaceId,
                    ClientJobId = "too-expensive",
                    JobType = "qa",
                    ExecutionMode = AiExecutionModes.MemorixCloud,
                    InputTokens = 1000m,
                    MaxOutputTokens = 1000m
                }));

        Assert.Equal("quota_insufficient", exception.Code);
        Assert.Empty(await fixture.Db.AiJobs.Where(x => x.ClientJobId == "too-expensive").ToListAsync());
        var bucket = await fixture.Db.QuotaBuckets.SingleAsync();
        Assert.Equal(0m, bucket.ReservedCredits);
        Assert.Equal(0m, bucket.ConsumedCredits);
    }

    [Fact]
    public async Task ExpiredReservation_IsReleasedAndJobIsFailed()
    {
        await using var fixture = await BillingFixture.CreateAsync(
            defaultCredits: 10_000m,
            quotaEnforcement: true,
            shadowPricing: true);
        var job = await fixture.Service.CreateJobAsync(
            fixture.UserId,
            new CreateAiBillingJobRequest
            {
                WorkspaceId = fixture.WorkspaceId,
                ClientJobId = "expires",
                JobType = "batch",
                ExecutionMode = AiExecutionModes.MemorixCloud,
                InputTokens = 1000m,
                MaxOutputTokens = 1000m
            });
        var reservation = await fixture.Db.BalanceReservations.SingleAsync(x => x.JobId == job.JobId);
        reservation.ExpiresAt = DateTime.UtcNow.AddMinutes(-1);
        await fixture.Db.SaveChangesAsync();

        var maintenance = new BillingMaintenanceService(
            fixture.Db,
            Options.Create(new BillingSettings { Currency = "CNY" }));
        var released = await maintenance.ReleaseExpiredReservationsAsync();

        Assert.Equal(1, released);
        Assert.Equal(ReservationStatuses.Expired, reservation.Status);
        Assert.Equal(0m, (await fixture.Db.QuotaBuckets.SingleAsync()).ReservedCredits);
        var expiredJob = await fixture.Db.AiJobs.SingleAsync(x => x.Id == job.JobId);
        Assert.Equal(AiJobStatuses.Failed, expiredJob.Status);
        Assert.Equal("billing_reservation_expired", expiredJob.ErrorMessage);
    }

    private sealed class BillingFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private BillingFixture(
            SqliteConnection connection,
            AppDbContext db,
            AiBillingService service,
            Guid userId,
            Guid workspaceId)
        {
            _connection = connection;
            Db = db;
            Service = service;
            UserId = userId;
            WorkspaceId = workspaceId;
        }

        public AppDbContext Db { get; }
        public AiBillingService Service { get; }
        public Guid UserId { get; }
        public Guid WorkspaceId { get; }

        public static async Task<BillingFixture> CreateAsync(
            decimal defaultCredits,
            bool quotaEnforcement,
            bool shadowPricing)
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
                Email = "billing@example.com",
                Nickname = "Billing Test",
                PasswordHash = "test",
                PlanCode = "pro",
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
                Name = "Billing Workspace",
                Mode = "cloud",
                StorageProvider = "postgres",
                FileProvider = "minio",
                JobProvider = "redis",
                ModelProvider = "openai",
                CreatedAt = now,
                UpdatedAt = now
            });
            await db.SaveChangesAsync();

            var settings = new BillingSettings
            {
                MeteringEnabled = true,
                EntitlementEnforcementEnabled = true,
                QuotaEnforcementEnabled = quotaEnforcement,
                ShadowPricingEnabled = shadowPricing,
                DefaultMonthlyCredits = defaultCredits,
                Currency = "CNY",
                BaseCurrency = "CNY"
            };
            var service = new AiBillingService(
                db,
                Options.Create(settings),
                NullLogger<AiBillingService>.Instance);
            await service.EnsureDefaultsAsync();
            return new BillingFixture(connection, db, service, userId, workspaceId);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }
}
