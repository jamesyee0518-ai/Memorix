using KnowledgeEngine.Application.DTOs;
using KnowledgeEngine.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace KnowledgeEngine.Api.Controllers;

[ApiController]
[Route("api/mobile/push-notifications")]
[Authorize]
public class PushNotificationsController : BaseController
{
    private readonly IConfigService _configService;
    private readonly IKnowledgeRepository _repo;

    public PushNotificationsController(IConfigService configService, IKnowledgeRepository repo)
    {
        _configService = configService;
        _repo = repo;
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? status, [FromQuery] int limit = 50, CancellationToken ct = default)
    {
        var wsId = await _configService.GetCurrentWorkspaceIdAsync(ct);
        if (wsId == null)
        {
            return BadRequest(ApiResponse<object>.FailObject("NO_WORKSPACE", "未找到活动工作区", GetTraceId()));
        }

        var notifications = await _repo.ListPushNotificationsAsync(
            wsId,
            string.IsNullOrWhiteSpace(status) || status == "all" ? null : status,
            Math.Clamp(limit, 1, 200),
            ct);

        return Ok(ApiResponse<List<PushNotificationDto>>.Ok(notifications, GetTraceId()));
    }
}
