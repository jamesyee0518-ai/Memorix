using KnowledgeEngine.Application.DTOs;
using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace KnowledgeEngine.Api.Controllers;

/// <summary>
/// Mobile capture API (§16.2).
///
/// Accepts quick-capture submissions from mobile devices (and other thin
/// clients). Each endpoint creates an inbox item with createdFrom="mobile" and
/// records the originating client id. All creation is delegated to
/// <see cref="ImportService"/> so the existing inbox + event workflow is reused.
/// </summary>
[ApiController]
[Route("api/mobile/capture")]
[Authorize]
public class MobileCaptureController : BaseController
{
    private readonly ImportService _importService;
    private readonly IConfigService _configService;
    private readonly IKnowledgeRepository _repo;

    public MobileCaptureController(
        ImportService importService,
        IConfigService configService,
        IKnowledgeRepository repo)
    {
        _importService = importService;
        _configService = configService;
        _repo = repo;
    }

    /// <summary>
    /// Lists recent capture submissions for one mobile client.
    /// </summary>
    [HttpGet("status")]
    public async Task<IActionResult> ListStatus([FromQuery] string? clientId, [FromQuery] int limit = 50, CancellationToken ct = default)
    {
        var wsId = await _configService.GetCurrentWorkspaceIdAsync(ct);
        if (wsId == null)
        {
            return BadRequest(ApiResponse<object>.FailObject("NO_WORKSPACE", "未找到活动工作区", GetTraceId()));
        }

        var resolvedClient = await ResolveMobileClientIdAsync(wsId, clientId, ct);
        if (resolvedClient.Error != null)
        {
            return resolvedClient.Error;
        }

        var items = await _repo.ListMobileCaptureItemsAsync(
            wsId,
            resolvedClient.ClientId!,
            Math.Clamp(limit, 1, 100),
            ct);

        return Ok(ApiResponse<List<InboxItemDto>>.Ok(items, GetTraceId()));
    }

    /// <summary>
    /// Capture text content from a mobile device.
    /// Creates an inbox item with inputType="text", createdFrom="mobile".
    /// </summary>
    [HttpPost("text")]
    public async Task<IActionResult> CaptureText([FromBody] MobileCaptureTextDto input, CancellationToken ct)
    {
        var wsId = await _configService.GetCurrentWorkspaceIdAsync(ct);
        if (wsId == null)
        {
            return BadRequest(ApiResponse<object>.FailObject("NO_WORKSPACE", "未找到活动工作区", GetTraceId()));
        }

        if (string.IsNullOrWhiteSpace(input.ContentText))
        {
            return BadRequest(ApiResponse<object>.FailObject("NO_CONTENT", "文本内容不能为空", GetTraceId()));
        }

        var resolvedClient = await ResolveMobileClientIdAsync(wsId, input.ClientId, ct);
        if (resolvedClient.Error != null)
        {
            return resolvedClient.Error;
        }

        var item = await _importService.CreateTextAsync(
            wsId,
            title: null,
            content: input.ContentText,
            topicId: input.TopicId?.ToString(),
            createdFrom: "mobile",
            originDeviceId: resolvedClient.ClientId,
            ct: ct);

        return Ok(ApiResponse<InboxItemDto>.Ok(item, GetTraceId()));
    }

    /// <summary>
    /// Capture a URL from a mobile device.
    /// Creates an inbox item with inputType="url", createdFrom="mobile".
    /// </summary>
    [HttpPost("url")]
    public async Task<IActionResult> CaptureUrl([FromBody] MobileCaptureUrlDto input, CancellationToken ct)
    {
        var wsId = await _configService.GetCurrentWorkspaceIdAsync(ct);
        if (wsId == null)
        {
            return BadRequest(ApiResponse<object>.FailObject("NO_WORKSPACE", "未找到活动工作区", GetTraceId()));
        }

        if (string.IsNullOrWhiteSpace(input.SourceUrl))
        {
            return BadRequest(ApiResponse<object>.FailObject("NO_URL", "链接地址不能为空", GetTraceId()));
        }

        var resolvedClient = await ResolveMobileClientIdAsync(wsId, input.ClientId, ct);
        if (resolvedClient.Error != null)
        {
            return resolvedClient.Error;
        }

        var item = await _importService.CreateUrlAsync(
            wsId,
            url: input.SourceUrl,
            title: input.Title,
            topicId: input.TopicId?.ToString(),
            createdFrom: "mobile",
            originDeviceId: resolvedClient.ClientId,
            ct: ct);

        return Ok(ApiResponse<InboxItemDto>.Ok(item, GetTraceId()));
    }

    /// <summary>
    /// Capture a file upload from a mobile device.
    /// Saves the file and creates an inbox item with inputType="file",
    /// createdFrom="mobile".
    /// </summary>
    [HttpPost("upload")]
    [RequestSizeLimit(100 * 1024 * 1024)] // 100MB max
    public async Task<IActionResult> CaptureUpload([FromForm] MobileCaptureUploadDto input, CancellationToken ct)
    {
        var wsId = await _configService.GetCurrentWorkspaceIdAsync(ct);
        if (wsId == null)
        {
            return BadRequest(ApiResponse<object>.FailObject("NO_WORKSPACE", "未找到活动工作区", GetTraceId()));
        }

        if (input.File == null || input.File.Length == 0)
        {
            return BadRequest(ApiResponse<object>.FailObject("NO_FILE", "未提供文件", GetTraceId()));
        }

        var resolvedClient = await ResolveMobileClientIdAsync(wsId, input.ClientId, ct);
        if (resolvedClient.Error != null)
        {
            return resolvedClient.Error;
        }

        using var stream = input.File.OpenReadStream();
        var item = await _importService.CreateFileAsync(
            wsId,
            fileName: input.File.FileName,
            mimeType: input.File.ContentType,
            stream: stream,
            topicId: input.TopicId?.ToString(),
            createdFrom: "mobile",
            originDeviceId: resolvedClient.ClientId,
            ct: ct);

        return Ok(ApiResponse<InboxItemDto>.Ok(item, GetTraceId()));
    }

    private async Task<(string? ClientId, IActionResult? Error)> ResolveMobileClientIdAsync(
        string workspaceId,
        string? requestedClientId,
        CancellationToken ct)
    {
        var tokenType = User.FindFirstValue("token_type");
        if (string.Equals(tokenType, "mobile_device", StringComparison.OrdinalIgnoreCase))
        {
            var tokenWorkspaceId = User.FindFirstValue("workspace_id");
            var tokenClientId = User.FindFirstValue("client_id");
            if (!string.Equals(tokenWorkspaceId, workspaceId, StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(tokenClientId))
            {
                return (null, Unauthorized(ApiResponse<object>.FailObject(
                    "INVALID_DEVICE_TOKEN",
                    "移动设备凭证与当前工作区不匹配",
                    GetTraceId())));
            }

            var device = await _repo.GetMobileDeviceAsync(workspaceId, tokenClientId, ct);
            if (device == null || !string.Equals(device.Status, "active", StringComparison.OrdinalIgnoreCase))
            {
                return (null, Unauthorized(ApiResponse<object>.FailObject(
                    "DEVICE_NOT_ACTIVE",
                    "移动设备未绑定或已停用",
                    GetTraceId())));
            }

            return (tokenClientId.Trim(), null);
        }

        if (string.IsNullOrWhiteSpace(requestedClientId))
        {
            return (null, BadRequest(ApiResponse<object>.FailObject(
                "NO_CLIENT_ID",
                "缺少采集设备 ID",
                GetTraceId())));
        }

        return (requestedClientId.Trim(), null);
    }
}

// ===== DTOs =====

public class MobileCaptureTextDto
{
    public string ContentText { get; set; } = string.Empty;
    public Guid? TopicId { get; set; }
    public string? ClientId { get; set; }
}

public class MobileCaptureUrlDto
{
    public string SourceUrl { get; set; } = string.Empty;
    public string? Title { get; set; }
    public Guid? TopicId { get; set; }
    public string? ClientId { get; set; }
}

public class MobileCaptureUploadDto
{
    public IFormFile? File { get; set; }
    public Guid? TopicId { get; set; }
    public string? ClientId { get; set; }
}
