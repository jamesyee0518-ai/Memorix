using KnowledgeEngine.Application.DTOs;

namespace KnowledgeEngine.Application.Interfaces;

/// <summary>
/// Unified repository abstraction for knowledge data (Phase 1 + Phase 2).
/// Local mode: LocalKnowledgeRepository → SQLite
/// Cloud mode: CloudKnowledgeRepository → HTTP API → PostgreSQL
/// </summary>
public interface IKnowledgeRepository
{
    // Topics
    Task<TopicDto> CreateTopicAsync(CreateTopicInput input, CancellationToken ct = default);
    Task<TopicDto?> GetTopicAsync(string id, CancellationToken ct = default);
    Task<List<TopicDto>> ListTopicsAsync(string workspaceId, CancellationToken ct = default);

    // Inbox Items (§12.1)
    Task<InboxItemDto> CreateInboxItemAsync(CreateInboxItemInput input, CancellationToken ct = default);
    Task<InboxItemDto?> GetInboxItemAsync(string id, CancellationToken ct = default);
    Task<List<InboxItemDto>> ListInboxItemsAsync(string workspaceId, string? status = null, string? inputType = null, string? topicId = null, int limit = 100, int offset = 0, CancellationToken ct = default);
    Task<List<InboxItemDto>> ListMobileCaptureItemsAsync(string workspaceId, string clientId, int limit = 50, CancellationToken ct = default);
    Task UpdateInboxItemAsync(string id, UpdateInboxItemInput input, CancellationToken ct = default);
    Task UpdateInboxItemStatusAsync(string id, string status, string? errorMessage = null, CancellationToken ct = default);
    Task SetInboxItemImportedAsync(string inboxItemId, string sourceId, CancellationToken ct = default);
    Task IncrementRetryCountAsync(string inboxItemId, CancellationToken ct = default);
    Task ArchiveInboxItemAsync(string id, CancellationToken ct = default);
    Task DeleteInboxItemAsync(string id, CancellationToken ct = default);
    Task<int> CountInboxItemsAsync(string workspaceId, string? status = null, CancellationToken ct = default);
    Task<bool> IsDuplicateUrlAsync(string workspaceId, string url, CancellationToken ct = default);
    Task<bool> IsDuplicateContentAsync(string workspaceId, string contentHash, CancellationToken ct = default);

    // Inbox Attachments (§7.2)
    Task<InboxAttachmentDto> CreateInboxAttachmentAsync(CreateInboxAttachmentInput input, CancellationToken ct = default);
    Task<List<InboxAttachmentDto>> ListInboxAttachmentsAsync(string inboxItemId, CancellationToken ct = default);

    // File Objects (§7.3)
    Task<FileObjectDto> CreateFileObjectAsync(CreateFileObjectInput input, CancellationToken ct = default);
    Task<FileObjectDto?> GetFileObjectAsync(string id, CancellationToken ct = default);

    // Import Jobs (§7.5)
    Task<ImportJobDto> CreateImportJobAsync(CreateImportJobInput input, CancellationToken ct = default);
    Task<ImportJobDto?> GetImportJobAsync(string id, CancellationToken ct = default);
    Task<List<ImportJobDto>> ListImportJobsAsync(string workspaceId, string? status = null, CancellationToken ct = default);
    Task UpdateImportJobAsync(string id, string status, string? sourceId = null, string? errorCode = null, string? errorMessage = null, CancellationToken ct = default);

    // Inbox Events (§7.6)
    Task CreateInboxEventAsync(string workspaceId, string inboxItemId, string eventType, string? payload = null, string? createdBy = null, CancellationToken ct = default);
    Task<List<InboxEventDto>> ListInboxEventsAsync(string inboxItemId, CancellationToken ct = default);

    // Sync Cursors (§7.7)
    Task<SyncCursorDto?> GetSyncCursorAsync(string workspaceId, string cursorType, CancellationToken ct = default);
    Task UpdateSyncCursorAsync(string workspaceId, string cursorType, string cursorValue, CancellationToken ct = default);

    // Cloud Inbox Sync Logs
    Task<CloudInboxSyncLogDto> CreateCloudInboxSyncLogAsync(CreateCloudInboxSyncLogInput input, CancellationToken ct = default);
    Task<List<CloudInboxSyncLogDto>> ListCloudInboxSyncLogsAsync(string workspaceId, int limit = 10, CancellationToken ct = default);

    // Mobile Devices
    Task<MobileDeviceDto> UpsertMobileDeviceAsync(UpsertMobileDeviceInput input, CancellationToken ct = default);
    Task<MobileDeviceDto?> GetMobileDeviceAsync(string workspaceId, string clientId, CancellationToken ct = default);
    Task<MobileDeviceDto?> GetMobileDeviceByRefreshTokenHashAsync(string refreshTokenHash, CancellationToken ct = default);
    Task<MobileDeviceDto> UpdateMobileDeviceRefreshTokenAsync(string workspaceId, string clientId, string refreshTokenHash, DateTime expiresAt, CancellationToken ct = default);
    Task DeactivateMobileDeviceAsync(string workspaceId, string clientId, CancellationToken ct = default);
    Task<List<MobileDeviceDto>> ListMobileDevicesAsync(string workspaceId, CancellationToken ct = default);

    // Push Notifications
    Task<PushNotificationDto> CreatePushNotificationAsync(CreatePushNotificationInput input, CancellationToken ct = default);
    Task<List<PushNotificationDto>> ListPushNotificationsAsync(string workspaceId, string? status = null, int limit = 50, CancellationToken ct = default);
    Task<List<PushNotificationDto>> ListPendingPushNotificationsAsync(int limit = 20, CancellationToken ct = default);
    Task MarkPushNotificationSentAsync(string id, string? providerResponse = null, CancellationToken ct = default);
    Task MarkPushNotificationFailedAsync(string id, string errorMessage, DateTime? nextAttemptAt, CancellationToken ct = default);

    // Sources
    Task<SourceDto> CreateSourceAsync(CreateSourceInput input, CancellationToken ct = default);
    Task<SourceDto?> GetSourceAsync(string id, CancellationToken ct = default);
    Task<List<SourceDto>> ListSourcesAsync(string workspaceId, string? topicId = null, CancellationToken ct = default);

    // Documents
    Task<DocumentDto> CreateDocumentAsync(CreateDocumentInput input, CancellationToken ct = default);
    Task<DocumentDto?> GetDocumentAsync(string id, CancellationToken ct = default);
    Task<List<DocumentDto>> ListDocumentsAsync(string workspaceId, string? topicId = null, CancellationToken ct = default);

    // Document Chunks (§12.1 saveChunks)
    Task SaveChunksAsync(string documentId, List<ChunkDto> chunks, CancellationToken ct = default);

    // Search (§12.1 searchDocuments)
    Task<List<SearchResultDto>> SearchDocumentsAsync(string workspaceId, string query, int limit = 20, CancellationToken ct = default);

    // Settings
    Task<string?> GetSettingAsync(string workspaceId, string key, CancellationToken ct = default);
    Task SetSettingAsync(string workspaceId, string key, string value, CancellationToken ct = default);

    // Tags (Phase 4)
    Task<TagDto> CreateTagAsync(CreateTagInput input, CancellationToken ct = default);
    Task<TagDto?> GetTagAsync(string id, CancellationToken ct = default);
    Task<List<TagDto>> ListTagsAsync(string workspaceId, string? tagType = null, int limit = 100, int offset = 0, CancellationToken ct = default);
    Task UpdateTagAsync(string id, UpdateTagInput input, CancellationToken ct = default);
    Task DeleteTagAsync(string id, CancellationToken ct = default);

    // Document Tags (Phase 4)
    Task<List<DocumentTagDto>> GetDocumentTagsAsync(string documentId, CancellationToken ct = default);
    Task<DocumentTagDto> AddDocumentTagAsync(string documentId, string tagName, string? tagType, string source, decimal? confidence, string? reason, CancellationToken ct = default);
    Task ConfirmDocumentTagAsync(string documentId, string tagId, string? confirmedBy, CancellationToken ct = default);
    Task DeleteDocumentTagAsync(string documentId, string tagId, CancellationToken ct = default);

    // Entities (Phase 4)
    Task<EntityDto> CreateEntityAsync(CreateEntityInput input, CancellationToken ct = default);
    Task<EntityDto?> GetEntityAsync(string id, CancellationToken ct = default);
    Task<List<EntityDto>> ListEntitiesAsync(string workspaceId, string? entityType = null, int limit = 100, int offset = 0, CancellationToken ct = default);
    Task DeleteEntityAsync(string id, CancellationToken ct = default);

    // Document Entities (Phase 4)
    Task<List<DocumentEntityDto>> GetDocumentEntitiesAsync(string documentId, CancellationToken ct = default);
    Task DeleteDocumentEntityAsync(string documentId, string entityId, CancellationToken ct = default);

    // Chunks (Phase 4)
    Task<List<ChunkDto>> GetDocumentChunksAsync(string documentId, CancellationToken ct = default);
    Task<ChunkDto?> GetChunkAsync(string chunkId, CancellationToken ct = default);
    Task DeleteChunksByDocumentAsync(string documentId, CancellationToken ct = default);

    // Embeddings (Phase 4)
    Task<ChunkEmbeddingDto?> GetChunkEmbeddingAsync(string chunkId, CancellationToken ct = default);
    Task SaveChunkEmbeddingAsync(SaveChunkEmbeddingInput input, CancellationToken ct = default);
    Task MarkEmbeddingsStaleAsync(string workspaceId, string? model = null, CancellationToken ct = default);

    // Vector Index State (Phase 4)
    Task<VectorIndexStateDto?> GetVectorIndexStateAsync(string workspaceId, CancellationToken ct = default);
    Task UpdateVectorIndexStateAsync(string workspaceId, UpdateVectorIndexStateInput input, CancellationToken ct = default);
}

// ===== DTOs =====

public class TopicDto
{
    public string Id { get; set; } = string.Empty;
    public string WorkspaceId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Domain { get; set; }
    public string Status { get; set; } = "active";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class CreateTopicInput
{
    public string WorkspaceId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Domain { get; set; }
}

public class SourceDto
{
    public string Id { get; set; } = string.Empty;
    public string WorkspaceId { get; set; } = string.Empty;
    public string? TopicId { get; set; }
    public string? InboxItemId { get; set; }
    public string SourceType { get; set; } = string.Empty;
    public string? Title { get; set; }
    public string? Url { get; set; }
    public string? Domain { get; set; }
    public string? Author { get; set; }
    public string? LocalFilePath { get; set; }
    public string? ContentHash { get; set; }
    public string Status { get; set; } = "pending";
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class CreateSourceInput
{
    public string WorkspaceId { get; set; } = string.Empty;
    public string? TopicId { get; set; }
    public string? InboxItemId { get; set; }
    public string SourceType { get; set; } = string.Empty;
    public string? Title { get; set; }
    public string? Url { get; set; }
    public string? Author { get; set; }
    public string? LocalFilePath { get; set; }
    public string? ContentHash { get; set; }
    public string? RawText { get; set; }
}

public class DocumentDto
{
    public string Id { get; set; } = string.Empty;
    public string WorkspaceId { get; set; } = string.Empty;
    public string? TopicId { get; set; }
    public string? SourceId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? ContentMarkdown { get; set; }
    public string? ContentText { get; set; }
    public string? Summary { get; set; }
    public string AiStatus { get; set; } = "pending";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class CreateDocumentInput
{
    public string WorkspaceId { get; set; } = string.Empty;
    public string? TopicId { get; set; }
    public string? SourceId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? ContentMarkdown { get; set; }
    public string? ContentText { get; set; }
}

public class InboxItemDto
{
    public string Id { get; set; } = string.Empty;
    public string WorkspaceId { get; set; } = string.Empty;
    public string? UserId { get; set; }
    public string? TopicId { get; set; }
    public string InputType { get; set; } = "text";
    public string ItemType { get; set; } = "text";
    public string? Title { get; set; }
    public string? ContentText { get; set; }
    public string? SourceUrl { get; set; }
    public string? FilePath { get; set; }
    public string Status { get; set; } = "pending";
    public string? SuggestedTopicId { get; set; }
    public string? SuggestedTitle { get; set; }
    public string? SuggestedTags { get; set; }
    public string CreatedFrom { get; set; } = "desktop";
    public string? OriginDeviceId { get; set; }
    public string? OriginClientVersion { get; set; }
    public string? SourceId { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public int RetryCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? ImportedAt { get; set; }
    public DateTime? ArchivedAt { get; set; }
    public List<InboxAttachmentDto> Attachments { get; set; } = new();
}

public class CreateInboxItemInput
{
    public string WorkspaceId { get; set; } = string.Empty;
    public string? UserId { get; set; }
    public string? TopicId { get; set; }
    public string InputType { get; set; } = "text";
    public string? Title { get; set; }
    public string? ContentText { get; set; }
    public string? SourceUrl { get; set; }
    public string? FilePath { get; set; }
    public string CreatedFrom { get; set; } = "desktop";
    public string? OriginDeviceId { get; set; }
    public string? OriginClientVersion { get; set; }
}

public class UpdateInboxItemInput
{
    public string? Title { get; set; }
    public string? ContentText { get; set; }
    public string? TopicId { get; set; }
    public string? SuggestedTopicId { get; set; }
    public string? SuggestedTitle { get; set; }
    public string? SuggestedTags { get; set; }
}

public class InboxAttachmentDto
{
    public string Id { get; set; } = string.Empty;
    public string WorkspaceId { get; set; } = string.Empty;
    public string InboxItemId { get; set; } = string.Empty;
    public string FileId { get; set; } = string.Empty;
    public string Role { get; set; } = "primary";
    public string Filename { get; set; } = string.Empty;
    public string MimeType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class CreateInboxAttachmentInput
{
    public string WorkspaceId { get; set; } = string.Empty;
    public string InboxItemId { get; set; } = string.Empty;
    public string FileId { get; set; } = string.Empty;
    public string Role { get; set; } = "primary";
    public string Filename { get; set; } = string.Empty;
    public string MimeType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
}

public class FileObjectDto
{
    public string Id { get; set; } = string.Empty;
    public string WorkspaceId { get; set; } = string.Empty;
    public string StorageProvider { get; set; } = "local_fs";
    public string? Bucket { get; set; }
    public string? ObjectKey { get; set; }
    public string? LocalPath { get; set; }
    public string OriginalFilename { get; set; } = string.Empty;
    public string MimeType { get; set; } = string.Empty;
    public string? Extension { get; set; }
    public long SizeBytes { get; set; }
    public string? Sha256 { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class CreateFileObjectInput
{
    public string WorkspaceId { get; set; } = string.Empty;
    public string StorageProvider { get; set; } = "local_fs";
    public string? Bucket { get; set; }
    public string? ObjectKey { get; set; }
    public string? LocalPath { get; set; }
    public string OriginalFilename { get; set; } = string.Empty;
    public string MimeType { get; set; } = string.Empty;
    public string? Extension { get; set; }
    public long SizeBytes { get; set; }
    public string? Sha256 { get; set; }
}

public class ImportJobDto
{
    public string Id { get; set; } = string.Empty;
    public string WorkspaceId { get; set; } = string.Empty;
    public string InboxItemId { get; set; } = string.Empty;
    public string? SourceId { get; set; }
    public string JobType { get; set; } = "text_import";
    public string Status { get; set; } = "queued";
    public int Attempt { get; set; }
    public int MaxAttempts { get; set; } = 3;
    public DateTime? StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class CreateImportJobInput
{
    public string WorkspaceId { get; set; } = string.Empty;
    public string InboxItemId { get; set; } = string.Empty;
    public string JobType { get; set; } = "text_import";
}

public class InboxEventDto
{
    public string Id { get; set; } = string.Empty;
    public string WorkspaceId { get; set; } = string.Empty;
    public string InboxItemId { get; set; } = string.Empty;
    public string EventType { get; set; } = "created";
    public string? EventPayload { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class SyncCursorDto
{
    public string Id { get; set; } = string.Empty;
    public string WorkspaceId { get; set; } = string.Empty;
    public string RemoteWorkspaceId { get; set; } = string.Empty;
    public string CursorType { get; set; } = "inbox";
    public string? CursorValue { get; set; }
    public DateTime? LastSyncedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class CloudInboxSyncLogDto
{
    public string Id { get; set; } = string.Empty;
    public string WorkspaceId { get; set; } = string.Empty;
    public string Direction { get; set; } = "pull";
    public string Status { get; set; } = "success";
    public string? CloudApiBaseUrl { get; set; }
    public string? CloudWorkspaceId { get; set; }
    public string Retention { get; set; } = "keep";
    public int PulledCount { get; set; }
    public int FailedCount { get; set; }
    public string? NextCursor { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime FinishedAt { get; set; }
    public long DurationMs { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class CreateCloudInboxSyncLogInput
{
    public string WorkspaceId { get; set; } = string.Empty;
    public string Direction { get; set; } = "pull";
    public string Status { get; set; } = "success";
    public string? CloudApiBaseUrl { get; set; }
    public string? CloudWorkspaceId { get; set; }
    public string Retention { get; set; } = "keep";
    public int PulledCount { get; set; }
    public int FailedCount { get; set; }
    public string? NextCursor { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime FinishedAt { get; set; }
}

public class MobileDeviceDto
{
    public string Id { get; set; } = string.Empty;
    public string WorkspaceId { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string? DeviceName { get; set; }
    public string? Platform { get; set; }
    public string? PushToken { get; set; }
    public DateTime? RefreshTokenExpiresAt { get; set; }
    public string Status { get; set; } = "active";
    public DateTime? LastSeenAt { get; set; }
    public DateTime BoundAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class UpsertMobileDeviceInput
{
    public string WorkspaceId { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string? DeviceName { get; set; }
    public string? Platform { get; set; }
    public string? PushToken { get; set; }
}

public class PushNotificationDto
{
    public string Id { get; set; } = string.Empty;
    public string WorkspaceId { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string PushToken { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string? DataJson { get; set; }
    public string Status { get; set; } = "pending";
    public int Attempt { get; set; }
    public int MaxAttempts { get; set; } = 3;
    public string? ProviderResponse { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime? NextAttemptAt { get; set; }
    public DateTime? SentAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class CreatePushNotificationInput
{
    public string WorkspaceId { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string PushToken { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string? DataJson { get; set; }
    public int MaxAttempts { get; set; } = 3;
}

public class ChunkDto
{
    public string Id { get; set; } = string.Empty;
    public string DocumentId { get; set; } = string.Empty;
    public int ChunkIndex { get; set; }
    public string? ChunkTitle { get; set; }
    public string Content { get; set; } = string.Empty;
    public int TokenCount { get; set; }
    public int CharCount { get; set; }
    // Phase 4 fields
    public string? ChunkUid { get; set; }
    public string? HeadingPath { get; set; }
    public int? SectionLevel { get; set; }
    public string? ContentHash { get; set; }
    public string? PrevChunkId { get; set; }
    public string? NextChunkId { get; set; }
    public int? PageStart { get; set; }
    public int? PageEnd { get; set; }
    public string IndexStatus { get; set; } = "pending";
}

public class SearchResultDto
{
    public string DocumentId { get; set; } = string.Empty;
    public string? ChunkId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? ContentSnippet { get; set; }
    public double Score { get; set; }
    public string? SourceUrl { get; set; }
}

// ===== Phase 4 DTOs =====

public class TagDto
{
    public string Id { get; set; } = string.Empty;
    public string WorkspaceId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string NormalizedName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string TagType { get; set; } = "custom";
    public string? Description { get; set; }
    public string? Color { get; set; }
    public string? Aliases { get; set; }
    public string Source { get; set; } = "user";
    public int UsageCount { get; set; }
    public bool IsSystem { get; set; }
    public bool IsArchived { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class CreateTagInput
{
    public string WorkspaceId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? NormalizedName { get; set; }
    public string? DisplayName { get; set; }
    public string TagType { get; set; } = "custom";
    public string? Description { get; set; }
    public string? Color { get; set; }
    public string? Aliases { get; set; }
    public string Source { get; set; } = "user";
}

public class UpdateTagInput
{
    public string? Name { get; set; }
    public string? DisplayName { get; set; }
    public string? TagType { get; set; }
    public string? Description { get; set; }
    public string? Color { get; set; }
    public string? Aliases { get; set; }
    public bool? IsArchived { get; set; }
}

public class DocumentTagDto
{
    public string Id { get; set; } = string.Empty;
    public string DocumentId { get; set; } = string.Empty;
    public string TagId { get; set; } = string.Empty;
    public string TagName { get; set; } = string.Empty;
    public string TagType { get; set; } = "custom";
    public string Source { get; set; } = "ai";
    public decimal? Confidence { get; set; }
    public string? Reason { get; set; }
    public bool IsConfirmed { get; set; }
    public string? ConfirmedBy { get; set; }
    public DateTime? ConfirmedAt { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class EntityDto
{
    public string Id { get; set; } = string.Empty;
    public string WorkspaceId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string NormalizedName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string EntityType { get; set; } = "other";
    public string? Aliases { get; set; }
    public string? Description { get; set; }
    public string? ExternalRef { get; set; }
    public string Source { get; set; } = "ai";
    public int UsageCount { get; set; }
    public bool IsVerified { get; set; }
    public bool IsArchived { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class CreateEntityInput
{
    public string WorkspaceId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? NormalizedName { get; set; }
    public string? DisplayName { get; set; }
    public string EntityType { get; set; } = "other";
    public string? Aliases { get; set; }
    public string? Description { get; set; }
    public string Source { get; set; } = "ai";
}

public class DocumentEntityDto
{
    public string Id { get; set; } = string.Empty;
    public string DocumentId { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public string EntityName { get; set; } = string.Empty;
    public string EntityType { get; set; } = "other";
    public int MentionCount { get; set; }
    public string? FirstMention { get; set; }
    public string? MentionExamples { get; set; }
    public decimal? Importance { get; set; }
    public string? Role { get; set; }
    public string? Sentiment { get; set; }
    public string Source { get; set; } = "ai";
    public decimal? Confidence { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class ChunkEmbeddingDto
{
    public string Id { get; set; } = string.Empty;
    public string WorkspaceId { get; set; } = string.Empty;
    public string ChunkId { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string? ModelVersion { get; set; }
    public int Dimension { get; set; }
    public string Status { get; set; } = "pending";
    public string? ErrorMessage { get; set; }
    public int RetryCount { get; set; }
    public string ChunkContentHash { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class SaveChunkEmbeddingInput
{
    public string WorkspaceId { get; set; } = string.Empty;
    public string ChunkId { get; set; } = string.Empty;
    public string Provider { get; set; } = "openai";
    public string Model { get; set; } = string.Empty;
    public string? ModelVersion { get; set; }
    public int Dimension { get; set; }
    public string? EmbeddingJson { get; set; }
    public string ChunkContentHash { get; set; } = string.Empty;
    public string Status { get; set; } = "done";
}

public class VectorIndexStateDto
{
    public string Id { get; set; } = string.Empty;
    public string WorkspaceId { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public int Dimension { get; set; }
    public string IndexBackend { get; set; } = "pgvector";
    public int TotalChunks { get; set; }
    public int IndexedChunks { get; set; }
    public int FailedChunks { get; set; }
    public int StaleChunks { get; set; }
    public string Status { get; set; } = "idle";
    public DateTime? LastRebuiltAt { get; set; }
    public string SchemaVersion { get; set; } = "v1";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class UpdateVectorIndexStateInput
{
    public string? Provider { get; set; }
    public string? Model { get; set; }
    public int? Dimension { get; set; }
    public string? IndexBackend { get; set; }
    public int? TotalChunks { get; set; }
    public int? IndexedChunks { get; set; }
    public int? FailedChunks { get; set; }
    public int? StaleChunks { get; set; }
    public string? Status { get; set; }
    public DateTime? LastRebuiltAt { get; set; }
}
