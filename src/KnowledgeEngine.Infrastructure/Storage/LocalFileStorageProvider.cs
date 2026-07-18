using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Application.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KnowledgeEngine.Infrastructure.Storage;

/// <summary>
/// Local filesystem implementation of IFileStorageProvider.
/// Stores files in a local Vault directory structure.
/// Used when workspace.fileProvider == "local_fs".
/// </summary>
public class LocalFileStorageProvider : IFileStorageProvider
{
    private readonly string _vaultRoot;
    private readonly ILogger<LocalFileStorageProvider> _logger;

    public LocalFileStorageProvider(IOptions<LocalFileStorageSettings> settings, ILogger<LocalFileStorageProvider> logger)
        : this(settings.Value.VaultRoot, logger)
    {
    }

    public LocalFileStorageProvider(string vaultRoot, ILogger<LocalFileStorageProvider> logger)
    {
        _vaultRoot = Path.GetFullPath(vaultRoot);
        _logger = logger;
        EnsureVaultStructure();
    }

    private void EnsureVaultStructure()
    {
        var dirs = new[] { "inbox", "sources", "documents", "attachments", "exports", "reports", "snapshots" };
        foreach (var dir in dirs)
        {
            var path = Path.Combine(_vaultRoot, dir);
            Directory.CreateDirectory(path);
        }
        _logger.LogInformation("LocalFileStorageProvider initialized. Vault root: {VaultRoot}", _vaultRoot);
    }

    public async Task UploadFileAsync(string bucket, string objectKey, Stream stream, string contentType, long? fileSize = null, CancellationToken cancellationToken = default)
    {
        // bucket is used as a subdirectory, objectKey as the file path
        var fullPath = GetFullPath(bucket, objectKey);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        using var fileStream = new FileStream(fullPath, FileMode.Create, FileAccess.Write);
        await stream.CopyToAsync(fileStream, cancellationToken);

        _logger.LogDebug("Saved file: {Path}", fullPath);
    }

    public Task<string> GetPresignedDownloadUrlAsync(string bucket, string objectKey, int expiry, CancellationToken cancellationToken = default)
    {
        // For local FS, return a file:// URL or a relative API path
        var fullPath = GetFullPath(bucket, objectKey);
        return Task.FromResult($"file://{fullPath}");
    }

    public Task EnsureBucketExistsAsync(string bucket, CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(_vaultRoot, bucket);
        Directory.CreateDirectory(path);
        return Task.CompletedTask;
    }

    public async Task<Stream> DownloadFileAsync(string bucket, string objectKey, CancellationToken cancellationToken = default)
    {
        var fullPath = GetFullPath(bucket, objectKey);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"File not found: {fullPath}");
        }

        var memoryStream = new MemoryStream();
        using var fileStream = new FileStream(fullPath, FileMode.Open, FileAccess.Read);
        await fileStream.CopyToAsync(memoryStream, cancellationToken);
        memoryStream.Position = 0;
        return memoryStream;
    }

    public Task DeleteFileAsync(string bucket, string objectKey, CancellationToken cancellationToken = default)
    {
        var fullPath = GetFullPath(bucket, objectKey);
        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
        }
        return Task.CompletedTask;
    }

    public Task<string?> GetFilePathAsync(string bucket, string objectKey, CancellationToken cancellationToken = default)
    {
        var fullPath = GetFullPath(bucket, objectKey);
        return Task.FromResult<string?>(File.Exists(fullPath) ? fullPath : null);
    }

    private string GetFullPath(string bucket, string objectKey)
    {
        // Sanitize path components to prevent directory traversal
        var safeBucket = bucket.Replace("..", "").TrimStart('/');
        var safeKey = objectKey.Replace("..", "").TrimStart('/');
        return Path.Combine(_vaultRoot, safeBucket, safeKey);
    }
}
