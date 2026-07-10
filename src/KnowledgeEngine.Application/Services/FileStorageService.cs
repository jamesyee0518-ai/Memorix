using KnowledgeEngine.Application.DTOs;
using KnowledgeEngine.Application.Exceptions;
using KnowledgeEngine.Application.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KnowledgeEngine.Application.Services;

public class FileStorageService
{
    private readonly IFileStorageFactory _storageFactory;
    private readonly IAppDbContext _db;
    private readonly ICurrentUserContext _currentUser;
    private readonly ILogger<FileStorageService> _logger;

    public FileStorageService(
        IFileStorageFactory storageFactory,
        IAppDbContext db,
        ICurrentUserContext currentUser,
        ILogger<FileStorageService> logger)
    {
        _storageFactory = storageFactory;
        _db = db;
        _currentUser = currentUser;
        _logger = logger;
    }

    public async Task<ApiResponse<FileUploadResult>> UploadPdfAsync(string fileName, string contentType, long fileSize, Stream stream, CancellationToken ct = default)
    {
        if (!_currentUser.IsAuthenticated || _currentUser.UserId == null)
        {
            throw new UnauthorizedException("User is not authenticated");
        }
        var userId = _currentUser.UserId.Value;

        if (!contentType.Equals("application/pdf", StringComparison.OrdinalIgnoreCase) &&
            !fileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            throw new ValidationException("file", "Only PDF files are allowed");
        }

        if (fileSize > 50 * 1024 * 1024)
        {
            throw new ValidationException("file", "File size must not exceed 50MB");
        }

        var fileId = Guid.NewGuid();
        var bucket = "knowledge-engine";
        var objectKey = $"users/{userId}/files/{fileId}/original.pdf";

        var storageProvider = await _storageFactory.GetProviderAsync(ct);
        await storageProvider.UploadFileAsync(bucket, objectKey, stream, contentType, fileSize, ct);

        var now = DateTime.UtcNow;
        var fileObject = new Domain.Entities.FileObject
        {
            Id = fileId,
            WorkspaceId = userId,
            Bucket = bucket,
            ObjectKey = objectKey,
            OriginalFilename = fileName,
            MimeType = contentType,
            SizeBytes = fileSize,
            StorageProvider = GetStorageProviderName(storageProvider),
            CreatedAt = now
        };
        _db.Files.Add(fileObject);
        await _db.SaveChangesAsync(ct);

        var result = new FileUploadResult
        {
            FileId = fileId,
            FileName = fileName,
            MimeType = contentType,
            FileSize = fileSize,
            FileHash = fileObject.Sha256 ?? string.Empty,
            Bucket = bucket,
            ObjectKey = objectKey
        };

        return ApiResponse<FileUploadResult>.Ok(result);
    }

    public async Task<ApiResponse<object>> GetDownloadUrlAsync(Guid fileId, CancellationToken ct = default)
    {
        if (!_currentUser.IsAuthenticated || _currentUser.UserId == null)
        {
            throw new UnauthorizedException("User is not authenticated");
        }
        var userId = _currentUser.UserId.Value;

        var fileObject = await _db.Files.FirstOrDefaultAsync(f => f.Id == fileId && f.WorkspaceId == userId, ct);
        if (fileObject == null)
        {
            throw new NotFoundException("File", fileId);
        }

        var storageProvider = await _storageFactory.GetProviderForWorkspaceAsync(userId.ToString(), ct);
        var url = await storageProvider.GetPresignedDownloadUrlAsync(fileObject.Bucket, fileObject.ObjectKey, 3600, ct);

        return ApiResponse<object>.Ok(new
        {
            file_id = fileObject.Id,
            file_name = fileObject.OriginalFilename,
            download_url = url,
            expires_in = 3600
        });
    }

    internal async Task<string> UploadFileInternalAsync(string bucket, string objectKey, Stream stream, string contentType, long fileSize, CancellationToken ct = default)
    {
        var storageProvider = await _storageFactory.GetProviderAsync(ct);
        await storageProvider.UploadFileAsync(bucket, objectKey, stream, contentType, fileSize, ct);
        return GetStorageProviderName(storageProvider);
    }

    private static string GetStorageProviderName(IFileStorageProvider storageProvider)
        => storageProvider.GetType().Name.Contains("Local", StringComparison.OrdinalIgnoreCase)
            ? "local_fs"
            : "minio";
}
