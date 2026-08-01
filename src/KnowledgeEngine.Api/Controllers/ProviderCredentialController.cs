using KnowledgeEngine.Application.DTOs;
using KnowledgeEngine.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace KnowledgeEngine.Api.Controllers;

/// <summary>
/// BYOK (Bring Your Own Key) credential management API.
/// Users and tenants can store, verify, disable, and rotate
/// API credentials for third-party audio providers.
/// </summary>
[ApiController]
[Route("api/provider-credentials")]
[Authorize]
public class ProviderCredentialController : BaseController
{
    private readonly ICredentialManager _credentialManager;
    private readonly ICurrentUserContext _currentUser;
    private readonly ILogger<ProviderCredentialController> _logger;

    public ProviderCredentialController(
        ICredentialManager credentialManager,
        ICurrentUserContext currentUser,
        ILogger<ProviderCredentialController> logger)
    {
        _credentialManager = credentialManager;
        _currentUser = currentUser;
        _logger = logger;
    }

    /// <summary>
    /// Lists all credentials for the current user.
    /// Never returns the encrypted secret.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> ListCredentials(CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated || _currentUser.UserId == null)
        {
            return Unauthorized(ApiResponse<object>.FailObject("UNAUTHORIZED", "User not authenticated", GetTraceId()));
        }

        var credentials = await _credentialManager.ListByOwnerAsync(
            "user", _currentUser.UserId.Value, ct);

        return Ok(ApiResponse<List<CredentialDto>>.Ok(credentials, GetTraceId()));
    }

    /// <summary>
    /// Stores a new provider credential with AES-GCM encryption.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> StoreCredential([FromBody] StoreCredentialRequest request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated || _currentUser.UserId == null)
        {
            return Unauthorized(ApiResponse<object>.FailObject("UNAUTHORIZED", "User not authenticated", GetTraceId()));
        }

        if (string.IsNullOrWhiteSpace(request.ProviderId))
        {
            return BadRequest(ApiResponse<object>.FailObject("MISSING_PROVIDER", "ProviderId is required", GetTraceId()));
        }

        if (string.IsNullOrWhiteSpace(request.Secret))
        {
            return BadRequest(ApiResponse<object>.FailObject("MISSING_SECRET", "Secret is required", GetTraceId()));
        }

        request.OwnerType = "user";
        request.OwnerId = _currentUser.UserId.Value;

        var credential = await _credentialManager.StoreAsync(request, ct);

        return Ok(ApiResponse<CredentialDto>.Ok(new CredentialDto
        {
            Id = credential.Id,
            ProviderId = credential.ProviderId,
            CredentialType = credential.CredentialType,
            OwnerType = credential.OwnerType,
            OwnerId = credential.OwnerId,
            Label = credential.Label,
            Status = credential.Status,
            LastVerifiedAt = credential.LastVerifiedAt,
            ExpiresAt = credential.ExpiresAt,
            CreatedAt = credential.CreatedAt
        }, GetTraceId()));
    }

    /// <summary>
    /// Verifies a credential by making a lightweight test call.
    /// </summary>
    [HttpPost("{credentialId}/verify")]
    public async Task<IActionResult> VerifyCredential(Guid credentialId, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
        {
            return Unauthorized(ApiResponse<object>.FailObject("UNAUTHORIZED", "User not authenticated", GetTraceId()));
        }

        var isValid = await _credentialManager.VerifyAsync(credentialId, ct);
        return Ok(ApiResponse<object>.Ok(new { credentialId, valid = isValid }, GetTraceId()));
    }

    /// <summary>
    /// Disables a credential without deleting it.
    /// </summary>
    [HttpPost("{credentialId}/disable")]
    public async Task<IActionResult> DisableCredential(Guid credentialId, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
        {
            return Unauthorized(ApiResponse<object>.FailObject("UNAUTHORIZED", "User not authenticated", GetTraceId()));
        }

        await _credentialManager.DisableAsync(credentialId, ct);
        return Ok(ApiResponse<object>.Ok(new { credentialId, status = "disabled" }, GetTraceId()));
    }

    /// <summary>
    /// Rotates the encryption key for a credential.
    /// </summary>
    [HttpPost("{credentialId}/rotate")]
    public async Task<IActionResult> RotateCredential(Guid credentialId, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
        {
            return Unauthorized(ApiResponse<object>.FailObject("UNAUTHORIZED", "User not authenticated", GetTraceId()));
        }

        await _credentialManager.RotateAsync(credentialId, ct);
        return Ok(ApiResponse<object>.Ok(new { credentialId, rotated = true }, GetTraceId()));
    }
}
