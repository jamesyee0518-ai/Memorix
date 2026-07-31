using System.Data;
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

namespace KnowledgeEngine.Infrastructure.Services;

public sealed class AiBillingService : IAiBillingService
{
    private static readonly Guid DefaultPriceVersionId =
        Guid.Parse("b1111111-1111-4111-8111-111111111111");

    private static readonly HashSet<string> SupportedExecutionModes =
    [
        AiExecutionModes.Local,
        AiExecutionModes.UserByok,
        AiExecutionModes.MemorixCloud
    ];

    private static readonly HashSet<string> AllowedRawUsageFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "prompt_tokens",
        "completion_tokens",
        "total_tokens",
        "input_tokens",
        "output_tokens",
        "cached_tokens",
        "cache_read_input_tokens",
        "cache_creation_input_tokens",
        "reasoning_tokens",
        "embedding_tokens",
        "request_id",
        "usage_type",
        "quantity"
    };

    private readonly AppDbContext _db;
    private readonly BillingSettings _settings;
    private readonly ILogger<AiBillingService> _logger;

    public AiBillingService(
        AppDbContext db,
        IOptions<BillingSettings> settings,
        ILogger<AiBillingService> logger)
    {
        _db = db;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task EnsureDefaultsAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        if (!await _db.PricePlanVersions.AnyAsync(x => x.Id == DefaultPriceVersionId, ct))
        {
            _db.PricePlanVersions.Add(new PricePlanVersion
            {
                Id = DefaultPriceVersionId,
                Code = "memorix-default",
                Version = 1,
                Currency = NormalizeCurrency(_settings.Currency),
                Status = PriceVersionStatuses.Published,
                EffectiveFrom = now,
                CreatedAt = now
            });
        }

        foreach (var meter in _settings.Meters.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            var ruleId = DeterministicGuid($"memorix-default-v1:{meter.Key}");
            if (await _db.PriceRules.AnyAsync(x => x.Id == ruleId, ct))
            {
                continue;
            }
            _db.PriceRules.Add(new PriceRule
            {
                Id = ruleId,
                PricePlanVersionId = DefaultPriceVersionId,
                MeterType = NormalizeMeterType(meter.Key),
                Unit = meter.Value.Unit,
                UnitSize = EnsurePositive(meter.Value.UnitSize, 1m),
                CreditRate = Math.Max(0m, meter.Value.CreditRate),
                SaleUnitPrice = Math.Max(0m, meter.Value.SaleUnitPrice),
                ProviderUnitCost = Math.Max(0m, meter.Value.ProviderUnitCost),
                ProviderCurrency = NormalizeCurrency(meter.Value.ProviderCurrency),
                CreatedAt = now
            });
        }

        await _db.SaveChangesAsync(ct);
    }

    public async Task<BillingEntitlementsResponse> GetEntitlementsAsync(
        Guid userId,
        Guid workspaceId,
        CancellationToken ct = default)
    {
        var account = await ResolveAccountAsync(userId, workspaceId, ct);
        var user = await _db.Users.AsNoTracking().SingleAsync(x => x.Id == userId, ct);
        var now = DateTime.UtcNow;

        var enabledByPlan = _settings.CloudAiEnabledPlanCodes.Contains(
            user.PlanCode,
            StringComparer.OrdinalIgnoreCase);
        var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["cloud_ai.enabled"] = enabledByPlan,
            ["cloud_ai.monthly_credits"] = _settings.DefaultMonthlyCredits,
            ["cloud_ai.pay_as_you_go"] = false,
            ["cloud_ai.premium_models"] = false,
            ["cloud_ai.single_job_credit_limit"] = null
        };

        var overrides = await _db.AccountEntitlements
            .AsNoTracking()
            .Where(x =>
                x.BillingAccountId == account.Id &&
                x.EffectiveFrom <= now &&
                (!x.EffectiveTo.HasValue || x.EffectiveTo > now))
            .OrderBy(x => x.EffectiveFrom)
            .ToListAsync(ct);

        foreach (var entitlement in overrides)
        {
            values[entitlement.EntitlementKey] = DeserializeEntitlement(entitlement.ValueJson);
        }

        return new BillingEntitlementsResponse(account.Id, workspaceId, values, now);
    }

    public async Task<BillingSummaryResponse> GetSummaryAsync(
        Guid userId,
        Guid workspaceId,
        CancellationToken ct = default)
    {
        var account = await ResolveAccountAsync(userId, workspaceId, ct);
        var now = DateTime.UtcNow;
        var buckets = await _db.QuotaBuckets
            .AsNoTracking()
            .Where(x =>
                x.BillingAccountId == account.Id &&
                x.EffectiveFrom <= now &&
                (!x.ExpiresAt.HasValue || x.ExpiresAt > now))
            .ToListAsync(ct);

        var actualAmount = await _db.BillingCharges
            .AsNoTracking()
            .Where(x => x.BillingAccountId == account.Id && x.Status == "POSTED")
            .SumAsync(x => (decimal?)x.Amount, ct) ?? 0m;
        var granted = buckets.Sum(x => x.GrantedCredits);
        var consumed = buckets.Sum(x => x.ConsumedCredits);
        var reserved = buckets.Sum(x => x.ReservedCredits);

        return new BillingSummaryResponse(
            account.Id,
            workspaceId,
            account.Currency,
            granted,
            consumed,
            reserved,
            Math.Max(0m, granted - consumed - reserved),
            actualAmount,
            true,
            now);
    }

    public async Task<AiJobEstimateResponse> EstimateAsync(
        Guid userId,
        EstimateAiJobRequest request,
        CancellationToken ct = default)
    {
        ValidateEstimateRequest(request);
        await RequireOwnedWorkspaceAsync(userId, request.WorkspaceId, ct);

        var executionMode = NormalizeExecutionMode(request.ExecutionMode);
        if (executionMode != AiExecutionModes.MemorixCloud)
        {
            return new AiJobEstimateResponse(
                request.WorkspaceId,
                null,
                0m,
                0m,
                NormalizeCurrency(_settings.Currency),
                false,
                _settings.ShadowPricingEnabled);
        }

        var quote = await CalculateEstimateAsync(request, ct);
        if (request.BudgetLimit.HasValue && quote.Amount > request.BudgetLimit.Value)
        {
            throw new AppException(
                "budget_exceeded",
                $"Estimated amount {quote.Amount:0.########} exceeds the job budget {request.BudgetLimit.Value:0.########}.");
        }

        return new AiJobEstimateResponse(
            request.WorkspaceId,
            quote.PricePlanVersionId,
            quote.Credits,
            quote.Amount,
            quote.Currency,
            _settings.QuotaEnforcementEnabled,
            _settings.ShadowPricingEnabled);
    }

    public async Task<AiBillingJobResponse> CreateJobAsync(
        Guid userId,
        CreateAiBillingJobRequest request,
        CancellationToken ct = default)
    {
        ValidateCreateRequest(request);
        await RequireOwnedWorkspaceAsync(userId, request.WorkspaceId, ct);

        var existing = await _db.AiJobs
            .AsNoTracking()
            .SingleOrDefaultAsync(x =>
                x.WorkspaceId == request.WorkspaceId &&
                x.ClientJobId == request.ClientJobId, ct);
        if (existing != null)
        {
            return await MapJobAsync(existing, ct);
        }

        var executionMode = NormalizeExecutionMode(request.ExecutionMode);
        BillingAccount? account = null;
        AiJobEstimateResponse estimate;
        var billingMode = executionMode switch
        {
            AiExecutionModes.Local => AiBillingModes.LocalFree,
            AiExecutionModes.UserByok => AiBillingModes.UserByok,
            _ => AiBillingModes.PlatformFree
        };

        if (executionMode == AiExecutionModes.MemorixCloud)
        {
            account = await ResolveAccountAsync(userId, request.WorkspaceId, ct);
            var entitlements = await GetEntitlementsAsync(userId, request.WorkspaceId, ct);
            if (_settings.EntitlementEnforcementEnabled &&
                (!entitlements.Entitlements.TryGetValue("cloud_ai.enabled", out var enabledValue) ||
                 enabledValue is not true))
            {
                throw new AppException("entitlement_denied", "The current plan does not include Memorix Cloud AI.");
            }

            estimate = await EstimateAsync(userId, request, ct);
            billingMode = _settings.QuotaEnforcementEnabled
                ? AiBillingModes.CloudIncludedQuota
                : AiBillingModes.PlatformFree;
        }
        else
        {
            estimate = await EstimateAsync(userId, request, ct);
        }

        await using var transaction = await _db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            ct);

        var now = DateTime.UtcNow;
        var job = new AiJob
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            ClientJobId = request.ClientJobId.Trim(),
            WorkspaceId = request.WorkspaceId,
            BillingAccountId = account?.Id,
            PricePlanVersionId = estimate.PricePlanVersionId,
            DeviceId = request.DeviceId,
            JobType = request.JobType.Trim(),
            TargetType = string.IsNullOrWhiteSpace(request.TargetType) ? "billing" : request.TargetType.Trim(),
            TargetId = request.TargetId ?? request.WorkspaceId,
            Status = _settings.QuotaEnforcementEnabled &&
                     executionMode == AiExecutionModes.MemorixCloud
                ? AiJobStatuses.Reserved
                : AiJobStatuses.Pending,
            ExecutionMode = executionMode,
            BillingMode = billingMode,
            Model = request.ModelId,
            DataPolicy = request.DataPolicy,
            ModelPolicy = request.ModelPolicy,
            EstimatedCredits = estimate.EstimatedCredits,
            EstimatedAmount = estimate.EstimatedAmount,
            BudgetLimit = request.BudgetLimit,
            Currency = estimate.Currency,
            CreatedAt = now
        };
        _db.AiJobs.Add(job);

        if (_settings.QuotaEnforcementEnabled &&
            executionMode == AiExecutionModes.MemorixCloud &&
            account != null)
        {
            await ReserveCreditsAsync(account.Id, job, estimate.EstimatedCredits, now, ct);
        }

        await _db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        _logger.LogInformation(
            "Created AI billing job {JobId} for workspace {WorkspaceId} in {ExecutionMode} mode",
            job.Id,
            job.WorkspaceId,
            job.ExecutionMode);
        return await MapJobAsync(job, ct);
    }

    public async Task<AiBillingJobResponse?> GetJobAsync(
        Guid userId,
        Guid workspaceId,
        Guid jobId,
        CancellationToken ct = default)
    {
        await RequireOwnedWorkspaceAsync(userId, workspaceId, ct);
        var job = await _db.AiJobs
            .AsNoTracking()
            .SingleOrDefaultAsync(x =>
                x.Id == jobId &&
                x.WorkspaceId == workspaceId &&
                x.UserId == userId, ct);
        return job == null ? null : await MapJobAsync(job, ct);
    }

    public async Task<UsageEventResponse> RecordUsageAsync(
        RecordUsageEventRequest request,
        CancellationToken ct = default)
    {
        if (!_settings.MeteringEnabled)
        {
            throw new AppException("billing_temporarily_unavailable", "AI usage metering is disabled.");
        }
        if (request.JobId == Guid.Empty ||
            string.IsNullOrWhiteSpace(request.IdempotencyKey) ||
            request.Quantity < 0)
        {
            throw new AppException("invalid_usage_event", "Job, idempotency key, and a non-negative quantity are required.");
        }

        var idempotencyKey = request.IdempotencyKey.Trim();
        var existing = await _db.UsageEvents
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.IdempotencyKey == idempotencyKey, ct);
        if (existing != null)
        {
            return MapUsage(existing, true);
        }

        await using var transaction = await _db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            ct);
        var job = await _db.AiJobs.SingleOrDefaultAsync(x => x.Id == request.JobId, ct)
            ?? throw new AppException("job_not_found", "The AI job does not exist.");

        var meterType = NormalizeMeterType(request.UsageType);
        var quote = job.ExecutionMode == AiExecutionModes.MemorixCloud
            ? await CalculateUsageAsync(
                job.PricePlanVersionId,
                meterType,
                request.Quantity,
                request.ProviderId,
                request.ModelId,
                ct)
            : new UsageQuote(job.PricePlanVersionId, 0m, 0m, job.Currency);
        var now = DateTime.UtcNow;
        var usage = new UsageEvent
        {
            Id = Guid.CreateVersion7(),
            JobId = job.Id,
            TaskId = request.TaskId,
            AttemptId = request.AttemptId,
            WorkspaceId = job.WorkspaceId,
            BillingAccountId = job.BillingAccountId,
            ProviderId = request.ProviderId.Trim(),
            ModelId = request.ModelId.Trim(),
            UsageType = meterType,
            Quantity = request.Quantity,
            Unit = string.IsNullOrWhiteSpace(request.Unit) ? InferUnit(meterType) : request.Unit.Trim(),
            UsageSource = NormalizeUsageSource(request.UsageSource),
            OccurredAt = request.OccurredAt ?? now,
            ReceivedAt = now,
            IdempotencyKey = idempotencyKey,
            RawUsageJson = FilterRawUsage(request.RawUsageJson),
            ReconciliationStatus = string.Equals(
                request.UsageSource,
                UsageSources.Estimated,
                StringComparison.OrdinalIgnoreCase)
                ? "PENDING_RECONCILIATION"
                : "VERIFIED",
            CalculatedCredits = quote.Credits,
            CalculatedAmount = quote.Amount,
            Currency = quote.Currency
        };
        _db.UsageEvents.Add(usage);

        if (meterType == UsageTypes.InputToken)
        {
            job.InputTokens = checked((job.InputTokens ?? 0) + DecimalToInt(request.Quantity));
        }
        else if (meterType == UsageTypes.OutputToken)
        {
            job.OutputTokens = checked((job.OutputTokens ?? 0) + DecimalToInt(request.Quantity));
        }

        job.ActualCredits += quote.Credits;
        job.ActualAmount += quote.Amount;
        if (job.Status is AiJobStatuses.Pending or AiJobStatuses.Reserved)
        {
            job.Status = AiJobStatuses.Running;
            job.StartedAt ??= now;
        }

        if (job.ExecutionMode == AiExecutionModes.MemorixCloud &&
            request.ProviderAmount is > 0m)
        {
            var exchangeRate = request.ExchangeRateSnapshot.GetValueOrDefault(1m);
            if (exchangeRate <= 0m)
            {
                throw new AppException("invalid_exchange_rate", "Exchange rate snapshot must be positive.");
            }

            var providerCurrency = NormalizeCurrency(request.ProviderCurrency ?? "USD");
            var baseCurrency = NormalizeCurrency(request.BaseCurrency ?? _settings.BaseCurrency);
            _db.ProviderCosts.Add(new ProviderCost
            {
                Id = Guid.CreateVersion7(),
                JobId = job.Id,
                AttemptId = request.AttemptId,
                ProviderId = request.ProviderId.Trim(),
                ModelId = request.ModelId.Trim(),
                ProviderAmount = request.ProviderAmount.Value,
                ProviderCurrency = providerCurrency,
                ExchangeRateSnapshot = exchangeRate,
                ExchangeRateSource = string.IsNullOrWhiteSpace(request.ExchangeRateSource)
                    ? "IDENTITY"
                    : request.ExchangeRateSource.Trim(),
                ExchangeRateEffectiveAt = request.OccurredAt ?? now,
                BaseCurrency = baseCurrency,
                BaseCurrencyAmount = RoundAmount(request.ProviderAmount.Value * exchangeRate),
                CostTags = request.CostTags,
                IdempotencyKey = $"{idempotencyKey}:provider-cost",
                CreatedAt = now
            });
        }

        await _db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        _logger.LogInformation(
            "Recorded usage event {UsageEventId} for job {JobId}: {UsageType}={Quantity}",
            usage.Id,
            usage.JobId,
            usage.UsageType,
            usage.Quantity);
        return MapUsage(usage, false);
    }

    public async Task<AiAttemptResponse> StartAttemptAsync(
        StartAiAttemptRequest request,
        CancellationToken ct = default)
    {
        if (request.JobId == Guid.Empty ||
            string.IsNullOrWhiteSpace(request.ProviderId) ||
            string.IsNullOrWhiteSpace(request.RequestedModelId) ||
            string.IsNullOrWhiteSpace(request.ProviderRequestId) ||
            request.AttemptNo < 1)
        {
            throw new AppException(
                "invalid_ai_attempt",
                "Job, provider, model, provider request ID, and a positive attempt number are required.");
        }

        var providerId = request.ProviderId.Trim();
        var providerRequestId = request.ProviderRequestId.Trim();
        var existing = await _db.AiRequestAttempts
            .AsNoTracking()
            .SingleOrDefaultAsync(x =>
                x.ProviderId == providerId &&
                x.ProviderRequestId == providerRequestId &&
                x.AttemptNo == request.AttemptNo, ct);
        if (existing != null)
        {
            return MapAttempt(existing);
        }

        await using var transaction = await _db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            ct);
        var job = await _db.AiJobs.SingleOrDefaultAsync(x => x.Id == request.JobId, ct)
            ?? throw new AppException("job_not_found", "The AI job does not exist.");
        var now = DateTime.UtcNow;
        AiTask task;
        if (request.TaskId.HasValue)
        {
            task = await _db.AiTasks.SingleOrDefaultAsync(x =>
                x.Id == request.TaskId.Value &&
                x.JobId == job.Id, ct)
                ?? throw new AppException("task_not_found", "The AI task does not exist.");
        }
        else
        {
            var nextSequence = (await _db.AiTasks
                .Where(x => x.JobId == job.Id)
                .MaxAsync(x => (int?)x.Sequence, ct) ?? 0) + 1;
            task = new AiTask
            {
                Id = Guid.CreateVersion7(),
                JobId = job.Id,
                TaskType = string.IsNullOrWhiteSpace(request.TaskType)
                    ? "model_call"
                    : request.TaskType.Trim(),
                Status = AiJobStatuses.Running,
                Sequence = nextSequence,
                CreatedAt = now,
                StartedAt = now
            };
            _db.AiTasks.Add(task);
        }

        var attempt = new AiRequestAttempt
        {
            Id = Guid.CreateVersion7(),
            JobId = job.Id,
            TaskId = task.Id,
            ProviderId = providerId,
            RequestedModelId = request.RequestedModelId.Trim(),
            ActualModelId = request.ActualModelId?.Trim(),
            ProviderRequestId = providerRequestId,
            AttemptNo = request.AttemptNo,
            Status = AiJobStatuses.Running,
            IsChargeable = request.IsChargeable,
            CreatedAt = now,
            StartedAt = now
        };
        _db.AiRequestAttempts.Add(attempt);
        if (job.Status is AiJobStatuses.Pending or AiJobStatuses.Reserved)
        {
            job.Status = AiJobStatuses.Running;
            job.StartedAt ??= now;
        }

        await _db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return MapAttempt(attempt);
    }

    public async Task<AiAttemptResponse> CompleteAttemptAsync(
        Guid attemptId,
        CompleteAiAttemptRequest request,
        CancellationToken ct = default)
    {
        var status = request.Status.Trim().ToLowerInvariant();
        if (status is not (AiJobStatuses.Completed or AiJobStatuses.Failed or AiJobStatuses.Cancelled))
        {
            throw new AppException("invalid_attempt_status", "A terminal attempt status is required.");
        }

        var attempt = await _db.AiRequestAttempts.SingleOrDefaultAsync(x => x.Id == attemptId, ct)
            ?? throw new AppException("attempt_not_found", "The AI request attempt does not exist.");
        if (attempt.CompletedAt.HasValue)
        {
            return MapAttempt(attempt);
        }

        var now = DateTime.UtcNow;
        attempt.Status = status;
        attempt.ActualModelId = request.ActualModelId?.Trim() ?? attempt.ActualModelId;
        attempt.HttpStatus = request.HttpStatus;
        attempt.ErrorCode = request.ErrorCode?.Trim();
        attempt.TerminationReason = request.TerminationReason?.Trim();
        attempt.IsChargeable = request.IsChargeable ?? attempt.IsChargeable;
        attempt.CompletedAt = now;

        if (attempt.TaskId.HasValue)
        {
            var task = await _db.AiTasks.SingleAsync(x => x.Id == attempt.TaskId.Value, ct);
            task.Status = status;
            task.CompletedAt = now;
        }

        await _db.SaveChangesAsync(ct);
        return MapAttempt(attempt);
    }

    public async Task<AiBillingJobResponse> CompleteJobAsync(
        Guid jobId,
        CompleteAiJobRequest request,
        CancellationToken ct = default)
    {
        var normalizedStatus = request.Status.Trim().ToLowerInvariant();
        if (normalizedStatus is not (AiJobStatuses.Completed or AiJobStatuses.Failed or AiJobStatuses.Cancelled))
        {
            throw new AppException("invalid_job_status", "A terminal job status is required.");
        }

        await using var transaction = await _db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            ct);
        var job = await _db.AiJobs.SingleOrDefaultAsync(x => x.Id == jobId, ct)
            ?? throw new AppException("job_not_found", "The AI job does not exist.");
        if (job.FinishedAt.HasValue)
        {
            return await MapJobAsync(job, ct);
        }

        var now = DateTime.UtcNow;
        if (_settings.QuotaEnforcementEnabled &&
            job.ExecutionMode == AiExecutionModes.MemorixCloud &&
            job.BillingAccountId.HasValue)
        {
            await SettleReservationAsync(job, now, ct);
        }

        job.Status = normalizedStatus;
        job.ErrorMessage = request.ErrorMessage;
        job.FinishedAt = now;
        job.StartedAt ??= now;

        await _db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        _logger.LogInformation(
            "Completed AI billing job {JobId} with status {Status}, credits {Credits}",
            job.Id,
            job.Status,
            job.ActualCredits);
        return await MapJobAsync(job, ct);
    }

    private async Task<BillingAccount> ResolveAccountAsync(
        Guid userId,
        Guid workspaceId,
        CancellationToken ct)
    {
        await RequireOwnedWorkspaceAsync(userId, workspaceId, ct);
        var binding = await _db.WorkspaceBillingBindings
            .SingleOrDefaultAsync(x => x.WorkspaceId == workspaceId && x.IsActive, ct);
        if (binding != null)
        {
            return await _db.BillingAccounts.SingleAsync(x => x.Id == binding.BillingAccountId, ct);
        }

        var account = await _db.BillingAccounts.SingleOrDefaultAsync(x =>
            x.OwnerUserId == userId &&
            x.AccountType == BillingAccountTypes.Personal &&
            x.Status == BillingAccountStatuses.Active, ct);
        var now = DateTime.UtcNow;
        if (account == null)
        {
            var user = await _db.Users.AsNoTracking().SingleAsync(x => x.Id == userId, ct);
            account = new BillingAccount
            {
                Id = Guid.CreateVersion7(),
                AccountType = BillingAccountTypes.Personal,
                OwnerUserId = userId,
                Name = string.IsNullOrWhiteSpace(user.Nickname) ? user.Email : user.Nickname,
                Currency = NormalizeCurrency(_settings.Currency),
                Status = BillingAccountStatuses.Active,
                CreatedAt = now,
                UpdatedAt = now
            };
            _db.BillingAccounts.Add(account);

            if (_settings.DefaultMonthlyCredits > 0m)
            {
                _db.QuotaBuckets.Add(new QuotaBucket
                {
                    Id = Guid.CreateVersion7(),
                    BillingAccountId = account.Id,
                    Source = QuotaBucketSources.Plan,
                    GrantedCredits = _settings.DefaultMonthlyCredits,
                    EffectiveFrom = now,
                    ExpiresAt = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(1),
                    Priority = 100,
                    CreatedAt = now,
                    UpdatedAt = now
                });
            }
        }

        _db.WorkspaceBillingBindings.Add(new WorkspaceBillingBinding
        {
            Id = Guid.CreateVersion7(),
            WorkspaceId = workspaceId,
            BillingAccountId = account.Id,
            IsActive = true,
            EffectiveFrom = now,
            CreatedByUserId = userId,
            CreatedAt = now
        });
        await _db.SaveChangesAsync(ct);
        return account;
    }

    private async Task RequireOwnedWorkspaceAsync(
        Guid userId,
        Guid workspaceId,
        CancellationToken ct)
    {
        var ownerId = await _db.Workspaces
            .AsNoTracking()
            .Where(x => x.Id == workspaceId)
            .Select(x => x.UserId)
            .SingleOrDefaultAsync(ct);
        if (!ownerId.HasValue)
        {
            throw new AppException("workspace_not_found", "The workspace does not exist.");
        }
        if (ownerId.Value != userId)
        {
            throw new AppException("workspace_forbidden", "The workspace does not belong to the current user.");
        }
    }

    private async Task<EstimateQuote> CalculateEstimateAsync(
        EstimateAiJobRequest request,
        CancellationToken ct)
    {
        var version = await GetActivePriceVersionAsync(ct);
        var input = await CalculateUsageAsync(
            version.Id,
            UsageTypes.InputToken,
            request.InputTokens,
            request.ProviderId,
            request.ModelId,
            ct);
        var output = await CalculateUsageAsync(
            version.Id,
            UsageTypes.OutputToken,
            request.MaxOutputTokens,
            request.ProviderId,
            request.ModelId,
            ct);
        var embedding = await CalculateUsageAsync(
            version.Id,
            UsageTypes.EmbeddingToken,
            request.EmbeddingTokens,
            request.ProviderId,
            request.ModelId,
            ct);

        return new EstimateQuote(
            version.Id,
            RoundCredits(input.Credits + output.Credits + embedding.Credits),
            RoundAmount(input.Amount + output.Amount + embedding.Amount),
            version.Currency);
    }

    private async Task<PricePlanVersion> GetActivePriceVersionAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var version = await _db.PricePlanVersions
            .AsNoTracking()
            .Where(x =>
                x.Status == PriceVersionStatuses.Published &&
                x.EffectiveFrom <= now &&
                (!x.EffectiveTo.HasValue || x.EffectiveTo > now))
            .OrderByDescending(x => x.EffectiveFrom)
            .ThenByDescending(x => x.Version)
            .FirstOrDefaultAsync(ct);
        if (version != null)
        {
            return version;
        }

        await EnsureDefaultsAsync(ct);
        return await _db.PricePlanVersions
            .AsNoTracking()
            .SingleAsync(x => x.Id == DefaultPriceVersionId, ct);
    }

    private async Task<UsageQuote> CalculateUsageAsync(
        Guid? priceVersionId,
        string meterType,
        decimal quantity,
        string? providerId,
        string? modelId,
        CancellationToken ct)
    {
        if (quantity <= 0m)
        {
            return new UsageQuote(priceVersionId, 0m, 0m, NormalizeCurrency(_settings.Currency));
        }

        var version = priceVersionId.HasValue
            ? await _db.PricePlanVersions.AsNoTracking().SingleAsync(x => x.Id == priceVersionId.Value, ct)
            : await GetActivePriceVersionAsync(ct);
        var rules = await _db.PriceRules
            .AsNoTracking()
            .Where(x => x.PricePlanVersionId == version.Id && x.MeterType == meterType)
            .ToListAsync(ct);
        var rule = rules
            .Where(x =>
                (x.ProviderId == null || string.Equals(x.ProviderId, providerId, StringComparison.OrdinalIgnoreCase)) &&
                (x.ModelId == null || string.Equals(x.ModelId, modelId, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(x => x.ProviderId != null)
            .ThenByDescending(x => x.ModelId != null)
            .FirstOrDefault()
            ?? throw new AppException("price_rule_unavailable", $"No price rule is available for meter {meterType}.");
        var units = quantity / EnsurePositive(rule.UnitSize, 1m);
        return new UsageQuote(
            version.Id,
            RoundCredits(units * rule.CreditRate),
            RoundAmount(units * rule.SaleUnitPrice),
            version.Currency);
    }

    private async Task ReserveCreditsAsync(
        Guid billingAccountId,
        AiJob job,
        decimal credits,
        DateTime now,
        CancellationToken ct)
    {
        if (credits <= 0m)
        {
            return;
        }

        var buckets = await _db.QuotaBuckets
            .Where(x =>
                x.BillingAccountId == billingAccountId &&
                x.EffectiveFrom <= now &&
                (!x.ExpiresAt.HasValue || x.ExpiresAt > now))
            .OrderBy(x => x.ExpiresAt ?? DateTime.MaxValue)
            .ThenBy(x => x.Priority)
            .ThenBy(x => x.CreatedAt)
            .ToListAsync(ct);
        var allocations = new Dictionary<Guid, decimal>();
        var remaining = credits;
        foreach (var bucket in buckets)
        {
            var available = Math.Max(0m, bucket.GrantedCredits - bucket.ConsumedCredits - bucket.ReservedCredits);
            var allocated = Math.Min(available, remaining);
            if (allocated <= 0m)
            {
                continue;
            }
            allocations[bucket.Id] = allocated;
            remaining -= allocated;
            if (remaining <= 0m)
            {
                break;
            }
        }

        if (remaining > 0m)
        {
            throw new AppException(
                "quota_insufficient",
                $"Insufficient cloud credits. Required {credits:0.######}, short by {remaining:0.######}.");
        }

        foreach (var bucket in buckets.Where(x => allocations.ContainsKey(x.Id)))
        {
            bucket.ReservedCredits += allocations[bucket.Id];
            bucket.Version++;
            bucket.UpdatedAt = now;
        }

        var reservation = new BalanceReservation
        {
            Id = Guid.CreateVersion7(),
            BillingAccountId = billingAccountId,
            JobId = job.Id,
            ReservedCredits = credits,
            AllocationJson = JsonSerializer.Serialize(allocations),
            Status = ReservationStatuses.Active,
            IdempotencyKey = $"reserve:{job.WorkspaceId}:{job.ClientJobId}",
            ExpiresAt = now.AddMinutes(Math.Max(1, _settings.ReservationTtlMinutes)),
            CreatedAt = now,
            UpdatedAt = now
        };
        _db.BalanceReservations.Add(reservation);
        _db.AccountLedger.Add(new AccountLedger
        {
            Id = Guid.CreateVersion7(),
            BillingAccountId = billingAccountId,
            BusinessType = "RESERVATION",
            BusinessId = reservation.Id,
            Action = LedgerActions.Reserve,
            Sequence = 1,
            Credits = credits,
            Currency = job.Currency,
            IdempotencyKey = $"{reservation.Id}:reserve",
            CreatedAt = now
        });
    }

    private async Task SettleReservationAsync(AiJob job, DateTime now, CancellationToken ct)
    {
        var reservation = await _db.BalanceReservations.SingleOrDefaultAsync(x =>
            x.JobId == job.Id &&
            x.Status == ReservationStatuses.Active, ct);
        if (reservation == null)
        {
            throw new AppException("reservation_missing", "No active reservation exists for the cloud job.");
        }
        if (job.ActualCredits > reservation.ReservedCredits)
        {
            throw new AppException(
                "settlement_quota_deficit",
                "Actual usage exceeds the reserved credits and requires reconciliation.");
        }

        var allocations = JsonSerializer.Deserialize<Dictionary<Guid, decimal>>(reservation.AllocationJson) ?? [];
        var bucketIds = allocations.Keys.ToList();
        var buckets = await _db.QuotaBuckets
            .Where(x => bucketIds.Contains(x.Id))
            .ToListAsync(ct);
        var remainingToConsume = job.ActualCredits;
        foreach (var allocation in allocations)
        {
            var bucket = buckets.SingleOrDefault(x => x.Id == allocation.Key)
                ?? throw new AppException("quota_bucket_missing", "A reserved quota bucket no longer exists.");
            var consume = Math.Min(allocation.Value, remainingToConsume);
            bucket.ReservedCredits = Math.Max(0m, bucket.ReservedCredits - allocation.Value);
            bucket.ConsumedCredits += consume;
            bucket.Version++;
            bucket.UpdatedAt = now;
            remainingToConsume -= consume;
        }

        reservation.ConsumedCredits = job.ActualCredits;
        reservation.Status = job.ActualCredits > 0m
            ? ReservationStatuses.Consumed
            : ReservationStatuses.Released;
        reservation.UpdatedAt = now;
        var released = Math.Max(0m, reservation.ReservedCredits - job.ActualCredits);
        _db.AccountLedger.Add(new AccountLedger
        {
            Id = Guid.CreateVersion7(),
            BillingAccountId = reservation.BillingAccountId,
            BusinessType = "JOB",
            BusinessId = job.Id,
            Action = LedgerActions.Consume,
            Sequence = 1,
            Credits = job.ActualCredits,
            Amount = job.ActualAmount,
            Currency = job.Currency,
            IdempotencyKey = $"{job.Id}:consume",
            CreatedAt = now
        });
        if (released > 0m)
        {
            _db.AccountLedger.Add(new AccountLedger
            {
                Id = Guid.CreateVersion7(),
                BillingAccountId = reservation.BillingAccountId,
                BusinessType = "JOB",
                BusinessId = job.Id,
                Action = LedgerActions.Release,
                Sequence = 2,
                Credits = released,
                Currency = job.Currency,
                IdempotencyKey = $"{job.Id}:release",
                CreatedAt = now
            });
        }

        if (!_settings.ShadowPricingEnabled &&
            job.PricePlanVersionId.HasValue &&
            (job.ActualCredits > 0m || job.ActualAmount > 0m))
        {
            _db.BillingCharges.Add(new BillingCharge
            {
                Id = Guid.CreateVersion7(),
                BillingAccountId = reservation.BillingAccountId,
                JobId = job.Id,
                PricePlanVersionId = job.PricePlanVersionId.Value,
                Credits = job.ActualCredits,
                Amount = job.ActualAmount,
                Currency = job.Currency,
                Status = "POSTED",
                IdempotencyKey = $"{job.Id}:{job.PricePlanVersionId}:AI_USAGE",
                CreatedAt = now
            });
        }
    }

    private async Task<AiBillingJobResponse> MapJobAsync(AiJob job, CancellationToken ct)
    {
        var reservationId = await _db.BalanceReservations
            .AsNoTracking()
            .Where(x => x.JobId == job.Id)
            .Select(x => (Guid?)x.Id)
            .FirstOrDefaultAsync(ct);
        return new AiBillingJobResponse(
            job.Id,
            job.ClientJobId ?? job.Id.ToString("N"),
            job.WorkspaceId ?? Guid.Empty,
            job.BillingAccountId,
            job.JobType,
            job.ExecutionMode,
            job.BillingMode,
            job.Status,
            job.EstimatedCredits,
            job.ActualCredits,
            job.EstimatedAmount,
            job.ActualAmount,
            job.Currency,
            reservationId,
            job.CreatedAt,
            job.FinishedAt);
    }

    private static UsageEventResponse MapUsage(UsageEvent usage, bool duplicate) =>
        new(
            usage.Id,
            usage.JobId,
            usage.UsageType,
            usage.Quantity,
            usage.CalculatedCredits,
            usage.CalculatedAmount,
            usage.Currency,
            duplicate);

    private static AiAttemptResponse MapAttempt(AiRequestAttempt attempt) =>
        new(
            attempt.Id,
            attempt.JobId,
            attempt.TaskId ?? Guid.Empty,
            attempt.ProviderId,
            attempt.RequestedModelId,
            attempt.ActualModelId,
            attempt.ProviderRequestId ?? string.Empty,
            attempt.AttemptNo,
            attempt.Status,
            attempt.IsChargeable,
            attempt.TerminationReason,
            attempt.CreatedAt,
            attempt.CompletedAt);

    private static void ValidateEstimateRequest(EstimateAiJobRequest request)
    {
        if (request.WorkspaceId == Guid.Empty || string.IsNullOrWhiteSpace(request.JobType))
        {
            throw new AppException("invalid_ai_job", "Workspace and job type are required.");
        }
        if (request.InputTokens < 0m || request.MaxOutputTokens < 0m || request.EmbeddingTokens < 0m)
        {
            throw new AppException("invalid_ai_job", "Estimated usage cannot be negative.");
        }
    }

    private static void ValidateCreateRequest(CreateAiBillingJobRequest request)
    {
        ValidateEstimateRequest(request);
        if (string.IsNullOrWhiteSpace(request.ClientJobId) || request.ClientJobId.Trim().Length > 160)
        {
            throw new AppException("invalid_client_job_id", "A client job ID of at most 160 characters is required.");
        }
    }

    private static string NormalizeExecutionMode(string mode)
    {
        var normalized = (mode ?? string.Empty).Trim().ToUpperInvariant();
        if (!SupportedExecutionModes.Contains(normalized))
        {
            throw new AppException("invalid_execution_mode", $"Unsupported execution mode: {mode}.");
        }
        return normalized;
    }

    private static string NormalizeMeterType(string meterType)
    {
        var normalized = (meterType ?? string.Empty).Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new AppException("invalid_usage_type", "Usage type is required.");
        }
        return normalized;
    }

    private static string NormalizeUsageSource(string source)
    {
        var normalized = (source ?? string.Empty).Trim().ToUpperInvariant();
        return normalized switch
        {
            UsageSources.Provider => normalized,
            UsageSources.VerifiedGatewayTokenizer => normalized,
            UsageSources.Estimated => normalized,
            UsageSources.ManualAdjustment => normalized,
            _ => throw new AppException("invalid_usage_source", $"Unsupported usage source: {source}.")
        };
    }

    private static string NormalizeCurrency(string currency)
    {
        var normalized = (currency ?? string.Empty).Trim().ToUpperInvariant();
        if (normalized.Length != 3)
        {
            throw new AppException("invalid_currency", $"Invalid currency: {currency}.");
        }
        return normalized;
    }

    private static string InferUnit(string meterType) =>
        meterType.EndsWith("_TOKEN", StringComparison.Ordinal) ? "token" :
        meterType == UsageTypes.OcrPage ? "page" :
        meterType == UsageTypes.AudioSecond ? "second" :
        "unit";

    private static object? DeserializeEntitlement(string valueJson)
    {
        try
        {
            using var document = JsonDocument.Parse(valueJson);
            return document.RootElement.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Number when document.RootElement.TryGetDecimal(out var number) => number,
                JsonValueKind.String => document.RootElement.GetString(),
                JsonValueKind.Null => null,
                _ => document.RootElement.GetRawText()
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? FilterRawUsage(string? rawUsageJson)
    {
        if (string.IsNullOrWhiteSpace(rawUsageJson))
        {
            return null;
        }
        if (rawUsageJson.Length > 8192)
        {
            throw new AppException("raw_usage_too_large", "Raw usage metadata exceeds 8 KiB.");
        }

        try
        {
            using var document = JsonDocument.Parse(rawUsageJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var safe = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (!AllowedRawUsageFields.Contains(property.Name))
                {
                    continue;
                }
                safe[property.Name] = property.Value.ValueKind switch
                {
                    JsonValueKind.Number when property.Value.TryGetDecimal(out var number) => number,
                    JsonValueKind.String when property.Name.Equals("request_id", StringComparison.OrdinalIgnoreCase) =>
                        property.Value.GetString(),
                    JsonValueKind.String when property.Name.Equals("usage_type", StringComparison.OrdinalIgnoreCase) =>
                        property.Value.GetString(),
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    JsonValueKind.Null => null,
                    _ => null
                };
            }
            return safe.Count == 0 ? null : JsonSerializer.Serialize(safe);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static Guid DeterministicGuid(string value)
    {
        var hash = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(value));
        Span<byte> guidBytes = stackalloc byte[16];
        hash.AsSpan(0, 16).CopyTo(guidBytes);
        guidBytes[6] = (byte)((guidBytes[6] & 0x0F) | 0x40);
        guidBytes[8] = (byte)((guidBytes[8] & 0x3F) | 0x80);
        return new Guid(guidBytes);
    }

    private static decimal EnsurePositive(decimal value, decimal fallback) =>
        value > 0m ? value : fallback;

    private static decimal RoundCredits(decimal value) =>
        Math.Ceiling(Math.Max(0m, value) * 1_000_000m) / 1_000_000m;

    private static decimal RoundAmount(decimal value) =>
        decimal.Round(Math.Max(0m, value), 8, MidpointRounding.AwayFromZero);

    private static int DecimalToInt(decimal value) =>
        value > int.MaxValue
            ? throw new AppException("usage_quantity_too_large", "Token quantity exceeds the supported range.")
            : decimal.ToInt32(decimal.Truncate(value));

    private sealed record EstimateQuote(
        Guid PricePlanVersionId,
        decimal Credits,
        decimal Amount,
        string Currency);

    private sealed record UsageQuote(
        Guid? PricePlanVersionId,
        decimal Credits,
        decimal Amount,
        string Currency);
}
