using KnowledgeEngine.Application.DTOs;
using KnowledgeEngine.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace KnowledgeEngine.Api.Controllers;

[ApiController]
[Route("api/bindings")]
[Authorize]
public class BindingsController : BaseController
{
    private readonly IBindingService _bindingService;

    public BindingsController(IBindingService bindingService)
    {
        _bindingService = bindingService;
    }

    [HttpGet("cloud-accounts")]
    public async Task<IActionResult> ListCloudAccounts(CancellationToken ct) =>
        Ok(ApiResponse<List<CloudAccountBindingDto>>.Ok(
            await _bindingService.ListCloudAccountsAsync(ct), GetTraceId()));

    [HttpPost("cloud-accounts")]
    public async Task<IActionResult> BindCloudAccount(
        [FromBody] CreateCloudAccountBindingDto input,
        CancellationToken ct)
    {
        var result = await _bindingService.BindCloudAccountAsync(input, ct);
        return StatusCode(201, ApiResponse<CloudAccountBindingDto>.Ok(result, GetTraceId()));
    }

    [HttpDelete("cloud-accounts/{id:guid}")]
    public async Task<IActionResult> UnbindCloudAccount(Guid id, CancellationToken ct)
    {
        await _bindingService.UnbindCloudAccountAsync(id, ct);
        return NoContent();
    }

    [HttpGet("workspaces")]
    public async Task<IActionResult> ListWorkspaceBindings(
        [FromQuery] Guid? workspaceId,
        CancellationToken ct) =>
        Ok(ApiResponse<List<WorkspaceBindingDto>>.Ok(
            await _bindingService.ListWorkspaceBindingsAsync(workspaceId, ct), GetTraceId()));

    [HttpPost("workspaces")]
    public async Task<IActionResult> CreateWorkspaceBinding(
        [FromBody] CreateWorkspaceBindingDto input,
        CancellationToken ct)
    {
        var result = await _bindingService.CreateWorkspaceBindingAsync(input, ct);
        return StatusCode(201, ApiResponse<WorkspaceBindingDto>.Ok(result, GetTraceId()));
    }

    [HttpPut("workspaces/{id:guid}")]
    public async Task<IActionResult> UpdateWorkspaceBinding(
        Guid id,
        [FromBody] UpdateWorkspaceBindingDto input,
        CancellationToken ct) =>
        Ok(ApiResponse<WorkspaceBindingDto>.Ok(
            await _bindingService.UpdateWorkspaceBindingAsync(id, input, ct), GetTraceId()));

    [HttpDelete("workspaces/{id:guid}")]
    public async Task<IActionResult> UnbindWorkspace(Guid id, CancellationToken ct)
    {
        await _bindingService.UnbindWorkspaceAsync(id, ct);
        return NoContent();
    }
}
