using KnowledgeEngine.Application.DTOs;
using KnowledgeEngine.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace KnowledgeEngine.Api.Controllers;

/// <summary>
/// Device capability detection API.
/// Allows clients to query server-side inferred capabilities or report
/// client-side hardware specs and receive a recommended audio processing mode.
/// </summary>
[ApiController]
[Route("api/device")]
[Authorize]
public class DeviceCapabilityController : BaseController
{
    private readonly IDeviceCapabilityDetector _detector;
    private readonly ICurrentUserContext _currentUser;
    private readonly ILogger<DeviceCapabilityController> _logger;

    public DeviceCapabilityController(
        IDeviceCapabilityDetector detector,
        ICurrentUserContext currentUser,
        ILogger<DeviceCapabilityController> logger)
    {
        _detector = detector;
        _currentUser = currentUser;
        _logger = logger;
    }

    /// <summary>
    /// Detects server-side device capabilities by inferring from the host environment.
    /// Returns CPU cores, memory, GPU availability, local toolchain support, and a
    /// recommended processing mode (batch, realtime, or offline).
    /// </summary>
    [HttpGet("capability")]
    public async Task<IActionResult> DetectCapability(CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
        {
            return Unauthorized(ApiResponse<object>.FailObject(
                "UNAUTHORIZED", "User not authenticated", GetTraceId()));
        }

        try
        {
            var result = await _detector.DetectAsync(ct);
            return Ok(ApiResponse<DeviceCapabilityResult>.Ok(result, GetTraceId()));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to detect server-side device capabilities");
            return StatusCode(500, ApiResponse<object>.FailObject(
                "DETECTION_FAILED", ex.Message, GetTraceId()));
        }
    }

    /// <summary>
    /// Reports client-side device capabilities and receives a server-determined
    /// recommendation for audio processing mode.
    /// </summary>
    [HttpPost("capability")]
    public async Task<IActionResult> ReportCapability(
        [FromBody] DeviceCapabilityReport report, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
        {
            return Unauthorized(ApiResponse<object>.FailObject(
                "UNAUTHORIZED", "User not authenticated", GetTraceId()));
        }

        if (report == null)
        {
            return BadRequest(ApiResponse<object>.FailObject(
                "INVALID_REPORT", "Device capability report is required", GetTraceId()));
        }

        try
        {
            var result = await _detector.ReportAsync(report, ct);
            return Ok(ApiResponse<DeviceCapabilityResult>.Ok(result, GetTraceId()));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process client device capability report");
            return StatusCode(500, ApiResponse<object>.FailObject(
                "REPORT_FAILED", ex.Message, GetTraceId()));
        }
    }
}
