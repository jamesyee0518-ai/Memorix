using System.Text.Encodings.Web;
using KnowledgeEngine.Application.DTOs;
using KnowledgeEngine.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace KnowledgeEngine.Api.Controllers;

[ApiController]
[Route("api/oauth")]
public class OAuthController : BaseController
{
    private readonly IOAuthBindingService _oauthService;

    public OAuthController(IOAuthBindingService oauthService)
    {
        _oauthService = oauthService;
    }

    [HttpPost("start")]
    [Authorize]
    public async Task<IActionResult> Start([FromBody] StartOAuthDto input, CancellationToken ct) =>
        Ok(ApiResponse<OAuthStartResultDto>.Ok(
            await _oauthService.StartAsync(input, ct), GetTraceId()));

    [HttpGet("status/{sessionId}")]
    [Authorize]
    public async Task<IActionResult> Status(string sessionId, CancellationToken ct) =>
        Ok(ApiResponse<OAuthStatusDto>.Ok(
            await _oauthService.GetStatusAsync(sessionId, ct), GetTraceId()));

    [HttpGet("callback")]
    [AllowAnonymous]
    public async Task<ContentResult> Callback(
        [FromQuery] string? code,
        [FromQuery] string? state,
        [FromQuery] string? error,
        CancellationToken ct)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(error))
            {
                throw new InvalidOperationException($"Authorization failed: {error}");
            }
            if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(state))
            {
                throw new InvalidOperationException("Authorization callback is missing code or state.");
            }
            await _oauthService.CompleteAsync(code, state, ct);
            return Html("Memorix 登录成功", "账号已安全连接，可以关闭此窗口并返回 Memorix。");
        }
        catch (Exception ex)
        {
            return Html("Memorix 登录失败", HtmlEncoder.Default.Encode(ex.Message));
        }
    }

    private static ContentResult Html(string title, string message) => new()
    {
        ContentType = "text/html; charset=utf-8",
        Content = $"""
            <!doctype html><html lang="zh-CN"><head><meta charset="utf-8">
            <meta name="viewport" content="width=device-width,initial-scale=1">
            <title>{title}</title></head>
            <body style="font-family:system-ui;padding:48px;background:#f5f5f4;color:#1c1917">
            <main style="max-width:560px;margin:auto;background:white;padding:32px;border-radius:16px">
            <h1>{title}</h1><p>{message}</p></main></body></html>
            """
    };
}
