using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using KnowledgeEngine.Application.Interfaces;
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
        ILogger<CloudInboxSyncService> logger)
    {
        _repo = repo;
        _httpClientFactory = httpClientFactory;
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
            response.EnsureSuccessStatusCode();

            // 3) Parse the response.
            var pullResponse = await response.Content.ReadFromJsonAsync<CloudPullResponseDto>(JsonOptions, ct);
            if (pullResponse == null)
            {
                _logger.LogWarning("Cloud inbox pull returned empty body for workspace {WorkspaceId}", workspaceId);
                return result;
            }

            // 4) Build a set of already-synced remote ids for duplicate detection.
            //    The remote id is stored on the local item's origin_device_id
            //    field with the "cloud:" prefix (see RemoteIdPrefix).
            var existingItems = await _repo.ListInboxItemsAsync(
                workspaceId, null, null, null, limit: 500, offset: 0, ct);
            var syncedRemoteIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var existing in existingItems)
            {
                if (!string.IsNullOrEmpty(existing.OriginDeviceId) &&
                    existing.OriginDeviceId.StartsWith(RemoteIdPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    syncedRemoteIds.Add(existing.OriginDeviceId[RemoteIdPrefix.Length..]);
                }
            }

            // 5) Process each remote item.
            foreach (var remote in pullResponse.Items)
            {
                try
                {
                    // Skip duplicates: the remote id is already present locally.
                    if (!string.IsNullOrEmpty(remote.Id) &&
                        syncedRemoteIds.Contains(remote.Id))
                    {
                        _logger.LogDebug("Skipping duplicate cloud inbox item: remoteId={RemoteId}", remote.Id);
                        continue;
                    }

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

                    result.PulledCount++;
                }
                catch (Exception itemEx)
                {
                    result.FailedCount++;
                    _logger.LogError(itemEx,
                        "Failed to sync cloud inbox item remoteId={RemoteId}", remote.Id);
                }
            }

            // 7) Persist the new cursor (only when the server provided one).
            if (!string.IsNullOrEmpty(pullResponse.NextCursor))
            {
                await _repo.UpdateSyncCursorAsync(workspaceId, InboxCursorType, pullResponse.NextCursor, ct);
                result.NextCursor = pullResponse.NextCursor;
            }

            _logger.LogInformation(
                "Cloud inbox pull complete: workspace={WorkspaceId}, pulled={Pulled}, failed={Failed}, nextCursor={NextCursor}",
                workspaceId, result.PulledCount, result.FailedCount, result.NextCursor ?? "(none)");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Cloud inbox pull failed for workspace {WorkspaceId}: {Message}", workspaceId, ex.Message);
            // Re-throw so callers can react; the partial result is still populated.
            throw;
        }

        return result;
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
}
