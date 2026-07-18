using System.Security.Claims;
using KnowledgeEngine.Application.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeEngine.Infrastructure.Security;

public sealed class WorkspaceAuthorizationService : IWorkspaceAuthorizationService
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUserContext _currentUser;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public WorkspaceAuthorizationService(
        IAppDbContext db,
        ICurrentUserContext currentUser,
        IHttpContextAccessor httpContextAccessor)
    {
        _db = db;
        _currentUser = currentUser;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task<WorkspaceAccessResult> AuthorizeAsync(
        Guid workspaceId,
        CancellationToken ct = default)
    {
        var ownerId = await _db.Workspaces
            .AsNoTracking()
            .Where(x => x.Id == workspaceId)
            .Select(x => x.UserId)
            .FirstOrDefaultAsync(ct);
        if (!ownerId.HasValue)
        {
            return await _db.Workspaces.AnyAsync(x => x.Id == workspaceId, ct)
                ? WorkspaceAccessResult.Forbidden
                : WorkspaceAccessResult.NotFound;
        }
        if (_currentUser.UserId == ownerId)
        {
            return WorkspaceAccessResult.Allowed;
        }

        var user = _httpContextAccessor.HttpContext?.User;
        var isDevice = string.Equals(
            user?.FindFirstValue("token_type"),
            "mobile_device",
            StringComparison.OrdinalIgnoreCase);
        var deviceWorkspace = user?.FindFirstValue("workspace_id");
        return isDevice &&
            Guid.TryParse(deviceWorkspace, out var tokenWorkspaceId) &&
            tokenWorkspaceId == workspaceId
                ? WorkspaceAccessResult.Allowed
                : WorkspaceAccessResult.Forbidden;
    }
}
