using KnowledgeEngine.Api.Services;
using KnowledgeEngine.Application.DTOs;
using KnowledgeEngine.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeEngine.Api.Controllers;

/// <summary>
/// Desktop-only capability discovery. The response contains no credentials or
/// infrastructure details and is safe to use before the setup flow is complete.
/// </summary>
[ApiController]
[Route("api/desktop")]
public sealed class DesktopRuntimeController : BaseController
{
    private readonly DesktopCapabilityService _capabilities;
    private readonly IBindingService _bindingService;
    private readonly IConfigService _configService;
    private readonly IAppDbContext _db;
    private readonly ICurrentUserContext _currentUser;
    private readonly DesktopRuntimeCoordinator _coordinator;

    public DesktopRuntimeController(
        DesktopCapabilityService capabilities,
        IBindingService bindingService,
        IConfigService configService,
        IAppDbContext db,
        ICurrentUserContext currentUser,
        DesktopRuntimeCoordinator coordinator)
    {
        _capabilities = capabilities;
        _bindingService = bindingService;
        _configService = configService;
        _db = db;
        _currentUser = currentUser;
        _coordinator = coordinator;
    }

    [HttpGet("capabilities")]
    [AllowAnonymous]
    public IActionResult GetCapabilities()
    {
        return Ok(ApiResponse<DesktopCapabilitiesDto>.Ok(
            _capabilities.GetCapabilities(),
            GetTraceId()));
    }

    [HttpGet("cloud-api-capabilities")]
    [AllowAnonymous]
    public IActionResult GetCloudApiCapabilities()
    {
        return Ok(ApiResponse<CloudApiCapabilitiesDto>.Ok(new CloudApiCapabilitiesDto
        {
            ApiVersion = "1.0",
            Features = ["oauth_pkce", "workspace_discovery", "cloud_inbox"]
        }, GetTraceId()));
    }

    [HttpGet("cloud-connection")]
    [Authorize]
    public async Task<IActionResult> GetCloudConnection(CancellationToken ct)
    {
        var accounts = await _bindingService.ListCloudAccountsAsync(ct);
        var currentWorkspace = await _configService.GetCurrentWorkspaceIdAsync(ct);
        var bindings = Guid.TryParse(currentWorkspace, out var workspaceId)
            ? await _bindingService.ListWorkspaceBindingsAsync(workspaceId, ct)
            : [];
        var binding = bindings.FirstOrDefault(x => x.BindingStatus == "active");
        var account = binding == null
            ? accounts.FirstOrDefault(x => x.BindingStatus == "active")
            : accounts.FirstOrDefault(x => x.Id == binding.CloudAccountBindingId);
        var host = Uri.TryCreate(account?.CloudApiBaseUrl, UriKind.Absolute, out var uri)
            ? uri.Host
            : null;
        var requiresReauthentication = account?.BindingStatus == "reauth_required";
        var connected = account?.BindingStatus == "active" && binding != null;
        return Ok(ApiResponse<DesktopCloudConnectionStatusDto>.Ok(new DesktopCloudConnectionStatusDto
        {
            Status = requiresReauthentication
                ? "reauth_required"
                : connected ? "connected" : account != null ? "account_connected" : "not_connected",
            CloudAccountBindingId = account?.Id,
            AccountDisplayName = account?.AccountDisplayName,
            AccountEmailMasked = account?.AccountEmailMasked,
            CloudApiHost = host,
            CloudWorkspaceId = binding?.CloudWorkspaceId,
            LastAuthenticatedAt = account?.LastAuthenticatedAt,
            RequiresReauthentication = requiresReauthentication
        }, GetTraceId()));
    }

    [HttpGet("state")]
    [Authorize]
    public async Task<IActionResult> GetState(CancellationToken ct)
    {
        var configured = await _configService.GetCurrentWorkspaceIdAsync(ct);
        var userId = _currentUser.UserId;
        var workspace = Guid.TryParse(configured, out var workspaceId) && userId.HasValue
            ? await _db.Workspaces.AsNoTracking().FirstOrDefaultAsync(
                x => x.Id == workspaceId && x.UserId == userId.Value, ct)
            : null;
        var binding = workspace == null
            ? null
            : (await _bindingService.ListWorkspaceBindingsAsync(workspace.Id, ct))
                .FirstOrDefault(x => x.BindingStatus == "active");
        var mode = workspace?.Mode ?? "unconfigured";
        return Ok(ApiResponse<DesktopRuntimeStateDto>.Ok(new DesktopRuntimeStateDto
        {
            LocalWorkspaceId = workspace?.Id,
            WorkspaceName = workspace?.Name,
            Mode = mode,
            RouteTarget = mode == "cloud" ? "cloud_gateway" : mode == "unconfigured" ? "none" : "local",
            ConnectionStatus = mode == "cloud"
                ? binding == null ? "not_connected" : "connected"
                : "not_required",
            CloudWorkspaceId = binding?.CloudWorkspaceId,
            Generation = _coordinator.Generation,
            LocalFallbackAllowed = mode != "cloud"
        }, GetTraceId()));
    }
}
