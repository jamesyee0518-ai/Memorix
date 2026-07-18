using System.Collections.Concurrent;

namespace KnowledgeEngine.Infrastructure.Runtime;

public sealed class CloudInboxScheduleMonitor
{
    private readonly ConcurrentDictionary<Guid, ScheduleState> _states = new();
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _running = new();
    private readonly ConcurrentDictionary<Guid, byte> _retryRequests = new();

    public ScheduleState Get(Guid workspaceId) =>
        _states.TryGetValue(workspaceId, out var state)
            ? state
            : new ScheduleState();

    public void Update(Guid workspaceId, Func<ScheduleState, ScheduleState> update) =>
        _states.AddOrUpdate(workspaceId, _ => update(new ScheduleState()), (_, state) => update(state));

    public CancellationToken Begin(Guid workspaceId, CancellationToken hostToken)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(hostToken);
        if (!_running.TryAdd(workspaceId, cts))
        {
            cts.Dispose();
            throw new InvalidOperationException("该工作区的自动拉取已经在运行。");
        }
        Update(workspaceId, state => state with
        {
            IsRunning = true,
            StartedAt = DateTime.UtcNow,
            LastError = null
        });
        return cts.Token;
    }

    public void End(Guid workspaceId)
    {
        if (_running.TryRemove(workspaceId, out var cts)) cts.Dispose();
        Update(workspaceId, state => state with { IsRunning = false, StartedAt = null });
    }

    public bool Cancel(Guid workspaceId)
    {
        if (!_running.TryGetValue(workspaceId, out var cts)) return false;
        cts.Cancel();
        return true;
    }

    public void RequestRetry(Guid workspaceId) => _retryRequests[workspaceId] = 0;

    public bool ConsumeRetry(Guid workspaceId) =>
        _retryRequests.TryRemove(workspaceId, out _);
}

public sealed record ScheduleState
{
    public bool WorkerActive { get; init; } = true;
    public bool IsRunning { get; init; }
    public DateTime? StartedAt { get; init; }
    public DateTime? NextPullAt { get; init; }
    public int ConsecutiveFailures { get; init; }
    public DateTime? RetryAt { get; init; }
    public string? LastError { get; init; }
}
