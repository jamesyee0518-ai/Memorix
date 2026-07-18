using System.Text.Json.Serialization;

namespace KnowledgeEngine.Application.DTOs;

public class WorkspaceDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Mode { get; set; } = string.Empty;
    public string StorageProvider { get; set; } = string.Empty;
    public string FileProvider { get; set; } = string.Empty;
    public string JobProvider { get; set; } = string.Empty;
    public string ModelProvider { get; set; } = string.Empty;
    public string? LocalDbPath { get; set; }
    public string? LocalVaultPath { get; set; }
    public string? CloudApiBaseUrl { get; set; }
    public string? CloudWorkspaceId { get; set; }
    public bool SyncEnabled { get; set; }
    public bool InboxEnabled { get; set; }
    public string SyncMode { get; set; } = "none";
    public string? ModelConfig { get; set; }
    public Guid? UserId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class CreateWorkspaceDto
{
    public string Name { get; set; } = string.Empty;
    public string Mode { get; set; } = "cloud";
    public string StorageProvider { get; set; } = "postgres";
    public string FileProvider { get; set; } = "minio";
    public string JobProvider { get; set; } = "redis";
    public string ModelProvider { get; set; } = "lmstudio";
    public string? LocalDbPath { get; set; }
    public string? LocalVaultPath { get; set; }
    public string? CloudApiBaseUrl { get; set; }
    public string? CloudWorkspaceId { get; set; }
    public bool SyncEnabled { get; set; } = false;
    public bool InboxEnabled { get; set; } = false;
    public string SyncMode { get; set; } = "none";
    public string? ModelConfig { get; set; }
}

public class InitLocalWorkspaceDto
{
    public string Name { get; set; } = string.Empty;
    public string VaultPath { get; set; } = string.Empty;
    public string? ModelProvider { get; set; } = "lmstudio";
    public string? ModelConfig { get; set; }
}

public class UpdateWorkspaceDto
{
    public string? Name { get; set; }
    public string? ModelProvider { get; set; }
    public string? ModelConfig { get; set; }
    public bool? SyncEnabled { get; set; }
    public bool? InboxEnabled { get; set; }
    public string? SyncMode { get; set; }
    public string? LocalVaultPath { get; set; }
    public string? CloudApiBaseUrl { get; set; }
    public string? CloudWorkspaceId { get; set; }
}

public class UpdateModelSettingsDto
{
    public string Provider { get; set; } = "lmstudio";
    public string? BaseUrl { get; set; }
    public string? ApiKey { get; set; }
    public string? ChatModel { get; set; }
    public string? EmbeddingModel { get; set; }
}

// InboxItemDto is defined in IKnowledgeRepository.cs (KnowledgeEngine.Application.Interfaces).
// It uses string-based IDs for cross-runtime compatibility (local SQLite + cloud API).

public class CreateInboxItemDto
{
    public string InputType { get; set; } = "text";
    public string? Title { get; set; }
    public string? ContentText { get; set; }
    public string? SourceUrl { get; set; }
    public Guid? TopicId { get; set; }
    public string? CreatedFrom { get; set; } = "desktop";
    public string? OriginDeviceId { get; set; }
    public string? OriginClientVersion { get; set; }
}

public class UpdateInboxItemDto
{
    public string? Title { get; set; }
    public string? ContentText { get; set; }
    public Guid? TopicId { get; set; }
    public Guid? SuggestedTopicId { get; set; }
    public string? SuggestedTitle { get; set; }
    public string? SuggestedTags { get; set; }
}

public class BatchImportInboxDto
{
    public List<Guid> InboxItemIds { get; set; } = new();
    public Guid? TopicId { get; set; }
}

public class BatchArchiveInboxDto
{
    public List<Guid> InboxItemIds { get; set; } = new();
}
