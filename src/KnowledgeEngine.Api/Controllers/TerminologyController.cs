using KnowledgeEngine.Application.DTOs;
using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace KnowledgeEngine.Api.Controllers;

[Authorize]
public sealed class TerminologyController : BaseController
{
    private readonly ITerminologyService _service;
    private readonly ICurrentUserContext _currentUser;

    public TerminologyController(ITerminologyService service, ICurrentUserContext currentUser)
    { _service = service; _currentUser = currentUser; }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? query, CancellationToken ct)
        => Ok(ApiResponse<IReadOnlyList<Terminology>>.Ok(await _service.ListAsync(RequireUser(), query, ct), GetTraceId()));

    [HttpPost]
    public async Task<IActionResult> Upsert([FromBody] Terminology term, CancellationToken ct)
        => Ok(ApiResponse<Terminology>.Ok(await _service.UpsertAsync(RequireUser(), term, ct), GetTraceId()));

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update([FromRoute] Guid id, [FromBody] Terminology term, CancellationToken ct)
    {
        term.Id = id;
        return Ok(ApiResponse<Terminology>.Ok(await _service.UpsertAsync(RequireUser(), term, ct), GetTraceId()));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete([FromRoute] Guid id, CancellationToken ct)
        => Ok(ApiResponse<bool>.Ok(await _service.DeleteAsync(RequireUser(), id, ct), GetTraceId()));

    private Guid RequireUser() => _currentUser.UserId ?? throw new UnauthorizedAccessException("User context is required");
}
