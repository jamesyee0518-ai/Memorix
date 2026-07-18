namespace KnowledgeEngine.Domain.Entities;

public class ExportFile
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }

    public Guid? ExportJobId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string FileType { get; set; } = string.Empty;
    public string? MimeType { get; set; }
    public long? FileSize { get; set; }

    public string StorageProvider { get; set; } = "minio";
    public string StoragePath { get; set; } = string.Empty;
    public string? DownloadUrl { get; set; }

    public string? Checksum { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
}
