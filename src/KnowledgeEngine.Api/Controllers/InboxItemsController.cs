using KnowledgeEngine.Application.DTOs;
using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Application.Services;
using KnowledgeEngine.Infrastructure.Runtime;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace KnowledgeEngine.Api.Controllers;

/// <summary>
/// Inbox items API.
/// Inbox is the buffer layer for all incoming information sources.
/// Items can be created from mobile capture, desktop, browser extension, etc.
/// Supports text, URL, file, and mixed input types with import workflow.
/// </summary>
[ApiController]
[Route("api/inbox")]
[Authorize]
public class InboxItemsController : BaseController
{
    private readonly RuntimeRouter _runtimeRouter;
    private readonly IConfigService _configService;
    private readonly ICurrentUserContext _currentUser;
    private readonly ImportService _importService;
    private readonly InboxImportService _inboxImportService;

    public InboxItemsController(
        RuntimeRouter runtimeRouter,
        IConfigService configService,
        ICurrentUserContext currentUser,
        ImportService importService,
        InboxImportService inboxImportService)
    {
        _runtimeRouter = runtimeRouter;
        _configService = configService;
        _currentUser = currentUser;
        _importService = importService;
        _inboxImportService = inboxImportService;
    }

    /// <summary>
    /// List inbox items for the current workspace.
    /// Supports filtering by status, inputType, topicId, and pagination.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? status,
        [FromQuery] string? inputType,
        [FromQuery] string? topicId,
        [FromQuery] int limit = 100,
        [FromQuery] int offset = 0,
        CancellationToken ct = default)
    {
        var wsId = await _configService.GetCurrentWorkspaceIdAsync(ct);
        if (wsId == null)
        {
            return Ok(ApiResponse<List<InboxItemDto>>.Ok(new List<InboxItemDto>(), GetTraceId()));
        }

        var repo = await _runtimeRouter.GetRepositoryAsync(ct);
        var items = await repo.ListInboxItemsAsync(wsId, status, inputType, topicId, limit, offset, ct);
        return Ok(ApiResponse<List<InboxItemDto>>.Ok(items, GetTraceId()));
    }

    /// <summary>
    /// Cloud inbox changes endpoint (§16.2).
    /// Used by desktop/hybrid clients to pull cloud inbox items with cursor-based pagination.
    /// - Cloud mode: returns a page of items plus a nextCursor for pagination.
    /// - Local mode: returns all items ordered by created_at (hasMore = false).
    /// </summary>
    [HttpGet("changes")]
    public async Task<IActionResult> Changes(
        [FromQuery] string? cursor,
        [FromQuery] int limit = 100,
        CancellationToken ct = default)
    {
        var wsId = await _configService.GetCurrentWorkspaceIdAsync(ct);
        if (wsId == null)
        {
            return Ok(ApiResponse<InboxChangesDto>.Ok(new InboxChangesDto
            {
                Items = new List<InboxItemDto>(),
                NextCursor = null,
                HasMore = false
            }, GetTraceId()));
        }

        if (limit < 1) limit = 100;
        if (limit > 500) limit = 500;

        var mode = await _runtimeRouter.GetCurrentModeAsync(ct);
        var repo = await _runtimeRouter.GetRepositoryAsync(ct);

        if (mode == "local" || mode == "hybrid")
        {
            // Local mode: return all items ordered by created_at (descending, as
            // implemented by the repository). No cursor pagination needed.
            var localItems = await repo.ListInboxItemsAsync(wsId, null, null, null, limit: 500, offset: 0, ct);
            return Ok(ApiResponse<InboxChangesDto>.Ok(new InboxChangesDto
            {
                Items = localItems,
                NextCursor = null,
                HasMore = false
            }, GetTraceId()));
        }

        // Cloud mode: cursor-based pagination. The cursor is an opaque base64
        // encoding of the numeric offset, so callers don't need to understand
        // the underlying paging scheme.
        var offset = DecodeCursor(cursor);
        var cloudItems = await repo.ListInboxItemsAsync(wsId, null, null, null, limit, offset, ct);
        var hasMore = cloudItems.Count == limit;
        var nextCursor = hasMore ? EncodeCursor(offset + limit) : null;

        return Ok(ApiResponse<InboxChangesDto>.Ok(new InboxChangesDto
        {
            Items = cloudItems,
            NextCursor = nextCursor,
            HasMore = hasMore
        }, GetTraceId()));
    }

    /// <summary>
    /// Get a specific inbox item.
    /// </summary>
    [HttpGet("{id}")]
    public async Task<IActionResult> Get(string id, CancellationToken ct)
    {
        var repo = await _runtimeRouter.GetRepositoryAsync(ct);
        var item = await repo.GetInboxItemAsync(id, ct);
        if (item == null)
        {
            return NotFound(ApiResponse<object>.FailObject("NOT_FOUND", "Inbox item not found", GetTraceId()));
        }
        return Ok(ApiResponse<InboxItemDto>.Ok(item, GetTraceId()));
    }

    /// <summary>
    /// Create a new inbox item.
    /// Uses ImportService to create text or URL items based on inputType.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateInboxItemDto input, CancellationToken ct)
    {
        var wsId = await _configService.GetCurrentWorkspaceIdAsync(ct);
        if (wsId == null)
        {
            return BadRequest(ApiResponse<object>.FailObject("NO_WORKSPACE", "No active workspace", GetTraceId()));
        }

        var topicId = input.TopicId?.ToString();
        var createdFrom = input.CreatedFrom ?? "desktop";

        InboxItemDto item;
        if (input.InputType == "url" || (!string.IsNullOrEmpty(input.SourceUrl) && string.IsNullOrEmpty(input.ContentText)))
        {
            item = await _importService.CreateUrlAsync(
                wsId, input.SourceUrl!, input.Title, topicId, createdFrom, null, ct);
        }
        else
        {
            item = await _importService.CreateTextAsync(
                wsId, input.Title, input.ContentText ?? "", topicId, createdFrom, null, ct);
        }

        return CreatedAtAction(nameof(Get), new { id = item.Id }, ApiResponse<InboxItemDto>.Ok(item, GetTraceId()));
    }

    /// <summary>
    /// Upload a file and create an inbox item from it.
    /// Accepts multipart/form-data with a file upload.
    /// </summary>
    [HttpPost("upload")]
    [RequestSizeLimit(100 * 1024 * 1024)] // 100MB max
    public async Task<IActionResult> Upload([FromForm] UploadInboxFileDto input, CancellationToken ct)
    {
        var wsId = await _configService.GetCurrentWorkspaceIdAsync(ct);
        if (wsId == null)
        {
            return BadRequest(ApiResponse<object>.FailObject("NO_WORKSPACE", "No active workspace", GetTraceId()));
        }

        if (input.File == null || input.File.Length == 0)
        {
            return BadRequest(ApiResponse<object>.FailObject("NO_FILE", "No file provided", GetTraceId()));
        }

        var topicId = input.TopicId?.ToString();
        var createdFrom = input.CreatedFrom ?? "desktop";

        using var stream = input.File.OpenReadStream();
        var item = await _importService.CreateFileAsync(
            wsId,
            input.File.FileName,
            input.File.ContentType,
            stream,
            topicId,
            createdFrom,
            null,
            ct);

        return CreatedAtAction(nameof(Get), new { id = item.Id }, ApiResponse<InboxItemDto>.Ok(item, GetTraceId()));
    }

    /// <summary>
    /// Update an inbox item (title, content, topic, suggestions).
    /// </summary>
    [HttpPatch("{id}")]
    public async Task<IActionResult> Update(string id, [FromBody] UpdateInboxItemDto input, CancellationToken ct)
    {
        var repo = await _runtimeRouter.GetRepositoryAsync(ct);
        await repo.UpdateInboxItemAsync(id, new UpdateInboxItemInput
        {
            Title = input.Title,
            ContentText = input.ContentText,
            TopicId = input.TopicId?.ToString(),
            SuggestedTopicId = input.SuggestedTopicId?.ToString(),
            SuggestedTitle = input.SuggestedTitle,
            SuggestedTags = input.SuggestedTags
        }, ct);

        return Ok(ApiResponse<object>.Ok(new { id, updated = true }, GetTraceId()));
    }

    /// <summary>
    /// Update inbox item status (e.g. mark as imported, archived).
    /// </summary>
    [HttpPut("{id}/status")]
    public async Task<IActionResult> UpdateStatus(string id, [FromBody] UpdateInboxStatusDto input, CancellationToken ct)
    {
        var repo = await _runtimeRouter.GetRepositoryAsync(ct);
        await repo.UpdateInboxItemStatusAsync(id, input.Status, input.ErrorMessage, ct);
        return Ok(ApiResponse<object>.Ok(new { id, status = input.Status }, GetTraceId()));
    }

    /// <summary>
    /// Update the sync status of an inbox item (§16.2).
    /// Called by a local client after it has synced the item from cloud to local.
    /// Updates the item status and records a "synced_to_local" event.
    /// </summary>
    [HttpPost("{id}/sync-status")]
    public async Task<IActionResult> SyncStatus(string id, [FromBody] UpdateSyncStatusDto input, CancellationToken ct)
    {
        var repo = await _runtimeRouter.GetRepositoryAsync(ct);
        var item = await repo.GetInboxItemAsync(id, ct);
        if (item == null)
        {
            return NotFound(ApiResponse<object>.FailObject("NOT_FOUND", "Inbox item not found", GetTraceId()));
        }

        await repo.UpdateInboxItemStatusAsync(id, input.Status, null, ct);

        var payload = System.Text.Json.JsonSerializer.Serialize(new
        {
            status = input.Status,
            localWorkspaceId = input.LocalWorkspaceId,
            syncedAt = input.SyncedAt ?? DateTime.UtcNow
        });
        await repo.CreateInboxEventAsync(item.WorkspaceId, id, "synced_to_local", payload, null, ct);

        return Ok(ApiResponse<object>.Ok(new { id, status = input.Status, synced = true }, GetTraceId()));
    }

    /// <summary>
    /// Import a single inbox item into the knowledge base as a source.
    /// </summary>
    [HttpPost("{id}/import")]
    public async Task<IActionResult> Import(string id, [FromBody] ImportInboxItemDto? input, CancellationToken ct)
    {
        try
        {
            var topicId = input?.TopicId?.ToString();
            var source = await _inboxImportService.ImportOneAsync(id, topicId, ct);
            return Ok(ApiResponse<SourceDto>.Ok(source, GetTraceId()));
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(ApiResponse<object>.FailObject("NOT_FOUND", ex.Message, GetTraceId()));
        }
        catch (Exception ex)
        {
            return BadRequest(ApiResponse<object>.FailObject("IMPORT_FAILED", ex.Message, GetTraceId()));
        }
    }

    /// <summary>
    /// Batch import multiple inbox items.
    /// </summary>
    [HttpPost("batch-import")]
    public async Task<IActionResult> BatchImport([FromBody] BatchImportInboxDto input, CancellationToken ct)
    {
        var ids = input.InboxItemIds.Select(g => g.ToString()).ToList();
        var topicId = input.TopicId?.ToString();
        var result = await _inboxImportService.ImportBatchAsync(ids, topicId, ct);
        return Ok(ApiResponse<object>.Ok(result, GetTraceId()));
    }

    /// <summary>
    /// Archive a single inbox item (soft delete).
    /// </summary>
    [HttpPost("{id}/archive")]
    public async Task<IActionResult> Archive(string id, CancellationToken ct)
    {
        var repo = await _runtimeRouter.GetRepositoryAsync(ct);
        await repo.ArchiveInboxItemAsync(id, ct);
        return Ok(ApiResponse<object>.Ok(new { id, archived = true }, GetTraceId()));
    }

    /// <summary>
    /// Batch archive multiple inbox items.
    /// </summary>
    [HttpPost("batch-archive")]
    public async Task<IActionResult> BatchArchive([FromBody] BatchArchiveInboxDto input, CancellationToken ct)
    {
        var repo = await _runtimeRouter.GetRepositoryAsync(ct);
        var archived = new List<string>();
        foreach (var id in input.InboxItemIds)
        {
            await repo.ArchiveInboxItemAsync(id.ToString(), ct);
            archived.Add(id.ToString());
        }
        return Ok(ApiResponse<object>.Ok(new { archived, count = archived.Count }, GetTraceId()));
    }

    /// <summary>
    /// Retry a failed inbox item import.
    /// Resets status to "pending", increments retry_count, creates "retried" event.
    /// </summary>
    [HttpPost("{id}/retry")]
    public async Task<IActionResult> Retry(string id, CancellationToken ct)
    {
        var repo = await _runtimeRouter.GetRepositoryAsync(ct);
        var item = await repo.GetInboxItemAsync(id, ct);
        if (item == null)
        {
            return NotFound(ApiResponse<object>.FailObject("NOT_FOUND", "Inbox item not found", GetTraceId()));
        }

        // Reset status to pending
        await repo.UpdateInboxItemStatusAsync(id, "pending", null, ct);

        // Increment retry_count in the database
        await repo.IncrementRetryCountAsync(id, ct);

        // Create "retried" event
        await repo.CreateInboxEventAsync(item.WorkspaceId, id, "retried",
            $"{{\"previousStatus\":\"{item.Status}\",\"retryCount\":{item.RetryCount + 1}}}", null, ct);

        return Ok(ApiResponse<object>.Ok(new { id, status = "pending", retried = true }, GetTraceId()));
    }

    /// <summary>
    /// List inbox events for a specific item.
    /// </summary>
    [HttpGet("{id}/events")]
    public async Task<IActionResult> ListEvents(string id, CancellationToken ct)
    {
        var repo = await _runtimeRouter.GetRepositoryAsync(ct);
        var events = await repo.ListInboxEventsAsync(id, ct);
        return Ok(ApiResponse<List<InboxEventDto>>.Ok(events, GetTraceId()));
    }

    /// <summary>
    /// List attachments for a specific inbox item.
    /// </summary>
    [HttpGet("{id}/attachments")]
    public async Task<IActionResult> ListAttachments(string id, CancellationToken ct)
    {
        var repo = await _runtimeRouter.GetRepositoryAsync(ct);
        var attachments = await repo.ListInboxAttachmentsAsync(id, ct);
        return Ok(ApiResponse<List<InboxAttachmentDto>>.Ok(attachments, GetTraceId()));
    }

    /// <summary>
    /// Delete an inbox item (soft delete via archive).
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id, CancellationToken ct)
    {
        var repo = await _runtimeRouter.GetRepositoryAsync(ct);
        await repo.ArchiveInboxItemAsync(id, ct);
        return Ok(ApiResponse<object>.Ok(new { id, archived = true }, GetTraceId()));
    }

    // ===== Cursor helpers =====

    /// <summary>
    /// Encodes a numeric offset into an opaque cursor string (base64).
    /// </summary>
    private static string? EncodeCursor(int offset)
    {
        if (offset <= 0) return null;
        return Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(offset.ToString()));
    }

    /// <summary>
    /// Decodes an opaque cursor string back into a numeric offset.
    /// Returns 0 for null/empty/invalid cursors.
    /// </summary>
    private static int DecodeCursor(string? cursor)
    {
        if (string.IsNullOrEmpty(cursor)) return 0;
        try
        {
            var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
            return int.TryParse(decoded, out var offset) ? Math.Max(0, offset) : 0;
        }
        catch
        {
            return 0;
        }
    }
}

// ===== DTOs =====

public class UpdateInboxStatusDto
{
    public string Status { get; set; } = "pending";
    public string? ErrorMessage { get; set; }
}

public class UploadInboxFileDto
{
    public IFormFile? File { get; set; }
    public Guid? TopicId { get; set; }
    public string? CreatedFrom { get; set; } = "desktop";
}

public class ImportInboxItemDto
{
    public Guid? TopicId { get; set; }
}

/// <summary>
/// Response payload for the inbox changes endpoint (cursor-based pagination).
/// </summary>
public class InboxChangesDto
{
    public List<InboxItemDto> Items { get; set; } = new();
    public string? NextCursor { get; set; }
    public bool HasMore { get; set; }
}

/// <summary>
/// Request body for updating an inbox item's sync status.
/// </summary>
public class UpdateSyncStatusDto
{
    public string Status { get; set; } = "synced";
    public string? LocalWorkspaceId { get; set; }
    public DateTime? SyncedAt { get; set; }
}
