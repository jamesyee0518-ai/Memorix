using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KnowledgeEngine.Application.Services;

/// <summary>
/// Cloud inbox synchronization service (P1-1, §17.4).
///
/// Pulls inbox items from a remote cloud backend into the local knowledge
/// repository. This is the core of the "hybrid" mode: mobile devices capture
/// into the cloud, and the desktop client pulls those items down for local
/// processing.
///
/// The service is storage-agnostic: all persistence goes through
/// <see cref="IKnowledgeRepository"/> (routed to the local SQLite store by the
/// <c>RuntimeRepositoryFacade</c>), while all network calls go through
/// <see cref="IHttpClientFactory"/>.
/// </summary>
public class CloudInboxSyncService
{
    private readonly IKnowledgeRepository _repo;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IAppDbContext _db;
    private readonly ILogger<CloudInboxSyncService> _logger;

    // Cursor type used for inbox synchronization.
    private const string InboxCursorType = "inbox";

    // Prefix used to tag origin_device_id values that carry a remote id, so
    // that locally captured items (with a real device id) are never confused
    // with cloud-synced duplicates.
    private const string RemoteIdPrefix = "cloud:";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public CloudInboxSyncService(
        IKnowledgeRepository repo,
        IHttpClientFactory httpClientFactory,
        IAppDbContext db,
        ILogger<CloudInboxSyncService> logger)
    {
        _repo = repo;
        _httpClientFactory = httpClientFactory;
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// Result of a single pull operation.
    /// </summary>
    public class PullResult
    {
        public int PulledCount { get; set; }
        public int FailedCount { get; set; }
        public string? NextCursor { get; set; }
    }

    /// <summary>
    /// Pulls cloud inbox items into the local repository.
    ///
    /// Steps:
    /// 1. Read the current sync cursor for "inbox".
    /// 2. GET {cloudApiBaseUrl}/api/workspaces/{cloudWorkspaceId}/inbox/changes?cursor=...&amp;limit=100
    /// 3. Parse the response { items, nextCursor, hasMore }.
    /// 4. For each remote item: create a local inbox_item (skipping duplicates
    ///    identified by the remote id) and download any attachments.
    /// 5. Persist the new cursor.
    /// 6. Emit a "synced_from_cloud" event for every pulled item.
    /// 7. Return a <see cref="PullResult"/>.
    /// </summary>
    public async Task<PullResult> PullAsync(
        string workspaceId,
        string cloudApiBaseUrl,
        string cloudWorkspaceId,
        string authToken,
        string retention = "keep",
        CancellationToken ct = default)
    {
        var result = new PullResult();

        try
        {
            // 1) Resolve the current cursor.
            var cursor = await _repo.GetSyncCursorAsync(workspaceId, InboxCursorType, ct);
            var cursorValue = cursor?.CursorValue;

            _logger.LogInformation(
                "Pulling cloud inbox: workspace={WorkspaceId}, cloudWorkspace={CloudWorkspaceId}, cursor={Cursor}",
                workspaceId, cloudWorkspaceId, cursorValue ?? "(none)");

            // 2) Call the cloud changes endpoint.
            var client = CreateHttpClient(authToken);
            var url = BuildChangesUrl(cloudApiBaseUrl, cloudWorkspaceId, cursorValue, limit: 100);
            var response = await client.GetAsync(url, ct);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                response.Dispose();
                var legacyUrl = BuildLegacyChangesUrl(
                    cloudApiBaseUrl, cursorValue, limit: 100);
                _logger.LogWarning(
                    "Workspace-scoped Cloud Inbox changes route was not found; falling back to legacy route {LegacyUrl}",
                    legacyUrl);
                response = await client.GetAsync(legacyUrl, ct);
            }
            using (response)
            {
            response.EnsureSuccessStatusCode();

            // 3) Parse the response.
            var pullResponse = await ReadProtocolPayloadAsync<CloudPullResponseDto>(
                response.Content, ct);
            if (pullResponse == null)
            {
                _logger.LogWarning("Cloud inbox pull returned empty body for workspace {WorkspaceId}", workspaceId);
                return result;
            }
            await ProcessPullResponseAsync(
                pullResponse,
                workspaceId,
                cloudApiBaseUrl,
                cloudWorkspaceId,
                authToken,
                retention,
                client,
                result,
                ct);
            }

            _logger.LogInformation(
                "Cloud inbox pull complete: workspace={WorkspaceId}, pulled={Pulled}, failed={Failed}, nextCursor={NextCursor}",
                workspaceId, result.PulledCount, result.FailedCount, result.NextCursor ?? "(none)");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Cloud inbox pull failed for workspace {WorkspaceId}: {Message}", workspaceId, ex.Message);
            throw;
        }

        return result;
    }

    private async Task ProcessPullResponseAsync(
        CloudPullResponseDto pullResponse,
        string workspaceId,
        string cloudApiBaseUrl,
        string cloudWorkspaceId,
        string authToken,
        string retention,
        HttpClient client,
        PullResult result,
        CancellationToken ct)
    {
        if (!Guid.TryParse(workspaceId, out var localWorkspaceId))
            {
                throw new InvalidOperationException($"Invalid local workspace ID: {workspaceId}");
            }

            // 5) Process each remote item.
            foreach (var remote in pullResponse.Items)
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(remote.Id))
                    {
                        throw new InvalidOperationException("Cloud inbox item is missing its stable ID.");
                    }

                    var staging = await _db.SyncInboxStaging.FirstOrDefaultAsync(x =>
                        x.WorkspaceId == localWorkspaceId &&
                        x.CloudInboxItemId == remote.Id, ct);
                    if (staging?.Status == "imported")
                    {
                        _logger.LogDebug("Skipping duplicate cloud inbox item: remoteId={RemoteId}", remote.Id);
                        continue;
                    }

                    if (staging?.Status == "local_imported")
                    {
                        await AcknowledgeAsync(
                            client,
                            cloudApiBaseUrl,
                            cloudWorkspaceId,
                            remote.Id,
                            workspaceId,
                            staging.LocalInboxItemId,
                            retention,
                            ct);
                        staging.Status = "imported";
                        staging.ErrorMessage = null;
                        staging.UpdatedAt = DateTime.UtcNow;
                        await _db.SaveChangesAsync(ct);
                        continue;
                    }

                    var now = DateTime.UtcNow;
                    var isNewStaging = staging == null;
                    staging ??= new SyncInboxStaging
                    {
                        Id = Guid.CreateVersion7(),
                        WorkspaceId = localWorkspaceId,
                        CloudInboxItemId = remote.Id,
                        DiscoveredAt = now
                    };
                    if (isNewStaging)
                    {
                        _db.SyncInboxStaging.Add(staging);
                    }
                    staging.RemoteMetadataJson = JsonSerializer.Serialize(remote);
                    staging.Status = "importing";
                    staging.ErrorMessage = null;
                    staging.UpdatedAt = now;
                    await _db.SaveChangesAsync(ct);

                    var created = await CreateLocalInboxItemAsync(workspaceId, remote, ct);

                    // Download attachments (if any) into the local vault.
                    if (remote.Attachments != null && remote.Attachments.Count > 0)
                    {
                        foreach (var att in remote.Attachments)
                        {
                            if (string.IsNullOrEmpty(att.FileId)) continue;
                            try
                            {
                                // The vault path is derived from the workspace; attachments land in the "inbox" subdir.
                                var vaultPath = await ResolveLocalVaultPathAsync(workspaceId, ct);
                                if (!string.IsNullOrEmpty(vaultPath))
                                {
                                    await DownloadAttachmentAsync(cloudApiBaseUrl, att.FileId, authToken, vaultPath, ct);
                                }
                            }
                            catch (Exception attEx)
                            {
                                _logger.LogWarning(attEx,
                                    "Failed to download attachment {FileId} for remote item {RemoteId}",
                                    att.FileId, remote.Id);
                            }
                        }
                    }

                    // 6) Emit a "synced_from_cloud" event.
                    var payload = JsonSerializer.Serialize(new
                    {
                        remoteId = remote.Id,
                        inputType = remote.InputType ?? remote.ItemType,
                        title = remote.Title
                    });
                    await _repo.CreateInboxEventAsync(workspaceId, created.Id, "synced_from_cloud", payload, null, ct);

                    staging.LocalInboxItemId = Guid.TryParse(created.Id, out var localInboxItemId)
                        ? localInboxItemId
                        : null;
                    staging.Status = "local_imported";
                    staging.ImportedAt = DateTime.UtcNow;
                    staging.UpdatedAt = DateTime.UtcNow;
                    await _db.SaveChangesAsync(ct);

                    await AcknowledgeAsync(
                        client,
                        cloudApiBaseUrl,
                        cloudWorkspaceId,
                        remote.Id,
                        workspaceId,
                        staging.LocalInboxItemId,
                        retention,
                        ct);
                    staging.Status = "imported";
                    staging.UpdatedAt = DateTime.UtcNow;
                    await _db.SaveChangesAsync(ct);
                    result.PulledCount++;
                }
                catch (Exception itemEx)
                {
                    result.FailedCount++;
                    if (!string.IsNullOrWhiteSpace(remote.Id))
                    {
                        var failedStaging = await _db.SyncInboxStaging.FirstOrDefaultAsync(x =>
                            x.WorkspaceId == localWorkspaceId &&
                            x.CloudInboxItemId == remote.Id, ct);
                        if (failedStaging != null)
                        {
                            if (failedStaging.Status != "local_imported")
                            {
                                failedStaging.Status = "failed";
                            }
                            failedStaging.ErrorMessage = itemEx.Message;
                            failedStaging.UpdatedAt = DateTime.UtcNow;
                            await _db.SaveChangesAsync(CancellationToken.None);
                        }
                    }
                    _logger.LogError(itemEx,
                        "Failed to sync cloud inbox item remoteId={RemoteId}", remote.Id);
                }
            }

            // 7) A cursor is a batch commit point. Never advance past a failed
            // item; the server batch must remain replayable until all items are
            // either imported or recognized as already imported.
            if (result.FailedCount == 0 && !string.IsNullOrEmpty(pullResponse.NextCursor))
            {
                await _repo.UpdateSyncCursorAsync(workspaceId, InboxCursorType, pullResponse.NextCursor, ct);
                result.NextCursor = pullResponse.NextCursor;
            }
            else if (result.FailedCount > 0)
            {
                _logger.LogWarning(
                    "Cloud inbox cursor was not advanced because {FailedCount} item(s) failed",
                    result.FailedCount);
            }

    }

    /// <summary>
    /// Downloads a single attachment from the cloud into the local vault.
    ///
    /// Steps:
    /// 1. GET {cloudApiBaseUrl}/api/files/{fileId}/download-url with auth.
    /// 2. Parse { downloadUrl, expiresIn }.
    /// 3. Download the file from downloadUrl to localVaultPath.
    /// 4. Return the local file path.
    /// </summary>
    public async Task<string> DownloadAttachmentAsync(
        string cloudApiBaseUrl,
        string fileId,
        string authToken,
        string localVaultPath,
        CancellationToken ct = default)
    {
        // 1) Request a presigned download URL from the cloud.
        var client = CreateHttpClient(authToken);
        var url = $"{cloudApiBaseUrl.TrimEnd('/')}/api/files/{Uri.EscapeDataString(fileId)}/download-url";
        var response = await client.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        // 2) Parse the download-url response.
        var dlResponse = await response.Content.ReadFromJsonAsync<DownloadUrlResponseDto>(JsonOptions, ct);
        if (dlResponse == null || string.IsNullOrEmpty(dlResponse.DownloadUrl))
        {
            throw new InvalidOperationException($"Cloud did not return a download URL for file {fileId}");
        }

        // 3) Download the actual file bytes to the local vault.
        var inboxDir = Path.Combine(localVaultPath, "inbox");
        Directory.CreateDirectory(inboxDir);

        var localFilePath = Path.Combine(inboxDir, $"{fileId}");
        using var downloadClient = new HttpClient();
        using var fileStream = await downloadClient.GetStreamAsync(dlResponse.DownloadUrl, ct);
        await using var fs = File.Create(localFilePath);
        await fileStream.CopyToAsync(fs, ct);

        _logger.LogInformation(
            "Downloaded cloud attachment: fileId={FileId} -> {LocalPath}", fileId, localFilePath);

        // 4) Return the local file path.
        return localFilePath;
    }

    /// <summary>
    /// Updates the sync cursor for a given cursor type.
    /// Thin wrapper around the repository so callers don't need to inject the
    /// repository directly just to advance a cursor.
    /// </summary>
    public async Task UpdateCursorAsync(
        string workspaceId,
        string cursorType,
        string cursorValue,
        CancellationToken ct = default)
    {
        await _repo.UpdateSyncCursorAsync(workspaceId, cursorType, cursorValue, ct);
        _logger.LogDebug(
            "Updated sync cursor: workspace={WorkspaceId}, type={CursorType}, value={CursorValue}",
            workspaceId, cursorType, cursorValue);
    }

    // ===== Private helpers =====

    /// <summary>
    /// Creates a local inbox item from a remote item DTO.
    /// The remote id is stored in origin_device_id with the "cloud:" prefix
    /// so duplicate pulls can be detected.
    /// </summary>
    private async Task<InboxItemDto> CreateLocalInboxItemAsync(
        string workspaceId,
        RemoteInboxItemDto remote,
        CancellationToken ct)
    {
        var originDeviceId = string.IsNullOrEmpty(remote.Id)
            ? null
            : $"{RemoteIdPrefix}{remote.Id}";

        var inputType = !string.IsNullOrEmpty(remote.InputType)
            ? remote.InputType
            : (!string.IsNullOrEmpty(remote.ItemType) ? remote.ItemType : "text");

        var item = await _repo.CreateInboxItemAsync(new CreateInboxItemInput
        {
            WorkspaceId = workspaceId,
            TopicId = remote.TopicId,
            InputType = inputType,
            Title = remote.Title,
            ContentText = remote.ContentText ?? remote.Content,
            SourceUrl = remote.SourceUrl ?? remote.Url,
            FilePath = remote.FilePath,
            CreatedFrom = "cloud_sync",
            OriginDeviceId = originDeviceId
        }, ct);

        return item;
    }

    /// <summary>
    /// Resolves the local vault path for a workspace from workspace settings.
    /// Falls back to a default directory under the user profile when unset.
    /// </summary>
    private async Task<string?> ResolveLocalVaultPathAsync(string workspaceId, CancellationToken ct)
    {
        var vault = await _repo.GetSettingAsync(workspaceId, "local_vault_path", ct);
        if (!string.IsNullOrEmpty(vault)) return vault;

        // Default vault location.
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".knowledge-engine",
            $"workspace-{workspaceId}");
    }

    /// <summary>
    /// Builds the cloud changes endpoint URL with cursor-based pagination.
    /// </summary>
    private static string BuildChangesUrl(string cloudApiBaseUrl, string cloudWorkspaceId, string? cursor, int limit)
    {
        var baseUrl = cloudApiBaseUrl.TrimEnd('/');
        var url = $"{baseUrl}/api/workspaces/{Uri.EscapeDataString(cloudWorkspaceId)}/inbox/changes?limit={limit}";
        if (!string.IsNullOrEmpty(cursor))
        {
            url += $"&cursor={Uri.EscapeDataString(cursor)}";
        }
        return url;
    }

    private static string BuildLegacyChangesUrl(
        string cloudApiBaseUrl,
        string? cursor,
        int limit)
    {
        var url = $"{cloudApiBaseUrl.TrimEnd('/')}/api/inbox/changes?limit={limit}";
        if (!string.IsNullOrEmpty(cursor))
        {
            url += $"&cursor={Uri.EscapeDataString(cursor)}";
        }
        return url;
    }

    /// <summary>
    /// Creates an HttpClient with the Bearer auth header pre-configured.
    /// </summary>
    private HttpClient CreateHttpClient(string authToken)
    {
        var client = _httpClientFactory.CreateClient();
        if (!string.IsNullOrEmpty(authToken))
        {
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", authToken);
        }
        return client;
    }

    private static async Task AcknowledgeAsync(
        HttpClient client,
        string cloudApiBaseUrl,
        string cloudWorkspaceId,
        string remoteItemId,
        string localWorkspaceId,
        Guid? localInboxItemId,
        string retention,
        CancellationToken ct)
    {
        var normalizedRetention = retention is "deleteOriginal" or "deleteAll"
            ? retention
            : "keep";
        var baseUrl = cloudApiBaseUrl.TrimEnd('/');
        var url =
            $"{baseUrl}/api/workspaces/{Uri.EscapeDataString(cloudWorkspaceId)}/inbox/items/{Uri.EscapeDataString(remoteItemId)}/ack";
        var idempotencyKey =
            $"memorix:{localWorkspaceId}:{remoteItemId}:{normalizedRetention}";
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(new
            {
                cloudWorkspaceId,
                localWorkspaceId,
                localInboxItemId,
                result = "imported",
                retention = normalizedRetention,
                idempotencyKey
            })
        };
        request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);
        var response = await client.SendAsync(request, ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            response.Dispose();
            var legacyUrl =
                $"{baseUrl}/v1/inbox/items/{Uri.EscapeDataString(remoteItemId)}/ack";
            using var legacyRequest = new HttpRequestMessage(HttpMethod.Post, legacyUrl)
            {
                Content = JsonContent.Create(new
                {
                    cloudWorkspaceId,
                    localWorkspaceId,
                    localInboxItemId,
                    result = "imported",
                    retention = normalizedRetention,
                    idempotencyKey
                })
            };
            legacyRequest.Headers.TryAddWithoutValidation(
                "Idempotency-Key", idempotencyKey);
            response = await client.SendAsync(legacyRequest, ct);
        }
        using (response)
        {
            response.EnsureSuccessStatusCode();
            var acknowledgement = await ReadProtocolPayloadAsync<AcknowledgementResponseDto>(
                response.Content, ct);
        if (acknowledgement?.Acknowledged != true)
        {
            throw new InvalidOperationException(
                $"Cloud Inbox item {remoteItemId} was not acknowledged by the cloud service.");
        }
        if (normalizedRetention != "keep" &&
            !string.Equals(acknowledgement.RetentionApplied, normalizedRetention,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Cloud Inbox item {remoteItemId} did not confirm retention policy {normalizedRetention}.");
        }
        }
    }

    private static async Task<T?> ReadProtocolPayloadAsync<T>(
        HttpContent content,
        CancellationToken ct)
    {
        await using var stream = await content.ReadAsStreamAsync(ct);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var root = document.RootElement;
        if (root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty("success", out var success))
        {
            if (success.ValueKind == JsonValueKind.False)
            {
                var message = root.TryGetProperty("error", out var error) &&
                    error.TryGetProperty("message", out var errorMessage)
                        ? errorMessage.GetString()
                        : "Cloud service returned an unsuccessful response.";
                throw new InvalidOperationException(message);
            }
            if (!root.TryGetProperty("data", out var data) ||
                data.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                return default;
            }
            return data.Deserialize<T>(JsonOptions);
        }
        return root.Deserialize<T>(JsonOptions);
    }

    // ===== Response DTOs =====

    /// <summary>
    /// Response payload from GET /api/workspaces/{id}/inbox/changes.
    /// </summary>
    private class CloudPullResponseDto
    {
        [JsonPropertyName("items")]
        public List<RemoteInboxItemDto> Items { get; set; } = new();

        [JsonPropertyName("nextCursor")]
        public string? NextCursor { get; set; }

        [JsonPropertyName("hasMore")]
        public bool HasMore { get; set; }
    }

    /// <summary>
    /// A remote inbox item as returned by the cloud changes endpoint.
    /// Field aliases (e.g. content vs contentText) tolerate both naming
    /// conventions used across the codebase.
    /// </summary>
    private class RemoteInboxItemDto
    {
        public string? Id { get; set; }
        public string? InputType { get; set; }
        public string? ItemType { get; set; }
        public string? Title { get; set; }
        public string? ContentText { get; set; }
        public string? Content { get; set; }
        public string? SourceUrl { get; set; }
        public string? Url { get; set; }
        public string? FilePath { get; set; }
        public string? TopicId { get; set; }
        public List<RemoteAttachmentDto>? Attachments { get; set; }
    }

    private class RemoteAttachmentDto
    {
        public string? FileId { get; set; }
        public string? Filename { get; set; }
        public string? MimeType { get; set; }
        public long SizeBytes { get; set; }
        public string? Role { get; set; }
    }

    /// <summary>
    /// Response payload from GET /api/files/{id}/download-url.
    /// </summary>
    private class DownloadUrlResponseDto
    {
        [JsonPropertyName("downloadUrl")]
        public string? DownloadUrl { get; set; }

        [JsonPropertyName("expiresIn")]
        public int ExpiresIn { get; set; }
    }

    private sealed class AcknowledgementResponseDto
    {
        [JsonPropertyName("acknowledged")]
        public bool Acknowledged { get; set; }

        [JsonPropertyName("retentionApplied")]
        public string? RetentionApplied { get; set; }
    }
}
