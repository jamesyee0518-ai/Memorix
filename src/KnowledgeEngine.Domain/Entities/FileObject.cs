namespace KnowledgeEngine.Domain.Entities;

/// <summary>
/// File object entity matching design doc §6.3.
/// Represents a stored file in the object storage (MinIO / local FS).
/// </summary>
public class FileObject
{
    public Guid Id { get; set; }

    /// <summary>
    /// Workspace (user) that owns this file. Replaces the old UserId field
    /// to align with the dual-mode workspace concept (§6.3).
    /// </summary>
    public Guid WorkspaceId { get; set; }

    public string Bucket { get; set; } = string.Empty;
    public string ObjectKey { get; set; } = string.Empty;

    /// <summary>
    /// Local filesystem path when StorageProvider is "local_fs".
    /// </summary>
    public string? LocalPath { get; set; }

    /// <summary>
    /// Original filename as uploaded by the user (§6.3).
    /// </summary>
    public string? OriginalFilename { get; set; }

    public string? MimeType { get; set; }

    /// <summary>
    /// File extension without the dot, e.g. "pdf", "md" (§6.3).
    /// </summary>
    public string? Extension { get; set; }

    /// <summary>
    /// File size in bytes (§6.3).
    /// </summary>
    public long SizeBytes { get; set; }

    /// <summary>
    /// SHA-256 hash of the file content (§6.3).
    /// </summary>
    public string? Sha256 { get; set; }

    /// <summary>
    /// "minio" | "local_fs" (§6.3)
    /// </summary>
    public string StorageProvider { get; set; } = "minio";

    public DateTime CreatedAt { get; set; }
}
