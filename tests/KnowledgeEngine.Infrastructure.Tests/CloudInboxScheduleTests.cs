using KnowledgeEngine.Infrastructure.Runtime;
using Xunit;

namespace KnowledgeEngine.Infrastructure.Tests;

public class CloudInboxScheduleTests
{
    private static readonly DateTime Now = new(2026, 7, 17, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void OnStartup_IsDueOnlyBeforeFirstAttempt()
    {
        Assert.True(CloudInboxSchedulePolicy.IsDue(
            "onStartup", false, null, null, Now));
        Assert.False(CloudInboxSchedulePolicy.IsDue(
            "onStartup", true, null, null, Now));
    }

    [Fact]
    public void Scheduled_IsDueAfterThirtyMinutes()
    {
        Assert.False(CloudInboxSchedulePolicy.IsDue(
            "scheduled", false, Now.AddMinutes(-29), null, Now));
        Assert.True(CloudInboxSchedulePolicy.IsDue(
            "scheduled", false, Now.AddMinutes(-30), null, Now));
        Assert.True(CloudInboxSchedulePolicy.IsDue(
            "scheduled", false, null, null, Now));
    }

    [Fact]
    public void RetryWindow_TakesPriorityOverNormalSchedule()
    {
        Assert.False(CloudInboxSchedulePolicy.IsDue(
            "scheduled", false, Now.AddHours(-1), Now.AddMinutes(2), Now));
        Assert.True(CloudInboxSchedulePolicy.IsDue(
            "scheduled", false, Now.AddHours(-1), Now.AddSeconds(-1), Now));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 4)]
    [InlineData(4, 8)]
    [InlineData(5, 16)]
    [InlineData(6, 30)]
    [InlineData(20, 30)]
    public void RetryDelay_UsesCappedExponentialBackoff(int failures, int expectedMinutes)
    {
        Assert.Equal(
            TimeSpan.FromMinutes(expectedMinutes),
            CloudInboxSchedulePolicy.RetryDelay(failures));
    }

    [Fact]
    public void RetryRequest_IsConsumedOnce()
    {
        var monitor = new CloudInboxScheduleMonitor();
        var workspaceId = Guid.NewGuid();

        monitor.RequestRetry(workspaceId);

        Assert.True(monitor.ConsumeRetry(workspaceId));
        Assert.False(monitor.ConsumeRetry(workspaceId));
    }

    [Fact]
    public void Begin_PreventsConcurrentRuns()
    {
        var monitor = new CloudInboxScheduleMonitor();
        var workspaceId = Guid.NewGuid();

        monitor.Begin(workspaceId, CancellationToken.None);

        Assert.Throws<InvalidOperationException>(() =>
            monitor.Begin(workspaceId, CancellationToken.None));
        monitor.End(workspaceId);
        var token = monitor.Begin(workspaceId, CancellationToken.None);
        Assert.False(token.IsCancellationRequested);
        monitor.End(workspaceId);
    }

    [Fact]
    public void Cancel_CancelsRunningTokenAndClearsOnEnd()
    {
        var monitor = new CloudInboxScheduleMonitor();
        var workspaceId = Guid.NewGuid();
        var token = monitor.Begin(workspaceId, CancellationToken.None);

        Assert.True(monitor.Cancel(workspaceId));
        Assert.True(token.IsCancellationRequested);
        monitor.End(workspaceId);
        Assert.False(monitor.Get(workspaceId).IsRunning);
        Assert.False(monitor.Cancel(workspaceId));
    }
}
