using KnowledgeEngine.Application.DTOs;
using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Application.Settings;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace KnowledgeEngine.Api.Controllers;

[ApiController]
[Route("api/mobile/devices")]
[Authorize]
public class MobileDevicesController : BaseController
{
    private const string PairingCodeHashSettingKey = "mobile_pairing_code_hash";
    private const string PairingCodeExpiresAtSettingKey = "mobile_pairing_code_expires_at";
    private static readonly TimeSpan PairingCodeTtl = TimeSpan.FromMinutes(10);

    private readonly IConfigService _configService;
    private readonly IKnowledgeRepository _repo;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly IWorkspaceAuthorizationService _workspaceAuthorization;
    private readonly JwtSettings _jwtSettings;

    public MobileDevicesController(
        IConfigService configService,
        IKnowledgeRepository repo,
        IJwtTokenService jwtTokenService,
        IWorkspaceAuthorizationService workspaceAuthorization,
        IOptions<JwtSettings> jwtSettings)
    {
        _configService = configService;
        _repo = repo;
        _jwtTokenService = jwtTokenService;
        _workspaceAuthorization = workspaceAuthorization;
        _jwtSettings = jwtSettings.Value;
    }

    [HttpPost("bind")]
    public async Task<IActionResult> Bind([FromBody] MobileDeviceBindDto input, CancellationToken ct = default)
    {
        var wsId = await _configService.GetCurrentWorkspaceIdAsync(ct);
        if (wsId == null)
        {
            return BadRequest(ApiResponse<object>.FailObject("NO_WORKSPACE", "未找到活动工作区", GetTraceId()));
        }
        var accessError = await AuthorizeWorkspaceAsync(wsId, ct);
        if (accessError != null) return accessError;

        if (string.IsNullOrWhiteSpace(input.ClientId))
        {
            return BadRequest(ApiResponse<object>.FailObject("NO_CLIENT_ID", "缺少设备 ID", GetTraceId()));
        }

        var device = await _repo.UpsertMobileDeviceAsync(new UpsertMobileDeviceInput
        {
            WorkspaceId = wsId,
            ClientId = input.ClientId,
            DeviceName = input.DeviceName,
            Platform = input.Platform,
            PushToken = input.PushToken
        }, ct);

        var tokens = await IssueDeviceTokensAsync(device, ct);

        return Ok(ApiResponse<MobileDeviceBindResponse>.Ok(new MobileDeviceBindResponse
        {
            Device = tokens.Device,
            DeviceAccessToken = tokens.AccessToken,
            RefreshToken = tokens.RefreshToken,
            ExpiresAt = tokens.ExpiresAt,
            RefreshTokenExpiresAt = tokens.RefreshTokenExpiresAt
        }, GetTraceId()));
    }

    [HttpPost("pairing-code")]
    public async Task<IActionResult> CreatePairingCode(CancellationToken ct = default)
    {
        var wsId = await _configService.GetCurrentWorkspaceIdAsync(ct);
        if (wsId == null)
        {
            return BadRequest(ApiResponse<object>.FailObject("NO_WORKSPACE", "未找到活动工作区", GetTraceId()));
        }
        var accessError = await AuthorizeWorkspaceAsync(wsId, ct);
        if (accessError != null) return accessError;

        var code = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
        var expiresAt = DateTime.UtcNow.Add(PairingCodeTtl);
        await _repo.SetSettingAsync(wsId, PairingCodeHashSettingKey, HashPairingCode(wsId, code), ct);
        await _repo.SetSettingAsync(wsId, PairingCodeExpiresAtSettingKey, expiresAt.ToString("o"), ct);

        return Ok(ApiResponse<MobilePairingCodeResponse>.Ok(new MobilePairingCodeResponse
        {
            Code = code,
            ExpiresAt = expiresAt
        }, GetTraceId()));
    }

    [HttpPost("pair")]
    [AllowAnonymous]
    public async Task<IActionResult> Pair([FromBody] MobileDevicePairDto input, CancellationToken ct = default)
    {
        var wsId = await _configService.GetCurrentWorkspaceIdAsync(ct);
        if (wsId == null)
        {
            return BadRequest(ApiResponse<object>.FailObject("NO_WORKSPACE", "未找到活动工作区", GetTraceId()));
        }

        if (string.IsNullOrWhiteSpace(input.Code) || string.IsNullOrWhiteSpace(input.ClientId))
        {
            return BadRequest(ApiResponse<object>.FailObject("INVALID_PAIRING_INPUT", "配对码和设备 ID 不能为空", GetTraceId()));
        }

        var expiresRaw = await _repo.GetSettingAsync(wsId, PairingCodeExpiresAtSettingKey, ct);
        var storedHash = await _repo.GetSettingAsync(wsId, PairingCodeHashSettingKey, ct);
        if (!DateTime.TryParse(expiresRaw, out var expiresAt) ||
            expiresAt <= DateTime.UtcNow ||
            string.IsNullOrWhiteSpace(storedHash) ||
            !FixedTimeEquals(storedHash, HashPairingCode(wsId, input.Code.Trim())))
        {
            return Unauthorized(ApiResponse<object>.FailObject("INVALID_PAIRING_CODE", "配对码无效或已过期", GetTraceId()));
        }

        var device = await _repo.UpsertMobileDeviceAsync(new UpsertMobileDeviceInput
        {
            WorkspaceId = wsId,
            ClientId = input.ClientId,
            DeviceName = input.DeviceName,
            Platform = input.Platform,
            PushToken = input.PushToken
        }, ct);
        var tokens = await IssueDeviceTokensAsync(device, ct);

        await _repo.SetSettingAsync(wsId, PairingCodeHashSettingKey, "", ct);
        await _repo.SetSettingAsync(wsId, PairingCodeExpiresAtSettingKey, DateTime.UtcNow.ToString("o"), ct);

        return Ok(ApiResponse<MobileDeviceBindResponse>.Ok(new MobileDeviceBindResponse
        {
            Device = tokens.Device,
            DeviceAccessToken = tokens.AccessToken,
            RefreshToken = tokens.RefreshToken,
            ExpiresAt = tokens.ExpiresAt,
            RefreshTokenExpiresAt = tokens.RefreshTokenExpiresAt
        }, GetTraceId()));
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<IActionResult> Refresh([FromBody] MobileDeviceRefreshDto input, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(input.RefreshToken))
        {
            return BadRequest(ApiResponse<object>.FailObject("NO_REFRESH_TOKEN", "缺少刷新凭证", GetTraceId()));
        }

        var refreshHash = HashRefreshToken(input.RefreshToken);
        var device = await _repo.GetMobileDeviceByRefreshTokenHashAsync(refreshHash, ct);
        if (device == null ||
            !string.Equals(device.Status, "active", StringComparison.OrdinalIgnoreCase) ||
            device.RefreshTokenExpiresAt == null ||
            device.RefreshTokenExpiresAt.Value <= DateTime.UtcNow)
        {
            return Unauthorized(ApiResponse<object>.FailObject("INVALID_REFRESH_TOKEN", "刷新凭证无效或已过期", GetTraceId()));
        }

        var tokens = await IssueDeviceTokensAsync(device, ct);
        return Ok(ApiResponse<MobileDeviceBindResponse>.Ok(new MobileDeviceBindResponse
        {
            Device = tokens.Device,
            DeviceAccessToken = tokens.AccessToken,
            RefreshToken = tokens.RefreshToken,
            ExpiresAt = tokens.ExpiresAt,
            RefreshTokenExpiresAt = tokens.RefreshTokenExpiresAt
        }, GetTraceId()));
    }

    [HttpPost("deactivate")]
    public async Task<IActionResult> Deactivate([FromBody] MobileDeviceDeactivateDto input, CancellationToken ct = default)
    {
        var wsId = await _configService.GetCurrentWorkspaceIdAsync(ct);
        if (wsId == null)
        {
            return BadRequest(ApiResponse<object>.FailObject("NO_WORKSPACE", "未找到活动工作区", GetTraceId()));
        }
        var accessError = await AuthorizeWorkspaceAsync(wsId, ct);
        if (accessError != null) return accessError;

        var tokenType = User.FindFirstValue("token_type");
        var clientId = string.Equals(tokenType, "mobile_device", StringComparison.OrdinalIgnoreCase)
            ? User.FindFirstValue("client_id")
            : input.ClientId;

        if (string.IsNullOrWhiteSpace(clientId))
        {
            return BadRequest(ApiResponse<object>.FailObject("NO_CLIENT_ID", "缺少设备 ID", GetTraceId()));
        }

        await _repo.DeactivateMobileDeviceAsync(wsId, clientId, ct);
        return Ok(ApiResponse<object>.Ok(new { deactivated = true, clientId }, GetTraceId()));
    }

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct = default)
    {
        var wsId = await _configService.GetCurrentWorkspaceIdAsync(ct);
        if (wsId == null)
        {
            return BadRequest(ApiResponse<object>.FailObject("NO_WORKSPACE", "未找到活动工作区", GetTraceId()));
        }
        var accessError = await AuthorizeWorkspaceAsync(wsId, ct);
        if (accessError != null) return accessError;

        var devices = await _repo.ListMobileDevicesAsync(wsId, ct);
        return Ok(ApiResponse<List<MobileDeviceDto>>.Ok(devices, GetTraceId()));
    }

    private async Task<(MobileDeviceDto Device, string AccessToken, string RefreshToken, DateTime ExpiresAt, DateTime RefreshTokenExpiresAt)> IssueDeviceTokensAsync(
        MobileDeviceDto device,
        CancellationToken ct)
    {
        var expiresAt = DateTime.UtcNow.AddMinutes(_jwtSettings.MobileDeviceExpiresMinutes);
        var refreshTokenExpiresAt = DateTime.UtcNow.AddDays(_jwtSettings.MobileDeviceRefreshExpiresDays);
        var refreshToken = GenerateRefreshToken();
        var updatedDevice = await _repo.UpdateMobileDeviceRefreshTokenAsync(
            device.WorkspaceId,
            device.ClientId,
            HashRefreshToken(refreshToken),
            refreshTokenExpiresAt,
            ct);

        var accessToken = _jwtTokenService.GenerateMobileDeviceToken(
            Guid.Parse(updatedDevice.WorkspaceId),
            Guid.Parse(updatedDevice.Id),
            updatedDevice.ClientId,
            expiresAt);

        return (updatedDevice, accessToken, refreshToken, expiresAt, refreshTokenExpiresAt);
    }

    private async Task<IActionResult?> AuthorizeWorkspaceAsync(
        string workspaceId,
        CancellationToken ct)
    {
        if (!Guid.TryParse(workspaceId, out var id))
        {
            return BadRequest(ApiResponse<object>.FailObject(
                "INVALID_WORKSPACE", "活动工作区 ID 无效", GetTraceId()));
        }
        var access = await _workspaceAuthorization.AuthorizeAsync(id, ct);
        return access switch
        {
            WorkspaceAccessResult.Allowed => null,
            WorkspaceAccessResult.NotFound => NotFound(
                ApiResponse<object>.FailObject(
                    "WORKSPACE_NOT_FOUND", "活动工作区不存在", GetTraceId())),
            _ => StatusCode(StatusCodes.Status403Forbidden,
                ApiResponse<object>.FailObject(
                    "WORKSPACE_FORBIDDEN", "无权管理该工作区的移动设备", GetTraceId()))
        };
    }

    private static string GenerateRefreshToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static string HashRefreshToken(string refreshToken)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string HashPairingCode(string workspaceId, string code)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{workspaceId}:{code}"));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static bool FixedTimeEquals(string left, string right)
    {
        var leftBytes = Encoding.UTF8.GetBytes(left);
        var rightBytes = Encoding.UTF8.GetBytes(right);
        return leftBytes.Length == rightBytes.Length &&
               CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }
}

public class MobileDeviceBindDto
{
    public string ClientId { get; set; } = string.Empty;
    public string? DeviceName { get; set; }
    public string? Platform { get; set; }
    public string? PushToken { get; set; }
}

public class MobileDevicePairDto : MobileDeviceBindDto
{
    public string Code { get; set; } = string.Empty;
}

public class MobilePairingCodeResponse
{
    public string Code { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
}

public class MobileDeviceBindResponse
{
    public MobileDeviceDto Device { get; set; } = new();
    public string DeviceAccessToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public DateTime RefreshTokenExpiresAt { get; set; }
}

public class MobileDeviceRefreshDto
{
    public string RefreshToken { get; set; } = string.Empty;
}

public class MobileDeviceDeactivateDto
{
    public string? ClientId { get; set; }
}
