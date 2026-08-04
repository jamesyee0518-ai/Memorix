using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace KnowledgeEngine.Api.Hubs;

/// <summary>
/// SignalR hub for job progress updates (§12.7).
/// Consolidates the /ws/v1/jobs/{jobId}/progress WebSocket endpoint into a single
/// hub connection per client. Clients subscribe to a job group to receive progress
/// events; server-side services push updates via <c>IHubContext&lt;JobProgressHub&gt;</c>.
/// </summary>
[Authorize]
public class JobProgressHub : Hub
{
    private readonly ILogger<JobProgressHub> _logger;

    public JobProgressHub(ILogger<JobProgressHub> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Called when a client connects to the hub. Rejects connections without a user identity.
    /// </summary>
    public override async Task OnConnectedAsync()
    {
        var userId = Context.User?.FindFirst("sub")?.Value;
        if (string.IsNullOrEmpty(userId))
        {
            _logger.LogWarning("JobProgressHub: connection rejected - no user identity ({ConnectionId})",
                Context.ConnectionId);
            Context.Abort();
            return;
        }

        _logger.LogInformation("JobProgressHub: client connected {ConnectionId} for user {UserId}",
            Context.ConnectionId, userId);
        await base.OnConnectedAsync();
    }

    /// <inheritdoc/>
    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation("JobProgressHub: client disconnected {ConnectionId}", Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// Subscribes the calling client to progress updates for the given job.
    /// </summary>
    public async Task SubscribeToJob(Guid jobId)
    {
        var groupName = JobGroup(jobId);
        await Groups.AddToGroupAsync(Context.ConnectionId, groupName);

        _logger.LogInformation("JobProgressHub: {ConnectionId} subscribed to job {JobId}",
            Context.ConnectionId, jobId);

        await Clients.Group(groupName).SendAsync("JobSubscribed", new
        {
            jobId,
            connectionId = Context.ConnectionId
        });
    }

    /// <summary>
    /// Unsubscribes the calling client from progress updates for the given job.
    /// </summary>
    public async Task UnsubscribeFromJob(Guid jobId)
    {
        var groupName = JobGroup(jobId);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);

        _logger.LogInformation("JobProgressHub: {ConnectionId} unsubscribed from job {JobId}",
            Context.ConnectionId, jobId);

        await Clients.Group(groupName).SendAsync("JobUnsubscribed", new
        {
            jobId,
            connectionId = Context.ConnectionId
        });
    }

    /// <summary>
    /// Builds the SignalR group name for a job: <c>job_{jobId}</c>.
    /// </summary>
    public static string JobGroup(Guid jobId) => $"job_{jobId}";
}
