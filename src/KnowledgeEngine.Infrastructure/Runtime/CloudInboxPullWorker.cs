using KnowledgeEngine.Application.DTOs;
using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Application.Services;
using KnowledgeEngine.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace KnowledgeEngine.Infrastructure.Runtime;

public sealed class CloudInboxPullWorker : BackgroundService
{
    private const string PullStrategySetting = "cloud_inbox_pull_strategy";
    private const string RetentionSetting = "cloud_inbox_retention";
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(10);
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CloudInboxPullWorker> _logger;
    private readonly CloudInboxScheduleMonitor _monitor;
    private readonly HashSet<Guid> _startupAttempts = [];
    private readonly Dictionary<Guid, RetryState> _retryStates = [];

    public CloudInboxPullWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<CloudInboxPullWorker> logger,
        CloudInboxScheduleMonitor monitor)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _monitor = monitor;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunCycleAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Cloud Inbox automatic pull cycle failed");
            }
            await Task.Delay(PollInterval, stoppingToken);
        }
    }

    private async Task RunCycleAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<IKnowledgeRepository>();
        var bindingService = scope.ServiceProvider.GetRequiredService<IBindingService>();
        var syncService = scope.ServiceProvider.GetRequiredService<CloudInboxSyncService>();

        var candidates = await (
            from binding in db.WorkspaceBindings
            join workspace in db.Workspaces on binding.LocalWorkspaceId equals workspace.Id
            join account in db.CloudAccountBindings on binding.CloudAccountBindingId equals account.Id
            where binding.BindingStatus == "active"
                && account.BindingStatus == "active"
                && binding.SyncMode == SyncModes.InboxOnly
                && workspace.InboxEnabled
            select new PullCandidate(
                workspace.Id,
                binding.CloudAccountBindingId,
                account.CloudApiBaseUrl,
                binding.CloudWorkspaceId))
            .ToListAsync(ct);

        foreach (var candidate in candidates)
        {
            var workspaceId = candidate.WorkspaceId.ToString();
            var strategy = await repo.GetSettingAsync(
                workspaceId, PullStrategySetting, ct) ?? "manual";
            var forcedRetry = _monitor.ConsumeRetry(candidate.WorkspaceId);
            if (forcedRetry)
            {
                _retryStates.Remove(candidate.WorkspaceId);
            }
            if (!forcedRetry &&
                !await IsDueAsync(candidate.WorkspaceId, workspaceId, strategy, repo, ct))
            {
                await UpdateNextPullAsync(candidate.WorkspaceId, workspaceId, strategy, repo, ct);
                continue;
            }

            _startupAttempts.Add(candidate.WorkspaceId);
            await PullAsync(candidate, repo, bindingService, syncService, ct);
        }
    }

    private async Task<bool> IsDueAsync(
        Guid workspaceId,
        string workspaceIdText,
        string strategy,
        IKnowledgeRepository repo,
        CancellationToken ct)
    {
        var latest = (await repo.ListCloudInboxSyncLogsAsync(
            workspaceIdText, 1, ct)).FirstOrDefault();
        return CloudInboxSchedulePolicy.IsDue(
            strategy,
            _startupAttempts.Contains(workspaceId),
            latest?.FinishedAt,
            _retryStates.TryGetValue(workspaceId, out var retry)
                ? retry.NextAttemptAt
                : null,
            DateTime.UtcNow);
    }

    private async Task UpdateNextPullAsync(
        Guid workspaceId,
        string workspaceIdText,
        string strategy,
        IKnowledgeRepository repo,
        CancellationToken ct)
    {
        var latest = (await repo.ListCloudInboxSyncLogsAsync(
            workspaceIdText, 1, ct)).FirstOrDefault();
        var nextPullAt = CloudInboxSchedulePolicy.NextPullAt(
            strategy,
            latest?.FinishedAt,
            _retryStates.TryGetValue(workspaceId, out var retry)
                ? retry.NextAttemptAt
                : null,
            DateTime.UtcNow);
        _monitor.Update(workspaceId, state => state with { NextPullAt = nextPullAt });
    }

    private async Task PullAsync(
        PullCandidate candidate,
        IKnowledgeRepository repo,
        IBindingService bindingService,
        CloudInboxSyncService syncService,
        CancellationToken ct)
    {
        var workspaceId = candidate.WorkspaceId.ToString();
        var retention = await repo.GetSettingAsync(
            workspaceId, RetentionSetting, ct) ?? "keep";
        var startedAt = DateTime.UtcNow;
        var pullToken = _monitor.Begin(candidate.WorkspaceId, ct);
        try
        {
            var token = await bindingService.GetAccessTokenAsync(
                candidate.CloudAccountBindingId, pullToken);
            if (string.IsNullOrWhiteSpace(token))
            {
                throw new InvalidOperationException(
                    "云端账号访问令牌不存在，请重新登录。");
            }
            var result = await syncService.PullAsync(
                workspaceId,
                candidate.CloudApiBaseUrl,
                candidate.CloudWorkspaceId,
                token,
                retention,
                pullToken);
            var finishedAt = DateTime.UtcNow;
            await repo.CreateCloudInboxSyncLogAsync(new CreateCloudInboxSyncLogInput
            {
                WorkspaceId = workspaceId,
                Direction = "pull",
                Status = result.FailedCount > 0 ? "partial" : "success",
                CloudApiBaseUrl = candidate.CloudApiBaseUrl,
                CloudWorkspaceId = candidate.CloudWorkspaceId,
                Retention = retention,
                PulledCount = result.PulledCount,
                FailedCount = result.FailedCount,
                NextCursor = result.NextCursor,
                StartedAt = startedAt,
                FinishedAt = finishedAt
            }, pullToken);
            _retryStates.Remove(candidate.WorkspaceId);
            _monitor.Update(candidate.WorkspaceId, state => state with
            {
                ConsecutiveFailures = 0,
                RetryAt = null,
                NextPullAt = DateTime.UtcNow.Add(
                    CloudInboxSchedulePolicy.ScheduledInterval),
                LastError = null
            });
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _monitor.Update(candidate.WorkspaceId, state => state with
            {
                NextPullAt = null,
                LastError = "自动拉取已取消"
            });
            _logger.LogInformation(
                "Automatic Cloud Inbox pull cancelled for workspace {WorkspaceId}",
                workspaceId);
        }
        catch (Exception ex)
        {
            var finishedAt = DateTime.UtcNow;
            await repo.CreateCloudInboxSyncLogAsync(new CreateCloudInboxSyncLogInput
            {
                WorkspaceId = workspaceId,
                Direction = "pull",
                Status = "failed",
                CloudApiBaseUrl = candidate.CloudApiBaseUrl,
                CloudWorkspaceId = candidate.CloudWorkspaceId,
                Retention = retention,
                FailedCount = 1,
                ErrorMessage = ex.Message,
                StartedAt = startedAt,
                FinishedAt = finishedAt
            }, CancellationToken.None);
            var failures = _retryStates.TryGetValue(candidate.WorkspaceId, out var state)
                ? state.Failures + 1
                : 1;
            var retryDelay = CloudInboxSchedulePolicy.RetryDelay(failures);
            _retryStates[candidate.WorkspaceId] = new RetryState(
                failures, DateTime.UtcNow.Add(retryDelay));
            _monitor.Update(candidate.WorkspaceId, state => state with
            {
                ConsecutiveFailures = failures,
                RetryAt = DateTime.UtcNow.Add(retryDelay),
                NextPullAt = DateTime.UtcNow.Add(retryDelay),
                LastError = ex.Message
            });
            _logger.LogWarning(
                ex,
                "Automatic Cloud Inbox pull failed for workspace {WorkspaceId}; retry in {DelayMinutes} minute(s)",
                workspaceId,
                retryDelay.TotalMinutes);
        }
        finally
        {
            _monitor.End(candidate.WorkspaceId);
        }
    }

    private sealed record PullCandidate(
        Guid WorkspaceId,
        Guid CloudAccountBindingId,
        string CloudApiBaseUrl,
        string CloudWorkspaceId);

    private sealed record RetryState(int Failures, DateTime NextAttemptAt);
}
