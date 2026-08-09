using KnowledgeEngine.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeEngine.Api.Hubs;

/// <summary>Real-time events for a user's own media jobs only.</summary>
[Authorize]
public sealed class MediaJobHub : Hub
{
    private readonly IAppDbContext _db;

    public MediaJobHub(IAppDbContext db)
    {
        _db = db;
    }

    public async Task Subscribe(Guid jobId)
    {
        var subject = Context.User?.FindFirst("sub")?.Value;
        if (!Guid.TryParse(subject, out var userId))
            throw new HubException("未认证的用户");
        var owned = await _db.MediaJobs.AnyAsync(x => x.Id == jobId && x.UserId == userId, Context.ConnectionAborted);
        if (!owned)
            throw new HubException("媒体任务不存在或无权订阅");
        await Groups.AddToGroupAsync(Context.ConnectionId, Group(jobId), Context.ConnectionAborted);
    }

    public Task Unsubscribe(Guid jobId) => Groups.RemoveFromGroupAsync(Context.ConnectionId, Group(jobId), Context.ConnectionAborted);

    public static string Group(Guid jobId) => $"media_job_{jobId:N}";
}
