using KnowledgeEngine.Application.DTOs;
using KnowledgeEngine.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace KnowledgeEngine.Api.Controllers;

[Authorize]
[Route("api/api-keys")]
public class ApiKeysController : BaseController
{
    private readonly IApiKeyService _apiKeyService;
    private readonly ICurrentUserContext _currentUser;

    public ApiKeysController(IApiKeyService apiKeyService, ICurrentUserContext currentUser)
    {
        _apiKeyService = apiKeyService;
        _currentUser = currentUser;
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateApiKeyRequest request, CancellationToken ct)
    {
        var userId = _currentUser.UserId;
        if (userId == null)
        {
            return Unauthorized();
        }

        var result = await _apiKeyService.CreateAsync(userId.Value, request, ct);

        if (!result.Success)
        {
            return Ok(ApiResponse<CreateApiKeyResponse>.Fail(result.Error!.Code, result.Error!.Message, GetTraceId()));
        }

        return StatusCode(201, ApiResponse<CreateApiKeyResponse>.Ok(result.Data!, GetTraceId()));
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var userId = _currentUser.UserId;
        if (userId == null)
        {
            return Unauthorized();
        }

        var result = await _apiKeyService.GetAllAsync(userId.Value, ct);

        if (!result.Success)
        {
            return Ok(ApiResponse<List<ApiKeyListItem>>.Fail(result.Error!.Code, result.Error!.Message, GetTraceId()));
        }

        return Ok(ApiResponse<List<ApiKeyListItem>>.Ok(result.Data!, GetTraceId()));
    }

    [HttpPost("{id:guid}/disable")]
    public async Task<IActionResult> Disable([FromRoute] Guid id, CancellationToken ct)
    {
        var userId = _currentUser.UserId;
        if (userId == null)
        {
            return Unauthorized();
        }

        var result = await _apiKeyService.DisableAsync(userId.Value, id, ct);

        if (!result.Success)
        {
            return Ok(ApiResponse<object>.Fail(result.Error!.Code, result.Error!.Message, GetTraceId()));
        }

        return Ok(ApiResponse<object>.Ok(result.Data!, GetTraceId()));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete([FromRoute] Guid id, CancellationToken ct)
    {
        var userId = _currentUser.UserId;
        if (userId == null)
        {
            return Unauthorized();
        }

        var result = await _apiKeyService.DeleteAsync(userId.Value, id, ct);

        if (!result.Success)
        {
            return Ok(ApiResponse<object>.Fail(result.Error!.Code, result.Error!.Message, GetTraceId()));
        }

        return Ok(ApiResponse<object>.Ok(result.Data!, GetTraceId()));
    }
}
