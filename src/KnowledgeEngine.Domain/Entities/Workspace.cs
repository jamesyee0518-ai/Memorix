using System.ComponentModel.DataAnnotations;

namespace KnowledgeEngine.Domain.Entities;

/// <summary>
/// Workspace is the top-level organizational unit.
/// It determines the runtime mode (local / cloud / hybrid),
/// storage provider, file provider, model provider, and job provider.
/// </summary>
public class Workspace
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// "local" | "cloud" | "hybrid"
    /// </summary>
    public string Mode { get; set; } = "local";

    /// <summary>
    /// "sqlite" | "postgres"
    /// </summary>
    public string StorageProvider { get; set; } = "postgres";

    /// <summary>
    /// "local_fs" | "s3" | "minio"
    /// </summary>
    public string FileProvider { get; set; } = "minio";

    /// <summary>
    /// "local_queue" | "redis"
    /// </summary>
    public string JobProvider { get; set; } = "redis";

    /// <summary>
    /// "ollama" | "lmstudio" | "openai" | "anthropic" | "custom"
    /// </summary>
    public string ModelProvider { get; set; } = "lmstudio";

    // Local mode paths
    public string? LocalDbPath { get; set; }
    public string? LocalVaultPath { get; set; }

    // Cloud mode config
    public string? CloudApiBaseUrl { get; set; }
    public string? CloudWorkspaceId { get; set; }

    // Sync / Inbox
    public bool SyncEnabled { get; set; } = false;
    public bool InboxEnabled { get; set; } = false;
    public string SyncMode { get; set; } = SyncModes.None;

    // Model config (JSON)
    public string? ModelConfig { get; set; }

    // Ownership
    public Guid? UserId { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
