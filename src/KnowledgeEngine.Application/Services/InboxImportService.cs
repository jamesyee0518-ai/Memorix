using System.Security.Cryptography;
using System.Text;
using KnowledgeEngine.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace KnowledgeEngine.Application.Services;

/// <summary>
/// Service for importing inbox items into the knowledge base as sources (§17.3).
/// Handles the conversion from inbox_item → source, with import job tracking
/// and inbox event logging.
/// </summary>
public class InboxImportService
{
    private readonly IKnowledgeRepository _repo;
    private readonly IPushNotificationService _pushNotifications;
    private readonly ILogger<InboxImportService> _logger;

    public InboxImportService(
        IKnowledgeRepository repo,
        IPushNotificationService pushNotifications,
        ILogger<InboxImportService> logger)
    {
        _repo = repo;
        _pushNotifications = pushNotifications;
        _logger = logger;
    }

    /// <summary>
    /// Result of a single inbox item import.
    /// </summary>
    public class ImportOneResult
    {
        public string InboxItemId { get; set; } = string.Empty;
        public bool Success { get; set; }
        public SourceDto? Source { get; set; }
        public string? ErrorMessage { get; set; }
    }

    /// <summary>
    /// Result of a batch import.
    /// </summary>
    public class BatchImportResult
    {
        public int Total { get; set; }
        public int Succeeded { get; set; }
        public int Failed { get; set; }
        public List<ImportOneResult> Results { get; set; } = new();
    }

    /// <summary>
    /// Imports a single inbox item into the knowledge base.
    /// 1. Get inbox item
    /// 2. Create import job (status="running")
    /// 3. Based on inputType: create a Source from the inbox item data
    /// 4. Update inbox item status to "imported", set source_id, imported_at
    /// 5. Update import job status to "succeeded", set source_id
    /// 6. Create inbox event "imported"
    /// 7. Return the source DTO
    /// On error: update job to "failed", update inbox item to "failed", create "failed" event, throw
    /// </summary>
    public async Task<SourceDto> ImportOneAsync(string inboxItemId, string? topicId = null, CancellationToken ct = default)
    {
        // 1) Get the inbox item
        var inboxItem = await _repo.GetInboxItemAsync(inboxItemId, ct);
        if (inboxItem == null)
        {
            throw new InvalidOperationException($"Inbox item not found: {inboxItemId}");
        }

        // 2) Create import job
        var jobType = inboxItem.InputType switch
        {
            "url" => "url_import",
            "file" or "image" or "audio" or "pdf" => "file_import",
            "mixed" => "mixed_import",
            _ => "text_import"
        };

        var job = await _repo.CreateImportJobAsync(new CreateImportJobInput
        {
            WorkspaceId = inboxItem.WorkspaceId,
            InboxItemId = inboxItemId,
            JobType = jobType
        }, ct);

        try
        {
            // 3) Create a Source from the inbox item data
            var effectiveTopicId = topicId ?? inboxItem.TopicId;
            var sourceType = inboxItem.InputType switch
            {
                "url" => "web",
                "file" or "image" or "audio" or "pdf" => "file",
                "mixed" => "mixed",
                _ => "text"
            };

            // Compute content hash for text content
            string? contentHash = null;
            if (!string.IsNullOrEmpty(inboxItem.ContentText))
            {
                contentHash = ComputeSha256(inboxItem.ContentText);
            }

            var source = await _repo.CreateSourceAsync(new CreateSourceInput
            {
                WorkspaceId = inboxItem.WorkspaceId,
                TopicId = effectiveTopicId,
                InboxItemId = inboxItemId,
                SourceType = sourceType,
                Title = inboxItem.Title ?? inboxItem.SourceUrl ?? "Untitled",
                Url = inboxItem.SourceUrl,
                LocalFilePath = inboxItem.FilePath,
                ContentHash = contentHash,
                RawText = inboxItem.ContentText
            }, ct);

            // 4) Update inbox item status to "imported"
            await UpdateInboxItemImportedAsync(inboxItemId, source.Id, ct);

            // 5) Update import job status to "succeeded"
            await _repo.UpdateImportJobAsync(job.Id, "succeeded", source.Id, null, null, ct);

            // 6) Create inbox event "imported"
            await _repo.CreateInboxEventAsync(inboxItem.WorkspaceId, inboxItemId, "imported",
                $"{{\"sourceId\":\"{source.Id}\",\"sourceType\":\"{sourceType}\"}}", null, ct);

            await _pushNotifications.SendToDeviceAsync(
                inboxItem.WorkspaceId,
                inboxItem.OriginDeviceId,
                "已导入知识库",
                inboxItem.Title ?? inboxItem.SourceUrl ?? "手机采集内容已完成导入",
                new Dictionary<string, string>
                {
                    ["event"] = "inbox_imported",
                    ["inboxItemId"] = inboxItemId,
                    ["sourceId"] = source.Id
                },
                ct);

            _logger.LogInformation("Imported inbox item {InboxItemId} → source {SourceId}", inboxItemId, source.Id);
            return source;
        }
        catch (Exception ex)
        {
            // Update job to "failed"
            await _repo.UpdateImportJobAsync(job.Id, "failed", null, "IMPORT_ERROR", ex.Message, ct);

            // Update inbox item to "failed"
            await _repo.UpdateInboxItemStatusAsync(inboxItemId, "failed", ex.Message, ct);

            // Create "failed" event
            await _repo.CreateInboxEventAsync(inboxItem.WorkspaceId, inboxItemId, "failed",
                $"{{\"error\":\"{EscapeJson(ex.Message)}\"}}", null, ct);

            await _pushNotifications.SendToDeviceAsync(
                inboxItem.WorkspaceId,
                inboxItem.OriginDeviceId,
                "导入失败",
                ex.Message,
                new Dictionary<string, string>
                {
                    ["event"] = "inbox_import_failed",
                    ["inboxItemId"] = inboxItemId
                },
                ct);

            _logger.LogError(ex, "Failed to import inbox item {InboxItemId}: {Message}", inboxItemId, ex.Message);
            throw;
        }
    }

    /// <summary>
    /// Imports multiple inbox items in batch.
    /// Returns results with success/failure counts.
    /// </summary>
    public async Task<BatchImportResult> ImportBatchAsync(List<string> inboxItemIds, string? topicId = null, CancellationToken ct = default)
    {
        var result = new BatchImportResult
        {
            Total = inboxItemIds.Count
        };

        foreach (var itemId in inboxItemIds)
        {
            var oneResult = new ImportOneResult { InboxItemId = itemId };
            try
            {
                var source = await ImportOneAsync(itemId, topicId, ct);
                oneResult.Success = true;
                oneResult.Source = source;
                result.Succeeded++;
            }
            catch (Exception ex)
            {
                oneResult.Success = false;
                oneResult.ErrorMessage = ex.Message;
                result.Failed++;
            }
            result.Results.Add(oneResult);
        }

        _logger.LogInformation("Batch import completed: {Succeeded}/{Total} succeeded, {Failed} failed",
            result.Succeeded, result.Total, result.Failed);
        return result;
    }

    // ===== Helpers =====

    /// <summary>
    /// Updates the inbox item to mark it as imported, writing back the source_id
    /// and imported_at timestamp via the dedicated SetInboxItemImportedAsync method.
    /// </summary>
    private async Task UpdateInboxItemImportedAsync(string inboxItemId, string sourceId, CancellationToken ct)
    {
        await _repo.SetInboxItemImportedAsync(inboxItemId, sourceId, ct);
    }

    /// <summary>
    /// Computes the SHA256 hash of a string and returns it as a lowercase hex string.
    /// </summary>
    private static string ComputeSha256(string input)
    {
        var bytes = Encoding.UTF8.GetBytes(input);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// Escapes a string for safe inclusion in a JSON string value.
    /// </summary>
    private static string EscapeJson(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        var sb = new StringBuilder(value);
        sb.Replace("\\", "\\\\");
        sb.Replace("\"", "\\\"");
        sb.Replace("\n", "\\n");
        sb.Replace("\r", "\\r");
        sb.Replace("\t", "\\t");
        return sb.ToString();
    }
}
