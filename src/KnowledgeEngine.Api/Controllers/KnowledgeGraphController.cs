using KnowledgeEngine.Application.DTOs;
using KnowledgeEngine.Application.Exceptions;
using KnowledgeEngine.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace KnowledgeEngine.Api.Controllers;

[Authorize]
public sealed class KnowledgeGraphController(
    IKnowledgeGraphService graph,
    ICurrentUserContext currentUser) : BaseController
{
    [HttpGet("entities")]
    public async Task<IActionResult> GetEntities(
        [FromQuery] Guid? workspaceId,
        [FromQuery] string? entityType,
        [FromQuery] string? language,
        [FromQuery] int limit = 300,
        CancellationToken ct = default)
    {
        var result = await graph.GetGraphAsync(
            RequireUserId(), workspaceId, entityType, language, limit, ct);
        return Ok(ApiResponse<EntityGraphDto>.Ok(result, GetTraceId()));
    }

    [HttpGet("entities/{id:guid}/neighbors")]
    public async Task<IActionResult> GetNeighbors(
        Guid id,
        [FromQuery] string? language,
        [FromQuery] int limit = 100,
        CancellationToken ct = default)
    {
        var result = await graph.GetNeighborsAsync(
            RequireUserId(), id, language, limit, ct);
        return Ok(ApiResponse<EntityGraphDto>.Ok(result, GetTraceId()));
    }

    [HttpGet("entities/{id:guid}/documents")]
    public async Task<IActionResult> GetDocuments(
        Guid id,
        [FromQuery] string? language,
        [FromQuery] int limit = 100,
        CancellationToken ct = default)
    {
        var result = await graph.GetDocumentsAsync(
            RequireUserId(), id, language, limit, ct);
        return Ok(ApiResponse<IReadOnlyList<EntityGraphDocumentDto>>.Ok(
            result, GetTraceId()));
    }

    private Guid RequireUserId()
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId == null)
            throw new UnauthorizedException("User is not authenticated.");
        return currentUser.UserId.Value;
    }
}
