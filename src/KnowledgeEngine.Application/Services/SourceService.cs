using System.Security.Cryptography;
using System.Text;
using KnowledgeEngine.Application.DTOs;
using KnowledgeEngine.Application.Exceptions;
using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Application.Mapping;
using KnowledgeEngine.Application.Validators;
using KnowledgeEngine.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KnowledgeEngine.Application.Services;

public class SourceService
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUserContext _currentUser;
    private readonly FileStorageService _fileStorageService;
    private readonly ILogger<SourceService> _logger;

    public SourceService(
        IAppDbContext db,
        ICurrentUserContext currentUser,
        FileStorageService fileStorageService,
        ILogger<SourceService> logger)
    {
        _db = db;
        _currentUser = currentUser;
        _fileStorageService = fileStorageService;
        _logger = logger;
    }

    public async Task<ApiResponse<SourceResponse>> ImportUrlAsync(ImportUrlRequest request, CancellationToken ct = default)
    {
        var userId = RequireUserId();

        var validator = new ImportUrlValidator();
        var validationResult = await validator.ValidateAsync(request, ct);
        if (!validationResult.IsValid)
        {
            throw new ValidationException(validationResult.ToDictionary());
        }

        await EnsureTopicExistsAsync(request.TopicId, userId, ct);

        var url = request.Url.Trim();
        var domain = ExtractDomain(url);

        var duplicate = await _db.Sources.FirstOrDefaultAsync(
            s => s.UserId == userId && s.TopicId == request.TopicId && s.Url == url && s.Status != "archived", ct);
        if (duplicate != null)
        {
            throw new DuplicateException("This URL has already been imported into this topic");
        }

        var now = DateTime.UtcNow;
        var source = new Source
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TopicId = request.TopicId,
            SourceType = "url",
            Title = request.Title,
            Url = url,
            Domain = domain,
            ImportedAt = now,
            Status = "queued",
            CreatedAt = now,
            UpdatedAt = now
        };

        _db.Sources.Add(source);

        var job = new IngestJob
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            SourceId = source.Id,
            JobType = "fetch_url",
            Status = "pending",
            CreatedAt = now
        };
        _db.IngestJobs.Add(job);

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("URL source imported: {SourceId} by {UserId}", source.Id, userId);
        return ApiResponse<SourceResponse>.Ok(Mapper.ToSourceResponse(source));
    }

    public async Task<ApiResponse<SourceResponse>> ImportTextAsync(ImportTextRequest request, CancellationToken ct = default)
    {
        var userId = RequireUserId();

        var validator = new ImportTextValidator();
        var validationResult = await validator.ValidateAsync(request, ct);
        if (!validationResult.IsValid)
        {
            throw new ValidationException(validationResult.ToDictionary());
        }

        await EnsureTopicExistsAsync(request.TopicId, userId, ct);

        var content = request.Content;
        var contentHash = ComputeSha256(content);

        var duplicate = await _db.Sources.FirstOrDefaultAsync(
            s => s.UserId == userId && s.TopicId == request.TopicId && s.ContentHash == contentHash && s.Status != "archived", ct);
        if (duplicate != null)
        {
            throw new DuplicateException("This text content has already been imported into this topic");
        }

        var now = DateTime.UtcNow;
        var source = new Source
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TopicId = request.TopicId,
            SourceType = "text",
            Title = request.Title.Trim(),
            RawText = content,
            ContentHash = contentHash,
            ImportedAt = now,
            Status = "queued",
            CreatedAt = now,
            UpdatedAt = now
        };

        _db.Sources.Add(source);

        var job = new IngestJob
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            SourceId = source.Id,
            JobType = "parse_text",
            Status = "pending",
            CreatedAt = now
        };
        _db.IngestJobs.Add(job);

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Text source imported: {SourceId} by {UserId}", source.Id, userId);
        return ApiResponse<SourceResponse>.Ok(Mapper.ToSourceResponse(source));
    }

    public async Task<ApiResponse<SourceResponse>> ImportFileAsync(Guid topicId, string fileName, string contentType, long fileSize, Stream fileStream, string? title = null, CancellationToken ct = default)
    {
        var userId = RequireUserId();
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        var sourceType = extension switch
        {
            ".pdf" => "pdf",
            ".md" or ".markdown" => "markdown",
            ".txt" => "text_file",
            ".doc" or ".docx" => "word",
            ".xls" or ".xlsx" => "spreadsheet",
            ".csv" => "csv",
            _ => null
        };

        if (sourceType == null)
        {
            throw new ValidationException("file", "Supported formats: PDF, Markdown, text, Word, Excel, and CSV");
        }

        if (fileSize > 50 * 1024 * 1024)
        {
            throw new ValidationException("file", "File size must not exceed 50MB");
        }

        await EnsureTopicExistsAsync(topicId, userId, ct);

        byte[] bytes;
        using (var ms = new MemoryStream())
        {
            await fileStream.CopyToAsync(ms, ct);
            bytes = ms.ToArray();
        }

        var fileHash = ComputeSha256(bytes);

        var duplicate = await _db.Sources.FirstOrDefaultAsync(
            s => s.UserId == userId && s.TopicId == topicId && s.ContentHash == fileHash && s.Status != "archived", ct);
        if (duplicate != null)
        {
            throw new DuplicateException("This file has already been imported into this topic");
        }

        var now = DateTime.UtcNow;
        var fileId = Guid.NewGuid();
        var bucket = "knowledge-engine";
        var objectKey = $"users/{userId}/sources/{fileId}/original{extension}";

        using var uploadStream = new MemoryStream(bytes);
        var storageProvider = await _fileStorageService.UploadFileInternalAsync(
            bucket, objectKey, uploadStream, contentType, fileSize, ct);

        var fileObject = new FileObject
        {
            Id = fileId,
            WorkspaceId = userId,
            Bucket = bucket,
            ObjectKey = objectKey,
            OriginalFilename = fileName,
            MimeType = contentType,
            SizeBytes = fileSize,
            Sha256 = fileHash,
            StorageProvider = storageProvider,
            CreatedAt = now
        };
        _db.Files.Add(fileObject);

        var source = new Source
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TopicId = topicId,
            SourceType = sourceType,
            Title = string.IsNullOrWhiteSpace(title) ? fileName : title.Trim(),
            OriginalFileId = fileId,
            ContentHash = fileHash,
            ImportedAt = now,
            Status = "queued",
            CreatedAt = now,
            UpdatedAt = now
        };

        _db.Sources.Add(source);

        var job = new IngestJob
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            SourceId = source.Id,
            JobType = sourceType == "pdf" ? "parse_pdf" : "parse_file",
            Status = "pending",
            CreatedAt = now
        };
        _db.IngestJobs.Add(job);

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("File source imported: {SourceId}, type={SourceType}, user={UserId}",
            source.Id, sourceType, userId);
        return ApiResponse<SourceResponse>.Ok(Mapper.ToSourceResponse(source));
    }

    public async Task<ApiResponse<PagedResult<SourceListItem>>> GetAllAsync(Guid? topicId = null, string? status = null, string? sourceType = null, int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        var userId = RequireUserId();

        if (page < 1) page = 1;
        if (pageSize < 1 || pageSize > 100) pageSize = 20;

        var query = _db.Sources.Where(s => s.UserId == userId && s.Status != "archived");

        if (topicId.HasValue)
        {
            query = query.Where(s => s.TopicId == topicId.Value);
        }

        if (!string.IsNullOrEmpty(status))
        {
            query = query.Where(s => s.Status == status);
        }

        if (!string.IsNullOrEmpty(sourceType))
        {
            query = query.Where(s => s.SourceType == sourceType);
        }

        var total = await query.CountAsync(ct);
        var sources = await query
            .OrderByDescending(s => s.ImportedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var items = sources.Select(Mapper.ToSourceListItem).ToList();

        var result = new PagedResult<SourceListItem>
        {
            Items = items,
            Total = total,
            Page = page,
            PageSize = pageSize
        };

        return ApiResponse<PagedResult<SourceListItem>>.Ok(result);
    }

    public async Task<ApiResponse<SourceDetail>> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var userId = RequireUserId();

        var source = await _db.Sources.FirstOrDefaultAsync(s => s.Id == id && s.UserId == userId, ct);
        if (source == null)
        {
            throw new NotFoundException("Source", id);
        }

        return ApiResponse<SourceDetail>.Ok(Mapper.ToSourceDetail(source));
    }

    public async Task<ApiResponse<object>> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var userId = RequireUserId();

        var source = await _db.Sources.FirstOrDefaultAsync(s => s.Id == id && s.UserId == userId, ct);
        if (source == null)
        {
            throw new NotFoundException("Source", id);
        }

        source.Status = "archived";
        source.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Source deleted: {SourceId} by {UserId}", source.Id, userId);
        return ApiResponse<object>.Ok(new { id = source.Id, status = "archived" });
    }

    public async Task<ApiResponse<SourceResponse>> RetryAsync(Guid id, string? fromStep = null, CancellationToken ct = default)
    {
        var userId = RequireUserId();

        var source = await _db.Sources.FirstOrDefaultAsync(s => s.Id == id && s.UserId == userId, ct);
        if (source == null)
        {
            throw new NotFoundException("Source", id);
        }

        if (source.Status != "failed")
        {
            throw new ValidationException("status", "Only failed sources can be retried");
        }

        var now = DateTime.UtcNow;
        source.Status = "queued";
        source.ErrorMessage = null;
        source.RetryCount += 1;
        source.UpdatedAt = now;

        // Determine whether this is an AI-only retry (resume from the ai_summarize step)
        var isAiOnlyRetry = string.Equals(fromStep, "ai_summarize", StringComparison.OrdinalIgnoreCase);

        string jobType;
        if (isAiOnlyRetry)
        {
            // Only re-run the AI summarization stage: reset the document's AI status
            // to pending and leave parse/clean statuses intact so parsing is not repeated.
            jobType = "ai_summarize";

            var document = await _db.Documents.FirstOrDefaultAsync(d => d.SourceId == source.Id, ct);
            if (document != null)
            {
                document.AiStatus = "pending";
                document.AiErrorMessage = null;
                document.UpdatedAt = now;
                // Note: ParseStatus and CleanStatus are intentionally left as-is
                // (they should already be "done") so the pipeline can skip re-parsing.
            }

            _logger.LogInformation("Source AI-only retry: {SourceId} by {UserId} (retry #{Count})",
                source.Id, userId, source.RetryCount);
        }
        else
        {
            // Full re-processing from the beginning (parse, clean, AI)
            jobType = source.SourceType switch
            {
                "url" => "fetch_url",
                "text" => "parse_text",
                "pdf" => "parse_pdf",
                "markdown" or "text_file" or "word" or "spreadsheet" or "csv" => "parse_file",
                _ => "prepare_document"
            };

            _logger.LogInformation("Source retry: {SourceId} by {UserId} (retry #{Count})",
                source.Id, userId, source.RetryCount);
        }

        var job = new IngestJob
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            SourceId = source.Id,
            JobType = jobType,
            Status = "pending",
            CreatedAt = now
        };
        _db.IngestJobs.Add(job);

        await _db.SaveChangesAsync(ct);

        return ApiResponse<SourceResponse>.Ok(Mapper.ToSourceResponse(source));
    }

    public async Task<ApiResponse<object>> TriggerProcessingAsync(Guid sourceId, CancellationToken ct = default)
    {
        var userId = RequireUserId();
        var normalizedSourceId = sourceId.ToString("D");
        var normalizedUserId = userId.ToString("D");

        // The Python fetcher writes canonical lower-case UUID text, while
        // Microsoft.Data.Sqlite binds Guid parameters as upper-case text.
        // SQLite compares those TEXT values case-sensitively, so keep the
        // regular comparison and add a normalized compatibility branch.
        var source = await _db.Sources.AsNoTracking().FirstOrDefaultAsync(s =>
            s.Id == sourceId || s.Id.ToString().ToLower() == normalizedSourceId, ct);
        if (source != null && source.UserId != userId)
        {
            throw new NotFoundException("Source", sourceId);
        }

        var restoredMissingSource = source == null;
        if (source == null)
        {
            var document = await _db.Documents.FirstOrDefaultAsync(
                d =>
                    (d.SourceId == sourceId || d.SourceId.ToString().ToLower() == normalizedSourceId) &&
                    (d.UserId == userId || d.UserId.ToString().ToLower() == normalizedUserId), ct);
            if (document == null)
            {
                throw new NotFoundException("Source", sourceId);
            }

            var restoredAt = DateTime.UtcNow;
            var canRefetchUrl = !string.IsNullOrWhiteSpace(document.SourceUrl);
            source = new Source
            {
                Id = sourceId,
                UserId = userId,
                TopicId = document.TopicId,
                SourceType = canRefetchUrl ? "url" : "text",
                Title = document.TitleOriginal ?? document.Title,
                Url = canRefetchUrl ? document.SourceUrl : null,
                Domain = document.SourceDomain,
                Author = document.Author,
                PublishedAt = document.PublishedAt,
                RawText = canRefetchUrl ? null : document.ContentText,
                ContentHash = document.ContentHash,
                ImportedAt = document.CreatedAt,
                Status = "queued",
                CreatedAt = document.CreatedAt,
                UpdatedAt = restoredAt
            };
            _db.Sources.Add(source);
            _logger.LogWarning(
                "Restored missing source {SourceId} from document {DocumentId} before retry",
                sourceId, document.Id);
        }

        // A fetcher import can have a completed Source while its linked Document AI
        // processing failed. In that case the document retry action must be allowed
        // to queue the source again.
        if (source.Status == "done")
        {
            var hasFailedDocument = await _db.Documents.AnyAsync(d =>
                (d.SourceId == sourceId || d.SourceId.ToString().ToLower() == normalizedSourceId) &&
                (d.UserId == userId || d.UserId.ToString().ToLower() == normalizedUserId) &&
                d.AiStatus == "failed", ct);
            if (!hasFailedDocument)
            {
                throw new ValidationException("status", "Source has already been processed");
            }
        }

        if (source.Status == "processing" || source.Status == "parsing" ||
            source.Status == "ai_processing" || source.Status == "fetching" ||
            source.Status == "cleaning" || source.Status == "indexing")
        {
            throw new ValidationException("status", "Source is currently being processed");
        }

        var now = DateTime.UtcNow;
        source.Status = "queued";
        source.ErrorMessage = null;
        source.UpdatedAt = now;

        if (restoredMissingSource)
        {
            await _db.SaveChangesAsync(ct);
        }
        else
        {
            var affected = await _db.Sources
                .Where(s =>
                    (s.Id == sourceId || s.Id.ToString().ToLower() == normalizedSourceId) &&
                    (s.UserId == userId || s.UserId.ToString().ToLower() == normalizedUserId))
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(s => s.Status, "queued")
                    .SetProperty(s => s.ErrorMessage, (string?)null)
                    .SetProperty(s => s.UpdatedAt, now), ct);
            if (affected == 0)
            {
                throw new NotFoundException("Source", sourceId);
            }
        }

        _logger.LogInformation("Processing triggered for source {SourceId} by {UserId}", sourceId, userId);

        return ApiResponse<object>.Ok(new
        {
            source_id = source.Id,
            status = source.Status,
            message = "Source has been queued for AI processing"
        });
    }

    private async Task EnsureTopicExistsAsync(Guid topicId, Guid userId, CancellationToken ct)
    {
        var topicExists = await _db.Topics.AnyAsync(t => t.Id == topicId && t.UserId == userId && t.Status != "deleted", ct);
        if (!topicExists)
        {
            throw new NotFoundException("Topic", topicId);
        }
    }

    private static string ComputeSha256(string input)
    {
        var bytes = Encoding.UTF8.GetBytes(input);
        return ComputeSha256(bytes);
    }

    private static string ComputeSha256(byte[] bytes)
    {
        var hashBytes = SHA256.HashData(bytes);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    private static string? ExtractDomain(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return uri.Host;
        }
        return null;
    }

    private Guid RequireUserId()
    {
        if (!_currentUser.IsAuthenticated || _currentUser.UserId == null)
        {
            throw new UnauthorizedException("User is not authenticated");
        }
        return _currentUser.UserId.Value;
    }
}
