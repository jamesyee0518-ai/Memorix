using System.Text.Json;
using KnowledgeEngine.Application.Exceptions;
using KnowledgeEngine.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace KnowledgeEngine.Application.Services;

/// <summary>
/// Inbox management service (P2-1, §17.2).
///
/// Extracts inbox CRUD logic out of the controller so the controller stays
/// thin. Each mutating method also writes an <c>inbox_event</c> row for
/// auditability.
///
/// All data access goes through <see cref="IKnowledgeRepository"/>, which the
/// <c>RuntimeRepositoryFacade</c> routes to the correct implementation
/// (local SQLite or cloud PostgreSQL) based on the current workspace mode.
/// </summary>
public class InboxService
{
    private readonly IKnowledgeRepository _repo;
    private readonly ILogger<InboxService> _logger;

    public InboxService(
        IKnowledgeRepository repo,
        ILogger<InboxService> logger)
    {
        _repo = repo;
        _logger = logger;
    }

    /// <summary>
    /// Paged result for inbox item listings.
    /// Uses limit/offset pagination to match the repository contract.
    /// </summary>
    public class InboxItemPagedResult
    {
        public List<InboxItemDto> Items { get; set; } = new();
        public int Total { get; set; }
        public int Limit { get; set; }
        public int Offset { get; set; }
    }

    /// <summary>
    /// Lists inbox items for a workspace with optional filters and pagination.
    /// Returns both the page of items and the total matching count.
    /// </summary>
    public async Task<InboxItemPagedResult> ListAsync(
        string workspaceId,
        string? status = null,
        string? inputType = null,
        string? topicId = null,
        int limit = 100,
        int offset = 0,
        CancellationToken ct = default)
    {
        // Clamp pagination values to sane bounds
        if (limit < 1) limit = 100;
        if (limit > 500) limit = 500;
        if (offset < 0) offset = 0;

        var items = await _repo.ListInboxItemsAsync(
            workspaceId, status, inputType, topicId, limit, offset, ct);

        // Count uses only the status filter (matches the repository contract).
        var total = await _repo.CountInboxItemsAsync(workspaceId, status, ct);

        _logger.LogDebug(
            "Listed inbox items: workspace={WorkspaceId}, count={Count}, total={Total}",
            workspaceId, items.Count, total);

        return new InboxItemPagedResult
        {
            Items = items,
            Total = total,
            Limit = limit,
            Offset = offset
        };
    }

    /// <summary>
    /// Gets a single inbox item by id.
    /// Throws <see cref="NotFoundException"/> when the item does not exist.
    /// </summary>
    public async Task<InboxItemDto> GetAsync(string id, CancellationToken ct = default)
    {
        var item = await _repo.GetInboxItemAsync(id, ct);
        if (item == null)
        {
            throw new NotFoundException("Inbox item", id);
        }
        return item;
    }

    /// <summary>
    /// Updates an inbox item (title, content, topic, suggestions) and records
    /// an "updated" inbox event for auditability.
    /// </summary>
    public async Task UpdateAsync(string id, UpdateInboxItemInput input, CancellationToken ct = default)
    {
        // Fetch first so we can record the event against the correct workspace.
        var item = await _repo.GetInboxItemAsync(id, ct);
        if (item == null)
        {
            throw new NotFoundException("Inbox item", id);
        }

        await _repo.UpdateInboxItemAsync(id, input, ct);

        var payload = JsonSerializer.Serialize(new
        {
            title = input.Title,
            topicId = input.TopicId,
            suggestedTopicId = input.SuggestedTopicId,
            suggestedTitle = input.SuggestedTitle
        });
        await _repo.CreateInboxEventAsync(item.WorkspaceId, id, "updated", payload, null, ct);

        _logger.LogInformation("Updated inbox item: {Id}", id);
    }

    /// <summary>
    /// Archives a single inbox item (soft delete) and records an "archived"
    /// inbox event.
    /// </summary>
    public async Task ArchiveAsync(string id, CancellationToken ct = default)
    {
        var item = await _repo.GetInboxItemAsync(id, ct);
        if (item == null)
        {
            throw new NotFoundException("Inbox item", id);
        }

        await _repo.ArchiveInboxItemAsync(id, ct);

        var payload = JsonSerializer.Serialize(new
        {
            previousStatus = item.Status,
            archivedAt = DateTime.UtcNow
        });
        await _repo.CreateInboxEventAsync(item.WorkspaceId, id, "archived", payload, null, ct);

        _logger.LogInformation("Archived inbox item: {Id}", id);
    }

    /// <summary>
    /// Permanently deletes an inbox item.
    /// </summary>
    public async Task DeleteAsync(string id, CancellationToken ct = default)
    {
        await _repo.DeleteInboxItemAsync(id, ct);
        _logger.LogInformation("Deleted inbox item: {Id}", id);
    }

    /// <summary>
    /// Retries a failed inbox item import.
    /// Resets the status to "pending" and records a "retried" inbox event.
    ///
    /// Note: <see cref="IKnowledgeRepository.UpdateInboxItemStatusAsync"/> does
    /// not expose a retry_count parameter, so the incremented retry count is
    /// captured in the event payload (consistent with the existing controller
    /// behaviour).
    /// </summary>
    public async Task<InboxItemDto> RetryAsync(string id, CancellationToken ct = default)
    {
        var item = await _repo.GetInboxItemAsync(id, ct);
        if (item == null)
        {
            throw new NotFoundException("Inbox item", id);
        }

        var previousStatus = item.Status;

        // Reset status to pending so the import pipeline picks it up again.
        await _repo.UpdateInboxItemStatusAsync(id, "pending", null, ct);

        // Record the retry event with the new (incremented) retry count.
        var newRetryCount = item.RetryCount + 1;
        var payload = JsonSerializer.Serialize(new
        {
            previousStatus,
            retryCount = newRetryCount
        });
        await _repo.CreateInboxEventAsync(item.WorkspaceId, id, "retried", payload, null, ct);

        _logger.LogInformation(
            "Retried inbox item: {Id} (previousStatus={PreviousStatus}, retryCount={RetryCount})",
            id, previousStatus, newRetryCount);

        // Return a refreshed view of the item with the reset status.
        item.Status = "pending";
        item.ErrorMessage = null;
        item.RetryCount = newRetryCount;
        return item;
    }
}
