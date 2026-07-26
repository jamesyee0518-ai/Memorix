using System.Text;
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
    private readonly IWorkspaceService _workspaces;

    public TerminologyController(
        ITerminologyService service, ICurrentUserContext currentUser, IWorkspaceService workspaces)
    { _service = service; _currentUser = currentUser; _workspaces = workspaces; }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] TerminologyQuery query, CancellationToken ct)
    {
        var (userId, workspaceId) = await RequireContext(ct);
        return Ok(ApiResponse<PagedResult<Terminology>>.Ok(
            await _service.ListAsync(userId, workspaceId, query, ct), GetTraceId()));
    }

    [HttpPost]
    public async Task<IActionResult> Upsert([FromBody] Terminology term, CancellationToken ct)
    {
        var (userId, workspaceId) = await RequireContext(ct);
        return Ok(ApiResponse<Terminology>.Ok(
            await _service.UpsertAsync(userId, workspaceId, term, true, ct), GetTraceId()));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update([FromRoute] Guid id, [FromBody] Terminology term, CancellationToken ct)
    {
        var (userId, workspaceId) = await RequireContext(ct);
        term.Id = id;
        return Ok(ApiResponse<Terminology>.Ok(
            await _service.UpsertAsync(userId, workspaceId, term, true, ct), GetTraceId()));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete([FromRoute] Guid id, CancellationToken ct)
    {
        var (userId, workspaceId) = await RequireContext(ct);
        return Ok(ApiResponse<bool>.Ok(
            await _service.DeleteAsync(userId, workspaceId, id, ct), GetTraceId()));
    }

    [HttpPost("bulk")]
    public async Task<IActionResult> Bulk([FromBody] TerminologyBulkRequest request, CancellationToken ct)
    {
        var (userId, workspaceId) = await RequireContext(ct);
        return Ok(ApiResponse<TerminologyBulkResult>.Ok(
            await _service.BulkUpsertAsync(userId, workspaceId, request, ct), GetTraceId()));
    }

    [HttpGet("export")]
    public async Task<IActionResult> Export(CancellationToken ct)
    {
        var (userId, workspaceId) = await RequireContext(ct);
        var result = await _service.ListAsync(userId, workspaceId,
            new TerminologyQuery { Page = 1, PageSize = 200 }, ct);
        var all = new List<Terminology>(result.Items);
        for (var page = 2; page <= result.TotalPages; page++)
        {
            var next = await _service.ListAsync(userId, workspaceId,
                new TerminologyQuery { Page = page, PageSize = 200 }, ct);
            all.AddRange(next.Items);
        }
        var csv = new StringBuilder("\uFEFFsource_language,source_term,target_language,target_term,aliases,domain,priority,review_status,version\n");
        foreach (var term in all)
            csv.AppendLine(string.Join(',', new[]
            {
                Csv(term.SourceLanguage), Csv(term.SourceTerm), Csv(term.TargetLanguage), Csv(term.TargetTerm),
                Csv(term.Aliases), Csv(term.Domain), term.Priority.ToString(), Csv(term.ReviewStatus), Csv(term.Version)
            }));
        return File(Encoding.UTF8.GetBytes(csv.ToString()), "text/csv; charset=utf-8",
            $"terminology-{DateTime.UtcNow:yyyyMMddHHmmss}.csv");
    }

    [HttpPost("{id:guid}/review")]
    public async Task<IActionResult> Review(
        [FromRoute] Guid id, [FromBody] TerminologyReviewRequest request, CancellationToken ct)
    {
        var (userId, workspaceId) = await RequireContext(ct);
        return Ok(ApiResponse<Terminology>.Ok(
            await _service.ReviewAsync(userId, workspaceId, id, request.Status, ct), GetTraceId()));
    }

    [HttpGet("conflicts")]
    public async Task<IActionResult> Conflicts(CancellationToken ct)
    {
        var (userId, workspaceId) = await RequireContext(ct);
        return Ok(ApiResponse<IReadOnlyList<TerminologyConflict>>.Ok(
            await _service.ListConflictsAsync(userId, workspaceId, ct), GetTraceId()));
    }

    [HttpGet("stats")]
    public async Task<IActionResult> Stats(CancellationToken ct)
    {
        var (userId, workspaceId) = await RequireContext(ct);
        return Ok(ApiResponse<TerminologyStats>.Ok(
            await _service.GetStatsAsync(userId, workspaceId, ct), GetTraceId()));
    }

    [HttpPost("usage")]
    public async Task<IActionResult> Usage([FromBody] TerminologyUsageRequest request, CancellationToken ct)
    {
        var (userId, workspaceId) = await RequireContext(ct);
        return Ok(ApiResponse<IReadOnlyList<TerminologyUsage>>.Ok(
            await _service.GetUsageAsync(userId, workspaceId, request.TerminologyIds, ct), GetTraceId()));
    }

    [HttpPost("extract")]
    public async Task<IActionResult> Extract(
        [FromBody] TerminologyExtractionRequest request, CancellationToken ct)
    {
        var (userId, workspaceId) = await RequireContext(ct);
        return Ok(ApiResponse<IReadOnlyList<TerminologyCandidate>>.Ok(
            await _service.ExtractCandidatesAsync(userId, workspaceId, request, ct), GetTraceId()));
    }

    private async Task<(Guid UserId, Guid WorkspaceId)> RequireContext(CancellationToken ct)
    {
        var userId = _currentUser.UserId ?? throw new UnauthorizedAccessException("User context is required");
        var workspace = await _workspaces.GetCurrentWorkspaceAsync(userId, ct)
            ?? throw new InvalidOperationException("Current workspace is required");
        return (userId, workspace.Id);
    }

    private static string Csv(string? value)
    {
        value ??= string.Empty;
        return $"\"{value.Replace("\"", "\"\"")}\"";
    }
}
