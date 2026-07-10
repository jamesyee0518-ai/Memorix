using KnowledgeEngine.Application.DTOs;
using KnowledgeEngine.Application.Exceptions;
using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Application.Mapping;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KnowledgeEngine.Application.Services;

public class DocumentService
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUserContext _currentUser;
    private readonly ILogger<DocumentService> _logger;

    public DocumentService(
        IAppDbContext db,
        ICurrentUserContext currentUser,
        ILogger<DocumentService> logger)
    {
        _db = db;
        _currentUser = currentUser;
        _logger = logger;
    }

    public async Task<ApiResponse<PagedResult<DocumentListItem>>> GetAllAsync(
        Guid? topicId = null,
        string? aiStatus = null,
        int page = 1,
        int pageSize = 20,
        CancellationToken ct = default)
    {
        var userId = RequireUserId();

        if (page < 1) page = 1;
        if (pageSize < 1 || pageSize > 100) pageSize = 20;

        var query = _db.Documents.Where(d => d.UserId == userId);

        if (topicId.HasValue)
        {
            query = query.Where(d => d.TopicId == topicId.Value);
        }

        if (!string.IsNullOrEmpty(aiStatus))
        {
            query = query.Where(d => d.AiStatus == aiStatus);
        }

        var total = await query.CountAsync(ct);
        var documents = await query
            .OrderByDescending(d => d.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var items = documents.Select(Mapper.ToDocumentListItem).ToList();

        var result = new PagedResult<DocumentListItem>
        {
            Items = items,
            Total = total,
            Page = page,
            PageSize = pageSize
        };

        return ApiResponse<PagedResult<DocumentListItem>>.Ok(result);
    }

    public async Task<ApiResponse<DocumentDetail>> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var userId = RequireUserId();

        var document = await _db.Documents.FirstOrDefaultAsync(d => d.Id == id && d.UserId == userId, ct);
        if (document == null)
        {
            throw new NotFoundException("Document", id);
        }

        // Load tags
        var tagData = await (
            from dt in _db.DocumentTags
            join t in _db.Tags on dt.TagId equals t.Id
            where dt.DocumentId == id
            select new { Tag = t, DocumentTag = dt }
        ).ToListAsync(ct);

        var tags = tagData.Select(x => Mapper.ToTagResponse(x.Tag, x.DocumentTag)).ToList();

        // Load entities
        var entityData = await (
            from de in _db.DocumentEntities
            join en in _db.Entities on de.EntityId equals en.Id
            where de.DocumentId == id
            select new { Entity = en, DocumentEntity = de }
        ).ToListAsync(ct);

        var entities = entityData.Select(x => Mapper.ToEntityInDocument(x.Entity, x.DocumentEntity)).ToList();

        return ApiResponse<DocumentDetail>.Ok(Mapper.ToDocumentDetail(document, tags, entities));
    }

    public async Task<ApiResponse<DocumentDetail>> GetBySourceIdAsync(Guid sourceId, CancellationToken ct = default)
    {
        var userId = RequireUserId();

        var document = await _db.Documents.FirstOrDefaultAsync(d => d.SourceId == sourceId && d.UserId == userId, ct);
        if (document == null)
        {
            throw new NotFoundException("Document for source", sourceId);
        }

        return await GetByIdAsync(document.Id, ct);
    }

    public async Task<ApiResponse<List<EntityInDocument>>> GetDocumentEntitiesAsync(Guid id, CancellationToken ct = default)
    {
        var userId = RequireUserId();

        var document = await _db.Documents.FirstOrDefaultAsync(d => d.Id == id && d.UserId == userId, ct);
        if (document == null)
        {
            throw new NotFoundException("Document", id);
        }

        var entityData = await (
            from de in _db.DocumentEntities
            join en in _db.Entities on de.EntityId equals en.Id
            where de.DocumentId == id
            select new { Entity = en, DocumentEntity = de }
        ).ToListAsync(ct);

        var entities = entityData.Select(x => Mapper.ToEntityInDocument(x.Entity, x.DocumentEntity)).ToList();

        return ApiResponse<List<EntityInDocument>>.Ok(entities);
    }

    public async Task<ApiResponse<ProcessingStatusResponse>> GetProcessingStatusAsync(Guid id, CancellationToken ct = default)
    {
        var userId = RequireUserId();
        var document = await _db.Documents.FirstOrDefaultAsync(d => d.Id == id && d.UserId == userId, ct);
        if (document == null) throw new NotFoundException("Document", id);

        return ApiResponse<ProcessingStatusResponse>.Ok(new ProcessingStatusResponse
        {
            ParseStatus = document.ParseStatus,
            CleanStatus = document.CleanStatus,
            AiStatus = document.AiStatus,
            ChunkStatus = document.ChunkStatus,
            IndexStatus = document.IndexStatus,
            AiErrorMessage = document.AiErrorMessage
        });
    }

    public async Task<ApiResponse<List<ProcessingLogItem>>> GetProcessingLogsAsync(Guid id, CancellationToken ct = default)
    {
        var userId = RequireUserId();
        var document = await _db.Documents.FirstOrDefaultAsync(d => d.Id == id && d.UserId == userId, ct);
        if (document == null) throw new NotFoundException("Document", id);

        var logs = await _db.DocumentProcessingLogs
            .Where(l => l.DocumentId == id)
            .OrderBy(l => l.CreatedAt)
            .ToListAsync(ct);

        return ApiResponse<List<ProcessingLogItem>>.Ok(logs.Select(Mapper.ToProcessingLogItem).ToList());
    }

    public async Task<ApiResponse<bool>> ResummarizeAsync(Guid id, ResummarizeRequestDto? request = null, CancellationToken ct = default)
    {
        var userId = RequireUserId();
        var document = await _db.Documents.FirstOrDefaultAsync(d => d.Id == id && d.UserId == userId, ct);
        if (document == null) throw new NotFoundException("Document", id);

        // MVP: actual model/prompt switching requires larger plumbing. For now we
        // record the requested overrides as intent so the pipeline can pick them up
        // later, and always re-run the AI stage with the current configuration.
        if (request != null)
        {
            if (!string.IsNullOrWhiteSpace(request.ModelName) ||
                !string.IsNullOrWhiteSpace(request.ModelProvider))
            {
                _logger.LogInformation(
                    "Resummarize requested with model override: provider={Provider}, model={Model} (applied in MVP: current config used)",
                    request.ModelProvider, request.ModelName);
            }

            if (!string.IsNullOrWhiteSpace(request.PromptVersion))
            {
                _logger.LogInformation(
                    "Resummarize requested with prompt version override: {PromptVersion} (applied in MVP: current config used)",
                    request.PromptVersion);
            }
        }

        // Reset AI status to trigger reprocessing
        document.AiStatus = "pending";
        document.AiErrorMessage = null;
        document.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        // Re-queue the source for processing
        var source = await _db.Sources.FirstOrDefaultAsync(s => s.Id == document.SourceId, ct);
        if (source != null)
        {
            source.Status = "queued";
            source.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
        }

        return ApiResponse<bool>.Ok(true);
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
