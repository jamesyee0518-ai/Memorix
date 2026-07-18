namespace KnowledgeEngine.Application.Interfaces;

/// <summary>
/// File storage abstraction.
/// Local mode: LocalFileStorageProvider → local filesystem (Vault)
/// Cloud mode: MinioStorageProvider → MinIO / S3
/// </summary>
public interface IFileStorageProvider
{
    /// <summary>Save/upload a file.</summary>
    Task UploadFileAsync(string bucket, string objectKey, Stream stream, string contentType, long? fileSize = null, CancellationToken cancellationToken = default);

    /// <summary>Get a presigned download URL (cloud) or file:// path (local).</summary>
    Task<string> GetPresignedDownloadUrlAsync(string bucket, string objectKey, int expiry, CancellationToken cancellationToken = default);

    /// <summary>Ensure the bucket/container exists.</summary>
    Task EnsureBucketExistsAsync(string bucket, CancellationToken cancellationToken = default);

    /// <summary>Read/download a file as a stream.</summary>
    Task<Stream> DownloadFileAsync(string bucket, string objectKey, CancellationToken cancellationToken = default);

    /// <summary>Delete a file. (§13.1)</summary>
    Task DeleteFileAsync(string bucket, string objectKey, CancellationToken cancellationToken = default);

    /// <summary>Get the local file path for a stored file (local mode only, returns null in cloud mode). (§13.1)</summary>
    Task<string?> GetFilePathAsync(string bucket, string objectKey, CancellationToken cancellationToken = default);
}
