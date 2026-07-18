using System.Security.Cryptography;
using System.Text;
using KnowledgeEngine.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace KnowledgeEngine.Application.Services;

/// <summary>
/// Import service for creating inbox items from various input types (§17.1).
/// Handles text, URL, file, and mixed content imports.
/// Each creation method also logs a "created" inbox event.
/// </summary>
public class ImportService
{
    private readonly IKnowledgeRepository _repo;
    private readonly IFileStorageFactory _fileStorageFactory;
    private readonly ILogger<ImportService> _logger;

    public ImportService(
        IKnowledgeRepository repo,
        IFileStorageFactory fileStorageFactory,
        ILogger<ImportService> logger)
    {
        _repo = repo;
        _fileStorageFactory = fileStorageFactory;
        _logger = logger;
    }

    /// <summary>
    /// Creates an inbox item from text content.
    /// </summary>
    public async Task<InboxItemDto> CreateTextAsync(
        string workspaceId,
        string? title,
        string content,
        string? topicId = null,
        string createdFrom = "desktop",
        string? originDeviceId = null,
        CancellationToken ct = default)
    {
        var item = await _repo.CreateInboxItemAsync(new CreateInboxItemInput
        {
            WorkspaceId = workspaceId,
            TopicId = topicId,
            InputType = "text",
            Title = title,
            ContentText = content,
            CreatedFrom = createdFrom,
            OriginDeviceId = originDeviceId
        }, ct);

        await _repo.CreateInboxEventAsync(workspaceId, item.Id, "created",
            $"{{\"inputType\":\"text\",\"title\":\"{EscapeJson(title)}\"}}", null, ct);

        _logger.LogInformation("Created text inbox item: {Id}", item.Id);
        return item;
    }

    /// <summary>
    /// Creates an inbox item from a URL.
    /// </summary>
    public async Task<InboxItemDto> CreateUrlAsync(
        string workspaceId,
        string url,
        string? title = null,
        string? topicId = null,
        string createdFrom = "desktop",
        string? originDeviceId = null,
        CancellationToken ct = default)
    {
        var item = await _repo.CreateInboxItemAsync(new CreateInboxItemInput
        {
            WorkspaceId = workspaceId,
            TopicId = topicId,
            InputType = "url",
            Title = title,
            SourceUrl = url,
            CreatedFrom = createdFrom,
            OriginDeviceId = originDeviceId
        }, ct);

        await _repo.CreateInboxEventAsync(workspaceId, item.Id, "created",
            $"{{\"inputType\":\"url\",\"url\":\"{EscapeJson(url)}\"}}", null, ct);

        _logger.LogInformation("Created URL inbox item: {Id} (url={Url})", item.Id, url);
        return item;
    }

    /// <summary>
    /// Creates an inbox item from a file upload.
    /// Saves the file via IFileStorageProvider, creates a file_object record,
    /// then creates the inbox item with a specific inputType:
    /// "image", "audio", "pdf", or "file".
    /// </summary>
    public async Task<InboxItemDto> CreateFileAsync(
        string workspaceId,
        string fileName,
        string mimeType,
        Stream stream,
        string? topicId = null,
        string createdFrom = "desktop",
        string? originDeviceId = null,
        CancellationToken ct = default)
    {
        // 1) Compute SHA256 hash and read stream to memory
        string sha256;
        byte[] fileBytes;
        using (var sha = SHA256.Create())
        {
            fileBytes = await ReadStreamAsync(stream, ct);
            sha256 = Convert.ToHexString(sha.ComputeHash(fileBytes)).ToLowerInvariant();
        }

        var fileSize = fileBytes.Length;
        var extension = Path.GetExtension(fileName);
        var inputType = DetectFileInputType(fileName, mimeType);

        // 2) Save file via storage provider
        var provider = await _fileStorageFactory.GetProviderForWorkspaceAsync(workspaceId, ct);
        var bucket = "inbox";
        var objectKey = $"{sha256}/{fileName}";

        await provider.EnsureBucketExistsAsync(bucket, ct);
        using var uploadStream = new MemoryStream(fileBytes);
        await provider.UploadFileAsync(bucket, objectKey, uploadStream, mimeType, fileSize, ct);

        // 3) Get the local file path (for local mode)
        var localPath = await provider.GetFilePathAsync(bucket, objectKey, ct);

        // 4) Create file_object record
        var fileObject = await _repo.CreateFileObjectAsync(new CreateFileObjectInput
        {
            WorkspaceId = workspaceId,
            StorageProvider = "local_fs",
            Bucket = bucket,
            ObjectKey = objectKey,
            LocalPath = localPath,
            OriginalFilename = fileName,
            MimeType = mimeType,
            Extension = extension,
            SizeBytes = fileSize,
            Sha256 = sha256
        }, ct);

        // 5) Create inbox item
        var item = await _repo.CreateInboxItemAsync(new CreateInboxItemInput
        {
            WorkspaceId = workspaceId,
            TopicId = topicId,
            InputType = inputType,
            Title = fileName,
            FilePath = localPath,
            CreatedFrom = createdFrom,
            OriginDeviceId = originDeviceId
        }, ct);

        // 6) Create inbox attachment linking the file to the inbox item
        await _repo.CreateInboxAttachmentAsync(new CreateInboxAttachmentInput
        {
            WorkspaceId = workspaceId,
            InboxItemId = item.Id,
            FileId = fileObject.Id,
            Role = "primary",
            Filename = fileName,
            MimeType = mimeType,
            SizeBytes = fileSize
        }, ct);

        // 7) Log creation event
        await _repo.CreateInboxEventAsync(workspaceId, item.Id, "created",
            $"{{\"inputType\":\"{inputType}\",\"fileName\":\"{EscapeJson(fileName)}\",\"sha256\":\"{sha256}\"}}", null, ct);

        _logger.LogInformation("Created file inbox item: {Id} (file={FileName}, size={Size})", item.Id, fileName, fileSize);
        return item;
    }

    /// <summary>
    /// Creates an inbox item with mixed content (multiple input types).
    /// </summary>
    public async Task<InboxItemDto> CreateMixedAsync(
        string workspaceId,
        string? title,
        string? content,
        string? url,
        string? filePath,
        string? topicId = null,
        string createdFrom = "desktop",
        CancellationToken ct = default)
    {
        var item = await _repo.CreateInboxItemAsync(new CreateInboxItemInput
        {
            WorkspaceId = workspaceId,
            TopicId = topicId,
            InputType = "mixed",
            Title = title,
            ContentText = content,
            SourceUrl = url,
            FilePath = filePath,
            CreatedFrom = createdFrom
        }, ct);

        await _repo.CreateInboxEventAsync(workspaceId, item.Id, "created",
            $"{{\"inputType\":\"mixed\",\"title\":\"{EscapeJson(title)}\"}}", null, ct);

        _logger.LogInformation("Created mixed inbox item: {Id}", item.Id);
        return item;
    }

    // ===== Helpers =====

    /// <summary>
    /// Reads a stream fully into a byte array.
    /// </summary>
    private static async Task<byte[]> ReadStreamAsync(Stream stream, CancellationToken ct)
    {
        if (stream is MemoryStream ms)
            return ms.ToArray();

        using var memoryStream = new MemoryStream();
        await stream.CopyToAsync(memoryStream, ct);
        return memoryStream.ToArray();
    }

    /// <summary>
    /// Escapes a string for safe inclusion in a JSON string value.
    /// </summary>
    private static string EscapeJson(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        var sb = new StringBuilder(value);
        sb.Replace("\\", "\\\\");
        sb.Replace("\"", "\\\"");
        sb.Replace("\n", "\\n");
        sb.Replace("\r", "\\r");
        sb.Replace("\t", "\\t");
        return sb.ToString();
    }

    private static string DetectFileInputType(string fileName, string mimeType)
    {
        if (mimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            return "image";
        if (mimeType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase))
            return "audio";
        if (mimeType.Equals("application/pdf", StringComparison.OrdinalIgnoreCase))
            return "pdf";

        var extension = Path.GetExtension(fileName).TrimStart('.').ToLowerInvariant();
        return extension switch
        {
            "jpg" or "jpeg" or "png" or "gif" or "webp" or "heic" or "heif" => "image",
            "wav" or "mp3" or "m4a" or "aac" or "flac" or "ogg" => "audio",
            "pdf" => "pdf",
            _ => "file"
        };
    }
}
