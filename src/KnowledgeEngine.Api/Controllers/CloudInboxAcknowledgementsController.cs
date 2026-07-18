using System.Security.Cryptography;
using System.Text;
using KnowledgeEngine.Application.DTOs;
using KnowledgeEngine.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeEngine.Api.Controllers;

[ApiController]
[Route("v1/inbox/items")]
[Route("api/workspaces/{workspaceId:guid}/inbox/items")]
[Authorize]
public sealed class CloudInboxAcknowledgementsController : BaseController
{
    private readonly IAppDbContext _db;
    private readonly IFileStorageFactory _storageFactory;
    private readonly IWorkspaceAuthorizationService _workspaceAuthorization;

    public CloudInboxAcknowledgementsController(
        IAppDbContext db,
        IFileStorageFactory storageFactory,
        IWorkspaceAuthorizationService workspaceAuthorization)
    {
        _db = db;
        _storageFactory = storageFactory;
        _workspaceAuthorization = workspaceAuthorization;
    }

    [HttpPost("{itemId:guid}/ack")]
    public async Task<IActionResult> Acknowledge(
        Guid itemId,
        [FromRoute] Guid? workspaceId,
        [FromBody] CloudInboxAcknowledgementRequest input,
        CancellationToken ct)
    {
        var retention = input.Retention is "deleteOriginal" or "deleteAll"
            ? input.Retention
            : "keep";
        if (!Guid.TryParse(input.CloudWorkspaceId, out var cloudWorkspaceId))
        {
            return BadRequest(ApiResponse<object>.FailObject(
                "INVALID_CLOUD_WORKSPACE", "云端工作区 ID 无效", GetTraceId()));
        }
        if (workspaceId.HasValue && workspaceId.Value != cloudWorkspaceId)
        {
            return BadRequest(ApiResponse<object>.FailObject(
                "WORKSPACE_MISMATCH",
                "路由工作区与请求工作区不一致",
                GetTraceId()));
        }
        var access = await _workspaceAuthorization.AuthorizeAsync(
            cloudWorkspaceId, ct);
        if (access != WorkspaceAccessResult.Allowed)
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                ApiResponse<object>.FailObject(
                    "WORKSPACE_FORBIDDEN",
                    "无权确认该云端工作区的 Inbox 条目",
                    GetTraceId()));
        }
        if (string.IsNullOrWhiteSpace(input.IdempotencyKey))
        {
            return BadRequest(ApiResponse<object>.FailObject(
                "IDEMPOTENCY_KEY_REQUIRED", "确认请求缺少幂等键", GetTraceId()));
        }

        var acknowledgementKey = BuildAcknowledgementKey(input.IdempotencyKey);
        var existing = await _db.WorkspaceSettings.FirstOrDefaultAsync(x =>
            x.WorkspaceId == cloudWorkspaceId && x.Key == acknowledgementKey, ct);
        if (existing != null)
        {
            return Ok(ApiResponse<CloudInboxAcknowledgementResponse>.Ok(new()
            {
                Acknowledged = true,
                RetentionApplied = existing.Value,
                AlreadyProcessed = true
            }, GetTraceId()));
        }

        var item = await _db.InboxItems.FirstOrDefaultAsync(x =>
            x.Id == itemId && x.WorkspaceId == cloudWorkspaceId, ct);
        if (item == null)
        {
            return NotFound(ApiResponse<object>.FailObject(
                "INBOX_ITEM_NOT_FOUND", "云端 Inbox 条目不存在", GetTraceId()));
        }

        if (retention is "deleteOriginal" or "deleteAll")
        {
            await DeleteOriginalFilesAsync(itemId, cloudWorkspaceId, ct);
        }
        if (retention == "deleteAll")
        {
            _db.InboxItems.Remove(item);
        }
        else
        {
            item.Status = "imported";
            item.ImportedAt = DateTime.UtcNow;
            item.UpdatedAt = DateTime.UtcNow;
            if (retention == "deleteOriginal") item.FilePath = null;
        }

        _db.WorkspaceSettings.Add(new KnowledgeEngine.Domain.Entities.WorkspaceSetting
        {
            Id = Guid.CreateVersion7(),
            WorkspaceId = cloudWorkspaceId,
            Key = acknowledgementKey,
            Value = retention,
            UpdatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync(ct);

        return Ok(ApiResponse<CloudInboxAcknowledgementResponse>.Ok(new()
        {
            Acknowledged = true,
            RetentionApplied = retention,
            AlreadyProcessed = false
        }, GetTraceId()));
    }

    private async Task DeleteOriginalFilesAsync(
        Guid itemId,
        Guid workspaceId,
        CancellationToken ct)
    {
        var attachments = await _db.InboxAttachments
            .Where(x => x.InboxItemId == itemId && x.WorkspaceId == workspaceId)
            .ToListAsync(ct);
        var fileIds = attachments.Select(x => x.FileId).Distinct().ToList();
        var files = await _db.Files.Where(x => fileIds.Contains(x.Id)).ToListAsync(ct);
        var storage = await _storageFactory.GetProviderForWorkspaceAsync(
            workspaceId.ToString(), ct);
        foreach (var file in files)
        {
            if (!string.IsNullOrWhiteSpace(file.ObjectKey))
            {
                await storage.DeleteFileAsync(file.Bucket, file.ObjectKey, ct);
            }
        }
        _db.InboxAttachments.RemoveRange(attachments);
        _db.Files.RemoveRange(files);
    }

    private static string BuildAcknowledgementKey(string idempotencyKey)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(idempotencyKey));
        return $"cloud_inbox_ack_{Convert.ToHexString(hash).ToLowerInvariant()}";
    }
}

public sealed class CloudInboxAcknowledgementRequest
{
    public string CloudWorkspaceId { get; set; } = string.Empty;
    public string LocalWorkspaceId { get; set; } = string.Empty;
    public Guid? LocalInboxItemId { get; set; }
    public string Result { get; set; } = "imported";
    public string Retention { get; set; } = "keep";
    public string IdempotencyKey { get; set; } = string.Empty;
}

public sealed class CloudInboxAcknowledgementResponse
{
    public bool Acknowledged { get; set; }
    public string RetentionApplied { get; set; } = "keep";
    public bool AlreadyProcessed { get; set; }
}
