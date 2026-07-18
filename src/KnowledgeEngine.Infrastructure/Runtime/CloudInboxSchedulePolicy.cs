namespace KnowledgeEngine.Infrastructure.Runtime;

public static class CloudInboxSchedulePolicy
{
    public static readonly TimeSpan ScheduledInterval = TimeSpan.FromMinutes(30);

    public static bool IsDue(
        string strategy,
        bool startupAttempted,
        DateTime? latestFinishedAt,
        DateTime? retryAt,
        DateTime now)
    {
        if (retryAt.HasValue && retryAt.Value > now) return false;
        if (strategy == "onStartup") return !startupAttempted;
        if (strategy != "scheduled") return false;
        return !latestFinishedAt.HasValue ||
            latestFinishedAt.Value <= now.Subtract(ScheduledInterval);
    }

    public static TimeSpan RetryDelay(int consecutiveFailures)
    {
        var normalized = Math.Max(1, consecutiveFailures);
        return TimeSpan.FromMinutes(
            Math.Min(30, Math.Pow(2, normalized - 1)));
    }

    public static DateTime? NextPullAt(
        string strategy,
        DateTime? latestFinishedAt,
        DateTime? retryAt,
        DateTime now)
    {
        if (retryAt.HasValue) return retryAt;
        if (strategy != "scheduled") return null;
        return latestFinishedAt?.Add(ScheduledInterval) ?? now;
    }
}
