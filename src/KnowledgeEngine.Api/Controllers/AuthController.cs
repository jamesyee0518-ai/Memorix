using KnowledgeEngine.Application.DTOs;
using KnowledgeEngine.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace KnowledgeEngine.Api.Controllers;

public class AuthController : BaseController
{
    private readonly AuthService _authService;

    public AuthController(AuthService authService)
    {
        _authService = authService;
    }

    [HttpPost("register")]
    [AllowAnonymous]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request, CancellationToken ct)
    {
        var result = await _authService.RegisterAsync(request, ct);
        var traceId = GetTraceId();
        return StatusCode(201, ApiResponse<RegisterResponse>.Ok(result.Data!, traceId));
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken ct)
    {
        var result = await _authService.LoginAsync(request, ct);
        var traceId = GetTraceId();
        return Ok(ApiResponse<LoginResponse>.Ok(result.Data!, traceId));
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<IActionResult> GetMe(CancellationToken ct)
    {
        var result = await _authService.GetCurrentUserAsync(ct);
        var traceId = GetTraceId();
        return Ok(ApiResponse<UserInfoResponse>.Ok(result.Data!, traceId));
    }

    [HttpPost("logout")]
    [Authorize]
    public IActionResult Logout()
    {
        var traceId = GetTraceId();
        return Ok(ApiResponse<object>.Ok(new { message = "Logged out successfully" }, traceId));
    }
}
