using KnowledgeEngine.Application.DTOs;
using KnowledgeEngine.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace KnowledgeEngine.Api.Controllers;

[ApiController]
[Route("api/workspaces/{workspaceId:guid}/inbox")]
[Authorize]
public sealed class WorkspaceInboxController : BaseController
{
    private readonly IKnowledgeRepository _repo;
    private readonly IWorkspaceAuthorizationService _workspaceAuthorization;

    public WorkspaceInboxController(
        IKnowledgeRepository repo,
        IWorkspaceAuthorizationService workspaceAuthorization)
    {
        _repo = repo;
        _workspaceAuthorization = workspaceAuthorization;
    }

    [HttpGet("changes")]
    public async Task<IActionResult> Changes(
        Guid workspaceId,
        [FromQuery] string? cursor,
        [FromQuery] int limit = 100,
        CancellationToken ct = default)
    {
        var access = await _workspaceAuthorization.AuthorizeAsync(workspaceId, ct);
        if (access == WorkspaceAccessResult.NotFound)
        {
            return NotFound(ApiResponse<object>.FailObject(
                "WORKSPACE_NOT_FOUND", "云端工作区不存在", GetTraceId()));
        }
        if (access != WorkspaceAccessResult.Allowed)
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                ApiResponse<object>.FailObject(
                    "WORKSPACE_FORBIDDEN",
                    "无权访问该云端工作区",
                    GetTraceId()));
        }

        limit = Math.Clamp(limit, 1, 500);
        var offset = DecodeCursor(cursor);
        var items = await _repo.ListInboxItemsAsync(
            workspaceId.ToString(), null, null, null, limit, offset, ct);
        var hasMore = items.Count == limit;
        return Ok(ApiResponse<InboxChangesDto>.Ok(new InboxChangesDto
        {
            Items = items,
            HasMore = hasMore,
            NextCursor = hasMore ? EncodeCursor(offset + limit) : null
        }, GetTraceId()));
    }

    private static string? EncodeCursor(int offset) =>
        Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(offset.ToString()));

    private static int DecodeCursor(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor)) return 0;
        try
        {
            var value = System.Text.Encoding.UTF8.GetString(
                Convert.FromBase64String(cursor));
            return int.TryParse(value, out var offset) && offset >= 0 ? offset : 0;
        }
        catch
        {
            return 0;
        }
    }
}
