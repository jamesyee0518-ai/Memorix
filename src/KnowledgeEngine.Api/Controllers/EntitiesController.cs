using KnowledgeEngine.Application.DTOs;
using KnowledgeEngine.Application.Exceptions;
using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace KnowledgeEngine.Api.Controllers;

[Authorize]
public class EntitiesController(
    EntityService entityService,
    IEntityMergeService mergeService,
    ICurrentUserContext currentUser) : BaseController
{
    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] string? entityType,
        [FromQuery] string? search,
        [FromQuery] string? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var result = await entityService.GetAllAsync(entityType, search, status, page, pageSize, ct);
        return Ok(ApiResponse<PagedResult<EntityListItem>>.Ok(result.Data!, GetTraceId()));
    }

    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CreateEntityRequest request, CancellationToken ct)
    {
        var result = await entityService.CreateAsync(request, ct);
        return Ok(ApiResponse<EntityDetail>.Ok(result, GetTraceId()));
    }

    [HttpPatch("{id:guid}")]
    public async Task<IActionResult> Update(
        Guid id, [FromBody] UpdateEntityRequest request, CancellationToken ct)
    {
        var result = await entityService.UpdateAsync(id, request, ct);
        return Ok(ApiResponse<EntityDetail>.Ok(result, GetTraceId()));
    }

    [HttpGet("{id:guid}/aliases")]
    public async Task<IActionResult> GetAliases(Guid id, CancellationToken ct)
    {
        var result = await entityService.GetAliasesAsync(id, ct);
        return Ok(ApiResponse<IReadOnlyList<EntityAliasItem>>.Ok(result, GetTraceId()));
    }

    [HttpPost("{id:guid}/aliases")]
    public async Task<IActionResult> AddAlias(
        Guid id, [FromBody] UpsertEntityAliasRequest request, CancellationToken ct)
    {
        var result = await entityService.AddAliasAsync(id, request, ct);
        return Ok(ApiResponse<EntityAliasItem>.Ok(result, GetTraceId()));
    }

    [HttpPatch("{id:guid}/aliases/{aliasId:guid}")]
    public async Task<IActionResult> UpdateAlias(
        Guid id, Guid aliasId,
        [FromBody] UpsertEntityAliasRequest request, CancellationToken ct)
    {
        var result = await entityService.UpdateAliasAsync(id, aliasId, request, ct);
        return Ok(ApiResponse<EntityAliasItem>.Ok(result, GetTraceId()));
    }

    [HttpDelete("{id:guid}/aliases/{aliasId:guid}")]
    public async Task<IActionResult> DeleteAlias(
        Guid id, Guid aliasId, CancellationToken ct)
    {
        var removed = await entityService.DeleteAliasAsync(id, aliasId, ct);
        return removed
            ? Ok(ApiResponse<object>.Ok(new { id = aliasId }, GetTraceId()))
            : NotFound(ApiResponse<object>.Fail(
                "ENTITY_ALIAS_NOT_FOUND", "实体别名不存在。", GetTraceId()));
    }

    [HttpGet("{id:guid}/mentions")]
    public async Task<IActionResult> GetMentions(
        Guid id, [FromQuery] int limit = 100, CancellationToken ct = default)
    {
        var result = await entityService.GetMentionsAsync(id, limit, ct);
        return Ok(ApiResponse<IReadOnlyList<EntityMentionItem>>.Ok(
            result, GetTraceId()));
    }

    [HttpGet("{id:guid}/relations")]
    public async Task<IActionResult> GetRelations(
        Guid id, [FromQuery] int limit = 100, CancellationToken ct = default)
    {
        var result = await entityService.GetRelationsAsync(id, limit, ct);
        return Ok(ApiResponse<IReadOnlyList<EntityRelationItem>>.Ok(
            result, GetTraceId()));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById([FromRoute] Guid id, CancellationToken ct)
    {
        var result = await entityService.GetByIdAsync(id, ct);
        return Ok(ApiResponse<EntityDetail>.Ok(result.Data!, GetTraceId()));
    }

    [HttpPost("merge-preview")]
    public async Task<IActionResult> MergePreview(
        [FromBody] EntityMergePreviewRequest request,
        CancellationToken ct)
    {
        var result = await mergeService.PreviewAsync(RequireUserId(), request, ct);
        return Ok(ApiResponse<EntityMergePreview>.Ok(result, GetTraceId()));
    }

    [HttpPost("merge")]
    public async Task<IActionResult> Merge(
        [FromBody] ExecuteEntityMergeRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
            request.IdempotencyKey = idempotencyKey ?? string.Empty;
        request.RequestId ??= GetTraceId();
        var result = await mergeService.MergeAsync(RequireUserId(), request, ct);
        return Ok(ApiResponse<EntityMergeResult>.Ok(result, GetTraceId()));
    }

    [HttpGet("merge-history")]
    public async Task<IActionResult> MergeHistory(
        [FromQuery] Guid? workspaceId,
        [FromQuery] int limit = 100,
        CancellationToken ct = default)
    {
        var result = await mergeService.GetHistoryAsync(
            RequireUserId(), workspaceId, limit, ct);
        return Ok(ApiResponse<IReadOnlyList<EntityMergeHistoryItem>>.Ok(
            result, GetTraceId()));
    }

    [HttpPost("merges/{mergeId:guid}/revert")]
    public async Task<IActionResult> RevertMerge(
        Guid mergeId,
        [FromBody] RevertEntityMergeRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken ct)
    {
        var key = string.IsNullOrWhiteSpace(request.IdempotencyKey)
            ? idempotencyKey
            : request.IdempotencyKey;
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Idempotency-Key is required.");
        var result = await mergeService.RevertAsync(
            RequireUserId(), mergeId, request.RequestId ?? key, ct);
        return Ok(ApiResponse<EntityMergeResult>.Ok(result, GetTraceId()));
    }

    [HttpPost("merge-blocklist")]
    public async Task<IActionResult> AddMergeBlock(
        [FromBody] AddEntityMergeBlockRequest request,
        CancellationToken ct)
    {
        var id = await mergeService.AddBlockAsync(RequireUserId(), request, ct);
        return Ok(ApiResponse<object>.Ok(new { id }, GetTraceId()));
    }

    [HttpDelete("merge-blocklist/{id:guid}")]
    public async Task<IActionResult> RemoveMergeBlock(Guid id, CancellationToken ct)
    {
        var removed = await mergeService.RemoveBlockAsync(RequireUserId(), id, ct);
        return removed
            ? Ok(ApiResponse<object>.Ok(new { id }, GetTraceId()))
            : NotFound(ApiResponse<object>.Fail(
                "ENTITY_MERGE_BLOCK_NOT_FOUND", "禁止合并记录不存在。", GetTraceId()));
    }

    private Guid RequireUserId()
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId == null)
            throw new UnauthorizedException("User is not authenticated.");
        return currentUser.UserId.Value;
    }
}
