using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Domain.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeEngine.Api.Controllers;

[Route("api/agent/inbox")]
public class AgentInboxController : AgentApiControllerBase
{
    private readonly IAppDbContext _db;

    public AgentInboxController(IAppDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Creates an inbox item (URL or text) that will be processed by the
    /// document pipeline.  Equivalent to the MCP <c>create_inbox_item</c> tool.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> CreateInboxItem([FromBody] CreateAgentInboxRequest request, CancellationToken ct)
    {
        var userId = AgentUserId;
        if (userId == null)
        {
            return Unauthorized();
        }

        // Check action permission
        if (!CheckActionAllowed("inbox:write"))
        {
            return Forbidden("permission_denied", "No permission to write inbox");
        }

        // Validate source type
        var sourceType = string.IsNullOrWhiteSpace(request.SourceType) ? "text" : request.SourceType;
        if (sourceType != "url" && sourceType != "text")
        {
            return BadRequest(new
            {
                success = false,
                error = new { code = "invalid_source_type", message = "source_type must be 'url' or 'text'." },
                trace_id = GetTraceId()
            });
        }

        if (sourceType == "url" && string.IsNullOrWhiteSpace(request.SourceUrl))
        {
            return BadRequest(new
            {
                success = false,
                error = new { code = "missing_source_url", message = "source_url is required when source_type is 'url'." },
                trace_id = GetTraceId()
            });
        }

        if (sourceType == "text" && string.IsNullOrWhiteSpace(request.Content))
        {
            return BadRequest(new
            {
                success = false,
                error = new { code = "missing_content", message = "content is required when source_type is 'text'." },
                trace_id = GetTraceId()
            });
        }

        // Check topic permission
        if (request.TopicId.HasValue && !CheckTopicAllowed(request.TopicId.Value))
        {
            return Forbidden("TOPIC_NOT_ALLOWED", "This API key does not have access to the requested topic.");
        }

        // Resolve the user's workspace (InboxItem.WorkspaceId is required)
        var workspaceId = await ResolveWorkspaceIdAsync(userId.Value, ct);
        if (workspaceId == null)
        {
            return NotFound(new
            {
                success = false,
                error = new { code = "workspace_not_found", message = "No workspace found for the user." },
                trace_id = GetTraceId()
            });
        }

        var now = DateTime.UtcNow;
        var inboxItem = new InboxItem
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId.Value,
            UserId = userId.Value,
            TopicId = request.TopicId,
            InputType = sourceType,
            ItemType = sourceType,
            Title = request.Title ?? (sourceType == "url" ? request.SourceUrl : "Agent 导入文本"),
            ContentText = request.Content,
            SourceUrl = sourceType == "url" ? request.SourceUrl : null,
            Status = "pending",
            CreatedFrom = "api",
            CreatedAt = now,
            UpdatedAt = now
        };

        _db.InboxItems.Add(inboxItem);
        await _db.SaveChangesAsync(ct);

        return Ok(new
        {
            success = true,
            data = new
            {
                inbox_item_id = inboxItem.Id,
                status = "pending",
                message = "已添加到收件箱，系统将自动处理"
            },
            trace_id = GetTraceId()
        });
    }

    /// <summary>
    /// Triggers a URL import by creating an inbox item with source_type="url".
    /// Equivalent to the MCP <c>import_url</c> tool.
    /// </summary>
    [HttpPost("import-url")]
    public async Task<IActionResult> ImportUrl([FromBody] ImportAgentUrlRequest request, CancellationToken ct)
    {
        var userId = AgentUserId;
        if (userId == null)
        {
            return Unauthorized();
        }

        // Check action permission
        if (!CheckActionAllowed("inbox:write"))
        {
            return Forbidden("permission_denied", "No permission to write inbox");
        }

        if (string.IsNullOrWhiteSpace(request.Url))
        {
            return BadRequest(new
            {
                success = false,
                error = new { code = "missing_url", message = "url is required." },
                trace_id = GetTraceId()
            });
        }

        // Check topic permission
        if (request.TopicId.HasValue && !CheckTopicAllowed(request.TopicId.Value))
        {
            return Forbidden("TOPIC_NOT_ALLOWED", "This API key does not have access to the requested topic.");
        }

        // Resolve the user's workspace (InboxItem.WorkspaceId is required)
        var workspaceId = await ResolveWorkspaceIdAsync(userId.Value, ct);
        if (workspaceId == null)
        {
            return NotFound(new
            {
                success = false,
                error = new { code = "workspace_not_found", message = "No workspace found for the user." },
                trace_id = GetTraceId()
            });
        }

        var now = DateTime.UtcNow;
        var inboxItem = new InboxItem
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId.Value,
            UserId = userId.Value,
            TopicId = request.TopicId,
            InputType = "url",
            ItemType = "url",
            SourceUrl = request.Url,
            Title = request.Title ?? request.Url,
            Status = "pending",
            CreatedFrom = "api",
            CreatedAt = now,
            UpdatedAt = now
        };

        _db.InboxItems.Add(inboxItem);
        await _db.SaveChangesAsync(ct);

        return Ok(new
        {
            success = true,
            data = new
            {
                inbox_item_id = inboxItem.Id,
                status = "pending",
                message = $"URL {request.Url} 已加入导入队列"
            },
            trace_id = GetTraceId()
        });
    }

    /// <summary>
    /// Resolves the workspace ID for a given user.
    /// </summary>
    private async Task<Guid?> ResolveWorkspaceIdAsync(Guid userId, CancellationToken ct)
    {
        var workspace = await _db.Workspaces
            .FirstOrDefaultAsync(w => w.UserId == userId, ct);
        return workspace?.Id;
    }
}

/// <summary>
/// Request body for POST /api/agent/inbox.
/// </summary>
public class CreateAgentInboxRequest
{
    /// <summary>来源类型：url 或 text</summary>
    public string SourceType { get; set; } = "text";

    /// <summary>URL 地址（source_type 为 url 时必填）</summary>
    public string? SourceUrl { get; set; }

    /// <summary>文本内容（source_type 为 text 时必填）</summary>
    public string? Content { get; set; }

    /// <summary>标题（可选）</summary>
    public string? Title { get; set; }

    /// <summary>归属专题 ID（可选）</summary>
    public Guid? TopicId { get; set; }
}

/// <summary>
/// Request body for POST /api/agent/inbox/import-url.
/// </summary>
public class ImportAgentUrlRequest
{
    /// <summary>要导入的网页 URL</summary>
    public string? Url { get; set; }

    /// <summary>自定义标题（可选）</summary>
    public string? Title { get; set; }

    /// <summary>归属专题 ID（可选）</summary>
    public Guid? TopicId { get; set; }
}
