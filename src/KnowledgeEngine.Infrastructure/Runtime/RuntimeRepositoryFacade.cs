using KnowledgeEngine.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace KnowledgeEngine.Infrastructure.Runtime;

/// <summary>
/// Facade that implements IKnowledgeRepository by delegating to the
/// correct implementation (Local or Cloud) via RuntimeRouter.
///
/// This is registered as IKnowledgeRepository in DI so that business
/// services can inject IKnowledgeRepository and get the correct
/// implementation for the current workspace mode automatically.
/// </summary>
public class RuntimeRepositoryFacade : IKnowledgeRepository
{
    private readonly RuntimeRouter _router;
    private readonly ILogger<RuntimeRepositoryFacade> _logger;

    public RuntimeRepositoryFacade(RuntimeRouter router, ILogger<RuntimeRepositoryFacade> logger)
    {
        _router = router;
        _logger = logger;
    }

    private async Task<IKnowledgeRepository> GetRepoAsync(CancellationToken ct)
    {
        return await _router.GetRepositoryAsync(ct);
    }

    // ===== Topics =====

    public async Task<TopicDto> CreateTopicAsync(CreateTopicInput input, CancellationToken ct = default)
        => await (await GetRepoAsync(ct)).CreateTopicAsync(input, ct);

    public async Task<TopicDto?> GetTopicAsync(string id, CancellationToken ct = default)
        => await (await GetRepoAsync(ct)).GetTopicAsync(id, ct);

    public async Task<List<TopicDto>> ListTopicsAsync(string workspaceId, CancellationToken ct = default)
        => await (await GetRepoAsync(ct)).ListTopicsAsync(workspaceId, ct);

    // ===== Inbox Items =====

    public async Task<InboxItemDto> CreateInboxItemAsync(CreateInboxItemInput input, CancellationToken ct = default)
        => await (await GetRepoAsync(ct)).CreateInboxItemAsync(input, ct);

    public async Task<InboxItemDto?> GetInboxItemAsync(string id, CancellationToken ct = default)
        => await (await GetRepoAsync(ct)).GetInboxItemAsync(id, ct);

    public async Task<List<InboxItemDto>> ListInboxItemsAsync(
        string workspaceId, string? status = null, string? inputType = null,
        string? topicId = null, int limit = 100, int offset = 0, CancellationToken ct = default)
        => await (await GetRepoAsync(ct)).ListInboxItemsAsync(workspaceId, status, inputType, topicId, limit, offset, ct);

    public async Task<List<InboxItemDto>> ListMobileCaptureItemsAsync(string workspaceId, string clientId, int limit = 50, CancellationToken ct = default)
        => await (await GetRepoAsync(ct)).ListMobileCaptureItemsAsync(workspaceId, clientId, limit, ct);

    public async Task UpdateInboxItemAsync(string id, UpdateInboxItemInput input, CancellationToken ct = default)
        => await (await GetRepoAsync(ct)).UpdateInboxItemAsync(id, input, ct);

    public async Task UpdateInboxItemStatusAsync(string id, string status, string? errorMessage = null, CancellationToken ct = default)
        => await (await GetRepoAsync(ct)).UpdateInboxItemStatusAsync(id, status, errorMessage, ct);

    public async Task SetInboxItemImportedAsync(string inboxItemId, string sourceId, CancellationToken ct = default)
        => await (await GetRepoAsync(ct)).SetInboxItemImportedAsync(inboxItemId, sourceId, ct);

    public async Task IncrementRetryCountAsync(string inboxItemId, CancellationToken ct = default)
        => await (await GetRepoAsync(ct)).IncrementRetryCountAsync(inboxItemId, ct);

    public async Task ArchiveInboxItemAsync(string id, CancellationToken ct = default)
        => await (await GetRepoAsync(ct)).ArchiveInboxItemAsync(id, ct);

    public async Task DeleteInboxItemAsync(string id, CancellationToken ct = default)
        => await (await GetRepoAsync(ct)).DeleteInboxItemAsync(id, ct);

    public async Task<int> CountInboxItemsAsync(string workspaceId, string? status = null, CancellationToken ct = default)
        => await (await GetRepoAsync(ct)).CountInboxItemsAsync(workspaceId, status, ct);

    public async Task<bool> IsDuplicateUrlAsync(string workspaceId, string url, CancellationToken ct = default)
        => await (await GetRepoAsync(ct)).IsDuplicateUrlAsync(workspaceId, url, ct);

    public async Task<bool> IsDuplicateContentAsync(string workspaceId, string contentHash, CancellationToken ct = default)
        => await (await GetRepoAsync(ct)).IsDuplicateContentAsync(workspaceId, contentHash, ct);

    // ===== Inbox Attachments =====

    public async Task<InboxAttachmentDto> CreateInboxAttachmentAsync(CreateInboxAttachmentInput input, CancellationToken ct = default)
        => await (await GetRepoAsync(ct)).CreateInboxAttachmentAsync(input, ct);

    public async Task<List<InboxAttachmentDto>> ListInboxAttachmentsAsync(string inboxItemId, CancellationToken ct = default)
        => await (await GetRepoAsync(ct)).ListInboxAttachmentsAsync(inboxItemId, ct);

    // ===== File Objects =====

    public async Task<FileObjectDto> CreateFileObjectAsync(CreateFileObjectInput input, CancellationToken ct = default)
        => await (await GetRepoAsync(ct)).CreateFileObjectAsync(input, ct);

    public async Task<FileObjectDto?> GetFileObjectAsync(string id, CancellationToken ct = default)
        => await (await GetRepoAsync(ct)).GetFileObjectAsync(id, ct);

    // ===== Import Jobs =====

    public async Task<ImportJobDto> CreateImportJobAsync(CreateImportJobInput input, CancellationToken ct = default)
        => await (await GetRepoAsync(ct)).CreateImportJobAsync(input, ct);

    public async Task<ImportJobDto?> GetImportJobAsync(string id, CancellationToken ct = default)
        => await (await GetRepoAsync(ct)).GetImportJobAsync(id, ct);

    public async Task<List<ImportJobDto>> ListImportJobsAsync(string workspaceId, string? status = null, CancellationToken ct = default)
        => await (await GetRepoAsync(ct)).ListImportJobsAsync(workspaceId, status, ct);

    public async Task UpdateImportJobAsync(string id, string status, string? sourceId = null, string? errorCode = null, string? errorMessage = null, CancellationToken ct = default)
        => await (await GetRepoAsync(ct)).UpdateImportJobAsync(id, status, sourceId, errorCode, errorMessage, ct);

    // ===== Inbox Events =====

    public async Task CreateInboxEventAsync(string workspaceId, string inboxItemId, string eventType, string? payload = null, string? createdBy = null, CancellationToken ct = default)
        => await (await GetRepoAsync(ct)).CreateInboxEventAsync(workspaceId, inboxItemId, eventType, payload, createdBy, ct);

    public async Task<List<InboxEventDto>> ListInboxEventsAsync(string inboxItemId, CancellationToken ct = default)
        => await (await GetRepoAsync(ct)).ListInboxEventsAsync(inboxItemId, ct);

    // ===== Sync Cursors =====

    public async Task<SyncCursorDto?> GetSyncCursorAsync(string workspaceId, string cursorType, CancellationToken ct = default)
        => await (await GetRepoAsync(ct)).GetSyncCursorAsync(workspaceId, cursorType, ct);

    public async Task UpdateSyncCursorAsync(string workspaceId, string cursorType, string cursorValue, CancellationToken ct = default)
        => await (await GetRepoAsync(ct)).UpdateSyncCursorAsync(workspaceId, cursorType, cursorValue, ct);

    // ===== Cloud Inbox Sync Logs =====

    public async Task<CloudInboxSyncLogDto> CreateCloudInboxSyncLogAsync(CreateCloudInboxSyncLogInput input, CancellationToken ct = default)
        => await (await GetRepoAsync(ct)).CreateCloudInboxSyncLogAsync(input, ct);

    public async Task<List<CloudInboxSyncLogDto>> ListCloudInboxSyncLogsAsync(string workspaceId, int limit = 10, CancellationToken ct = default)
        => await (await GetRepoAsync(ct)).ListCloudInboxSyncLogsAsync(workspaceId, limit, ct);

    // ===== Mobile Devices =====

    public async Task<MobileDeviceDto> UpsertMobileDeviceAsync(UpsertMobileDeviceInput input, CancellationToken ct = default)
        => await (await GetRepoAsync(ct)).UpsertMobileDeviceAsync(input, ct);

    public async Task<MobileDeviceDto?> GetMobileDeviceAsync(string workspaceId, string clientId, CancellationToken ct = default)
        => await (await GetRepoAsync(ct)).GetMobileDeviceAsync(workspaceId, clientId, ct);

    public async Task<MobileDeviceDto?> GetMobileDeviceByRefreshTokenHashAsync(string refreshTokenHash, CancellationToken ct = default)
        => await (await GetRepoAsync(ct)).GetMobileDeviceByRefreshTokenHashAsync(refreshTokenHash, ct);

    public async Task<MobileDeviceDto> UpdateMobileDeviceRefreshTokenAsync(string workspaceId, string clientId, string refreshTokenHash, DateTime expiresAt, CancellationToken ct = default)
        => await (await GetRepoAsync(ct)).UpdateMobileDeviceRefreshTokenAsync(workspaceId, clientId, refreshTokenHash, expiresAt, ct);

    public async Task DeactivateMobileDeviceAsync(string workspaceId, string clientId, CancellationToken ct = default)
        => await (await GetRepoAsync(ct)).DeactivateMobileDeviceAsync(workspaceId, clientId, ct);

    public async Task<List<MobileDeviceDto>> ListMobileDevicesAsync(string workspaceId, CancellationToken ct = default)
        => await (await GetRepoAsync(ct)).ListMobileDevicesAsync(workspaceId, ct);

    // ===== Push Notifications =====

    public async Task<PushNotificationDto> CreatePushNotificationAsync(CreatePushNotificationInput input, CancellationToken ct = default)
        => await (await GetRepoAsync(ct)).CreatePushNotificationAsync(input, ct);

    public async Task<List<PushNotificationDto>> ListPushNotificationsAsync(string workspaceId, string? status = null, int limit = 50, CancellationToken ct = default)
        => await (await GetRepoAsync(ct)).ListPushNotificationsAsync(workspaceId, status, limit, ct);

    public async Task<List<PushNotificationDto>> ListPendingPushNotificationsAsync(int limit = 20, CancellationToken ct = default)
        => await (await GetRepoAsync(ct)).ListPendingPushNotificationsAsync(limit, ct);

    public async Task MarkPushNotificationSentAsync(string id, string? providerResponse = null, CancellationToken ct = default)
        => await (await GetRepoAsync(ct)).MarkPushNotificationSentAsync(id, providerResponse, ct);

    public async Task MarkPushNotificationFailedAsync(string id, string errorMessage, DateTime? nextAttemptAt, CancellationToken ct = default)
        => await (await GetRepoAsync(ct)).MarkPushNotificationFailedAsync(id, errorMessage, nextAttemptAt, ct);

    // ===== Sources =====

    public async Task<SourceDto> CreateSourceAsync(CreateSourceInput input, CancellationToken ct = default)
        => await (await GetRepoAsync(ct)).CreateSourceAsync(input, ct);

    public async Task<SourceDto?> GetSourceAsync(string id, CancellationToken ct = default)
        => await (await GetRepoAsync(ct)).GetSourceAsync(id, ct);

    public async Task<List<SourceDto>> ListSourcesAsync(string workspaceId, string? topicId = null, CancellationToken ct = default)
        => await (await GetRepoAsync(ct)).ListSourcesAsync(workspaceId, topicId, ct);

    // ===== Documents =====

    public async Task<DocumentDto> CreateDocumentAsync(CreateDocumentInput input, CancellationToken ct = default)
        => await (await GetRepoAsync(ct)).CreateDocumentAsync(input, ct);

    public async Task<DocumentDto?> GetDocumentAsync(string id, CancellationToken ct = default)
        => await (await GetRepoAsync(ct)).GetDocumentAsync(id, ct);

    public async Task<List<DocumentDto>> ListDocumentsAsync(string workspaceId, string? topicId = null, CancellationToken ct = default)
        => await (await GetRepoAsync(ct)).ListDocumentsAsync(workspaceId, topicId, ct);

    // ===== Document Chunks =====

    public async Task SaveChunksAsync(string documentId, List<ChunkDto> chunks, CancellationToken ct = default)
        => await (await GetRepoAsync(ct)).SaveChunksAsync(documentId, chunks, ct);

    // ===== Search =====

    public async Task<List<SearchResultDto>> SearchDocumentsAsync(string workspaceId, string query, int limit = 20, CancellationToken ct = default)
        => await (await GetRepoAsync(ct)).SearchDocumentsAsync(workspaceId, query, limit, ct);

    // ===== Settings =====

    public async Task<string?> GetSettingAsync(string workspaceId, string key, CancellationToken ct = default)
        => await (await GetRepoAsync(ct)).GetSettingAsync(workspaceId, key, ct);

    public async Task SetSettingAsync(string workspaceId, string key, string value, CancellationToken ct = default)
        => await (await GetRepoAsync(ct)).SetSettingAsync(workspaceId, key, value, ct);

    // ===== Tags (Phase 4) =====

    public async Task<TagDto> CreateTagAsync(CreateTagInput input, CancellationToken ct = default)
        => await (await GetRepoAsync(ct)).CreateTagAsync(input, ct);

    public async Task<TagDto?> GetTagAsync(string id, CancellationToken ct = default)
        => await (await GetRepoAsync(ct)).GetTagAsync(id, ct);

    public async Task<List<TagDto>> ListTagsAsync(string workspaceId, string? tagType = null, int limit = 100, int offset = 0, CancellationToken ct = default)
        => await (await GetRepoAsync(ct)).ListTagsAsync(workspaceId, tagType, limit, offset, ct);

    public async Task UpdateTagAsync(string id, UpdateTagInput input, CancellationToken ct = default)
        => await (await GetRepoAsync(ct)).UpdateTagAsync(id, input, ct);

    public async Task DeleteTagAsync(string id, CancellationToken ct = default)
        => await (await GetRepoAsync(ct)).DeleteTagAsync(id, ct);

    // ===== Document Tags (Phase 4) =====

    public async Task<List<DocumentTagDto>> GetDocumentTagsAsync(string documentId, CancellationToken ct = default)
        => await (await GetRepoAsync(ct)).GetDocumentTagsAsync(documentId, ct);

    public async Task<DocumentTagDto> AddDocumentTagAsync(string documentId, string tagName, string? tagType, string source, decimal? confidence, string? reason, CancellationToken ct = default)
        => await (await GetRepoAsync(ct)).AddDocumentTagAsync(documentId, tagName, tagType, source, confidence, reason, ct);

    public async Task ConfirmDocumentTagAsync(string documentId, string tagId, string? confirmedBy, CancellationToken ct = default)
        => await (await GetRepoAsync(ct)).ConfirmDocumentTagAsync(documentId, tagId, confirmedBy, ct);

    public async Task DeleteDocumentTagAsync(string documentId, string tagId, CancellationToken ct = default)
        => await (await GetRepoAsync(ct)).DeleteDocumentTagAsync(documentId, tagId, ct);

    // ===== Entities (Phase 4) =====

    public async Task<EntityDto> CreateEntityAsync(CreateEntityInput input, CancellationToken ct = default)
        => await (await GetRepoAsync(ct)).CreateEntityAsync(input, ct);

    public async Task<EntityDto?> GetEntityAsync(string id, CancellationToken ct = default)
        => await (await GetRepoAsync(ct)).GetEntityAsync(id, ct);

    public async Task<List<EntityDto>> ListEntitiesAsync(string workspaceId, string? entityType = null, int limit = 100, int offset = 0, CancellationToken ct = default)
        => await (await GetRepoAsync(ct)).ListEntitiesAsync(workspaceId, entityType, limit, offset, ct);

    public async Task DeleteEntityAsync(string id, CancellationToken ct = default)
        => await (await GetRepoAsync(ct)).DeleteEntityAsync(id, ct);

    // ===== Document Entities (Phase 4) =====

    public async Task<List<DocumentEntityDto>> GetDocumentEntitiesAsync(string documentId, CancellationToken ct = default)
        => await (await GetRepoAsync(ct)).GetDocumentEntitiesAsync(documentId, ct);

    public async Task DeleteDocumentEntityAsync(string documentId, string entityId, CancellationToken ct = default)
        => await (await GetRepoAsync(ct)).DeleteDocumentEntityAsync(documentId, entityId, ct);

    // ===== Chunks (Phase 4) =====

    public async Task<List<ChunkDto>> GetDocumentChunksAsync(string documentId, CancellationToken ct = default)
        => await (await GetRepoAsync(ct)).GetDocumentChunksAsync(documentId, ct);

    public async Task<ChunkDto?> GetChunkAsync(string chunkId, CancellationToken ct = default)
        => await (await GetRepoAsync(ct)).GetChunkAsync(chunkId, ct);

    public async Task DeleteChunksByDocumentAsync(string documentId, CancellationToken ct = default)
        => await (await GetRepoAsync(ct)).DeleteChunksByDocumentAsync(documentId, ct);

    // ===== Embeddings (Phase 4) =====

    public async Task<ChunkEmbeddingDto?> GetChunkEmbeddingAsync(string chunkId, CancellationToken ct = default)
        => await (await GetRepoAsync(ct)).GetChunkEmbeddingAsync(chunkId, ct);

    public async Task SaveChunkEmbeddingAsync(SaveChunkEmbeddingInput input, CancellationToken ct = default)
        => await (await GetRepoAsync(ct)).SaveChunkEmbeddingAsync(input, ct);

    public async Task MarkEmbeddingsStaleAsync(string workspaceId, string? model = null, CancellationToken ct = default)
        => await (await GetRepoAsync(ct)).MarkEmbeddingsStaleAsync(workspaceId, model, ct);

    // ===== Vector Index State (Phase 4) =====

    public async Task<VectorIndexStateDto?> GetVectorIndexStateAsync(string workspaceId, CancellationToken ct = default)
        => await (await GetRepoAsync(ct)).GetVectorIndexStateAsync(workspaceId, ct);

    public async Task UpdateVectorIndexStateAsync(string workspaceId, UpdateVectorIndexStateInput input, CancellationToken ct = default)
        => await (await GetRepoAsync(ct)).UpdateVectorIndexStateAsync(workspaceId, input, ct);
}
