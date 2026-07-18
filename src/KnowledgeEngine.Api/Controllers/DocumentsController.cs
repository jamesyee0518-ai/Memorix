using KnowledgeEngine.Application.DTOs;
using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeEngine.Api.Controllers;

[Authorize]
public class DocumentsController : BaseController
{
    private readonly DocumentService _documentService;
    private readonly IAppDbContext _db;
    private readonly IVectorStore _vectorStore;
    private readonly ITagWorker _tagWorker;
    private readonly IEntityWorker _entityWorker;
    private readonly ILanguageDetectionService _languageDetection;
    private readonly IContentClassificationService _contentClassification;
    private readonly IChineseNormalizationService _chineseNormalization;
    private readonly IL1LocalizationService _l1Localization;
    private readonly IChineseFullTextIndexService _fullTextIndex;
    private readonly ICurrentUserContext _currentUser;
    private readonly IChunkLocalizationService _chunkLocalization;
    private readonly IChunkEnrichmentService _chunkEnrichment;
    private readonly IMultilingualBatchJobService _batchJobs;

    public DocumentsController(
        DocumentService documentService,
        IAppDbContext db,
        IVectorStore vectorStore,
        ITagWorker tagWorker,
        IEntityWorker entityWorker,
        ILanguageDetectionService languageDetection,
        IContentClassificationService contentClassification,
        IChineseNormalizationService chineseNormalization,
        IL1LocalizationService l1Localization,
        IChineseFullTextIndexService fullTextIndex,
        ICurrentUserContext currentUser,
        IChunkLocalizationService chunkLocalization,
        IChunkEnrichmentService chunkEnrichment,
        IMultilingualBatchJobService batchJobs)
    {
        _documentService = documentService;
        _db = db;
        _vectorStore = vectorStore;
        _tagWorker = tagWorker;
        _entityWorker = entityWorker;
        _languageDetection = languageDetection;
        _contentClassification = contentClassification;
        _chineseNormalization = chineseNormalization;
        _l1Localization = l1Localization;
        _fullTextIndex = fullTextIndex;
        _currentUser = currentUser;
        _chunkLocalization = chunkLocalization;
        _chunkEnrichment = chunkEnrichment;
        _batchJobs = batchJobs;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] Guid? topicId,
        [FromQuery] string? aiStatus,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var result = await _documentService.GetAllAsync(topicId, aiStatus, page, pageSize, ct);
        return Ok(ApiResponse<PagedResult<DocumentListItem>>.Ok(result.Data!, GetTraceId()));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById([FromRoute] Guid id, CancellationToken ct)
    {
        var result = await _documentService.GetByIdAsync(id, ct);
        return Ok(ApiResponse<DocumentDetail>.Ok(result.Data!, GetTraceId()));
    }

    [HttpGet("{id:guid}/language")]
    public async Task<IActionResult> GetLanguage([FromRoute] Guid id, CancellationToken ct)
    {
        var document = await _db.Documents.FirstOrDefaultAsync(item => item.Id == id, ct);
        if (document == null)
            return NotFound(ApiResponse<object>.FailObject("NOT_FOUND", "Document not found", GetTraceId()));

        return Ok(ApiResponse<object>.Ok(new
        {
            document.PrimaryLanguage,
            LegacyLanguage = document.Language,
            document.LanguageDistribution,
            document.IsMultilingual,
            document.LanguageDetectStatus,
            document.LocalizationStrategy,
            document.LocalizationLevel,
            document.LocalizationStatus
        }, GetTraceId()));
    }

    [HttpPut("{id:guid}/language")]
    public async Task<IActionResult> UpdateLanguage([FromRoute] Guid id, [FromBody] UpdateDocumentLanguageRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.PrimaryLanguage) || request.PrimaryLanguage.Length > 20)
            return BadRequest(ApiResponse<object>.FailObject("INVALID_LANGUAGE", "Language must be a valid BCP-47 language code", GetTraceId()));

        var document = await _db.Documents.FirstOrDefaultAsync(item => item.Id == id, ct);
        if (document == null)
            return NotFound(ApiResponse<object>.FailObject("NOT_FOUND", "Document not found", GetTraceId()));

        document.PrimaryLanguage = request.PrimaryLanguage.Trim();
        document.Language = document.PrimaryLanguage;
        document.LanguageDistribution = System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, double>
        {
            [document.PrimaryLanguage] = 1
        });
        document.IsMultilingual = request.IsMultilingual;
        document.LanguageDetectStatus = "manual";
        document.LocalizationStrategy = document.PrimaryLanguage.StartsWith("zh", StringComparison.OrdinalIgnoreCase)
            ? "none"
            : "metadata_only";
        document.LocalizationStatus = document.PrimaryLanguage.StartsWith("zh", StringComparison.OrdinalIgnoreCase)
            ? "not_required"
            : "pending";
        document.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return Ok(ApiResponse<bool>.Ok(true, GetTraceId()));
    }

    [HttpPut("{id:guid}/localization-level")]
    public async Task<IActionResult> UpdateLocalizationLevel([FromRoute] Guid id, [FromBody] UpdateLocalizationLevelRequest request, CancellationToken ct)
    {
        var level = request.Level?.Trim().ToUpperInvariant();
        if (level is not ("L1" or "L2" or "L3"))
            return BadRequest(ApiResponse<object>.FailObject("INVALID_LEVEL", "Localization level must be L1, L2 or L3", GetTraceId()));
        var document = await _db.Documents.FirstOrDefaultAsync(item => item.Id == id, ct);
        if (document == null)
            return NotFound(ApiResponse<object>.FailObject("NOT_FOUND", "Document not found", GetTraceId()));
        document.LocalizationLevel = level;
        document.LocalizationStrategy = level switch { "L3" => "full", "L2" => "on_demand", _ => "metadata_only" };
        document.LocalizationStatus = "pending";
        document.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return Ok(ApiResponse<bool>.Ok(true, GetTraceId()));
    }

    [HttpPost("{id:guid}/reprocess-language")]
    public async Task<IActionResult> ReprocessLanguage([FromRoute] Guid id, CancellationToken ct)
    {
        var document = await _db.Documents.FirstOrDefaultAsync(item => item.Id == id, ct);
        if (document == null)
            return NotFound(ApiResponse<object>.FailObject("NOT_FOUND", "Document not found", GetTraceId()));

        var detection = _languageDetection.Detect(document.ContentText ?? document.ContentMarkdown);
        document.PrimaryLanguage = detection.PrimaryLanguage;
        document.Language = detection.PrimaryLanguage;
        document.LanguageDistribution = detection.DistributionJson;
        document.IsMultilingual = detection.IsMultilingual;
        document.LanguageDetectStatus = "done";
        document.LocalizationStrategy = detection.PrimaryLanguage.StartsWith("zh", StringComparison.OrdinalIgnoreCase) ? "none" : "metadata_only";
        document.LocalizationStatus = detection.PrimaryLanguage.StartsWith("zh", StringComparison.OrdinalIgnoreCase) ? "not_required" : "pending";
        document.TitleOriginal ??= document.Title;
        if (detection.PrimaryLanguage.StartsWith("zh", StringComparison.OrdinalIgnoreCase)) document.TitleZh ??= document.Title;

        var chunks = await _db.DocumentChunks.Where(item => item.DocumentId == id).ToListAsync(ct);
        foreach (var chunk in chunks)
        {
            var chunkDetection = _languageDetection.Detect(chunk.ContentOriginal.Length > 0 ? chunk.ContentOriginal : chunk.Content);
            var classification = _contentClassification.Classify(chunk.Content, chunkDetection);
            chunk.ContentOriginal = chunk.ContentOriginal.Length > 0 ? chunk.ContentOriginal : chunk.Content;
            chunk.DetectedLanguage = chunkDetection.PrimaryLanguage;
            chunk.LanguageConfidence = (decimal)chunkDetection.Confidence;
            chunk.LanguageDistribution = chunkDetection.DistributionJson;
            chunk.ContentType = classification.ContentType;
            chunk.ProcessingRoute = classification.ProcessingRoute;
            chunk.LocalizationRequired = classification.LocalizationRequired;
            chunk.ChunkGroupId ??= Guid.NewGuid();
            chunk.ContentNormalized = chunkDetection.PrimaryLanguage.StartsWith("zh", StringComparison.OrdinalIgnoreCase)
                ? _chineseNormalization.Normalize(chunk.ContentOriginal)
                : null;
            chunk.UpdatedAt = DateTime.UtcNow;
        }

        document.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return Ok(ApiResponse<object>.Ok(new { documentId = id, chunks = chunks.Count, detection.PrimaryLanguage, detection.IsMultilingual }, GetTraceId()));
    }

    [HttpPost("{id:guid}/localize-l1")]
    public async Task<IActionResult> LocalizeL1([FromRoute] Guid id, CancellationToken ct)
    {
        try
        {
            var userId = _currentUser.UserId ?? throw new UnauthorizedAccessException("User context is required");
            if (!await _db.Documents.AsNoTracking().AnyAsync(d => d.Id == id && d.UserId == userId, ct))
                return NotFound(ApiResponse<object>.FailObject("NOT_FOUND", "Document not found", GetTraceId()));
            var result = await _l1Localization.LocalizeDocumentAsync(id, ct);
            return Ok(ApiResponse<L1LocalizationResult>.Ok(result, GetTraceId()));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(ApiResponse<object>.FailObject("NOT_FOUND", "Document not found", GetTraceId()));
        }
    }

    [HttpPut("{id:guid}/localized-metadata")]
    public async Task<IActionResult> UpdateLocalizedMetadata([FromRoute] Guid id,
        [FromBody] UpdateLocalizedMetadataRequest request, CancellationToken ct)
    {
        var userId = _currentUser.UserId ?? throw new UnauthorizedAccessException("User context is required");
        var document = await _db.Documents.FirstOrDefaultAsync(d => d.Id == id && d.UserId == userId, ct);
        if (document == null) return NotFound(ApiResponse<object>.FailObject("NOT_FOUND", "Document not found", GetTraceId()));
        if (string.IsNullOrWhiteSpace(request.TitleZh) || string.IsNullOrWhiteSpace(request.SummaryZh))
            return BadRequest(ApiResponse<object>.FailObject("INVALID_LOCALIZATION", "Chinese title and summary are required", GetTraceId()));

        document.TitleZh = request.TitleZh.Trim();
        document.SummaryZh = request.SummaryZh.Trim();
        document.KeywordsZh = System.Text.Json.JsonSerializer.Serialize(request.KeywordsZh ?? Array.Empty<string>());
        document.LocalizationStatus = request.Approved ? "done" : "review_required";
        document.LocalizationModel = "manual";
        document.LocalizationQualityScore = request.Approved ? 100 : document.LocalizationQualityScore;
        document.LocalizationQualityIssues = request.Approved ? "[]" : document.LocalizationQualityIssues;
        document.LocalizedAt = DateTime.UtcNow;
        document.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        await _fullTextIndex.IndexDocumentAsync(id, ct);
        return Ok(ApiResponse<bool>.Ok(true, GetTraceId()));
    }

    [HttpPost("actions/backfill-l1")]
    public async Task<IActionResult> BackfillL1([FromQuery] int limit = 20, CancellationToken ct = default)
    {
        limit = Math.Clamp(limit, 1, 100);
        var userId = _currentUser.UserId ?? throw new UnauthorizedAccessException("User context is required");
        var ids = await _db.Documents.AsNoTracking()
            .Where(d => d.UserId == userId && d.AiStatus == "done"
                && d.LocalizationStatus != "done" && d.LocalizationStatus != "review_required")
            .OrderBy(d => d.CreatedAt).Select(d => d.Id).Take(limit).ToListAsync(ct);
        var succeeded = 0;
        var failed = new List<object>();
        foreach (var id in ids)
        {
            try { await _l1Localization.LocalizeDocumentAsync(id, ct); succeeded++; }
            catch (Exception ex) { failed.Add(new { documentId = id, error = ex.Message }); }
        }
        return Ok(ApiResponse<object>.Ok(new { requested = ids.Count, succeeded, failed }, GetTraceId()));
    }

    [HttpPost("actions/rebuild-chinese-index")]
    public async Task<IActionResult> RebuildChineseIndex([FromQuery] int limit = 500, [FromQuery] bool force = false, CancellationToken ct = default)
    {
        limit = Math.Clamp(limit, 1, 5000);
        var userId = _currentUser.UserId ?? throw new UnauthorizedAccessException("User context is required");
        var query = _db.Documents.AsNoTracking().Where(d => d.UserId == userId && d.AiStatus == "done");
        if (!force) query = query.Where(d => d.FulltextIndexStatus != "done");
        var ids = await query.OrderBy(d => d.CreatedAt).Select(d => d.Id).Take(limit).ToListAsync(ct);
        var succeeded = 0;
        var failed = new List<object>();
        foreach (var id in ids)
        {
            try { await _fullTextIndex.IndexDocumentAsync(id, ct); succeeded++; }
            catch (Exception ex) { failed.Add(new { documentId = id, error = ex.Message }); }
        }
        return Ok(ApiResponse<object>.Ok(new { requested = ids.Count, succeeded, failed }, GetTraceId()));
    }

    [HttpGet("{id:guid}/entities")]
    public async Task<IActionResult> GetEntities([FromRoute] Guid id, CancellationToken ct)
    {
        var result = await _documentService.GetDocumentEntitiesAsync(id, ct);
        return Ok(ApiResponse<List<EntityInDocument>>.Ok(result.Data!, GetTraceId()));
    }

    [HttpGet("{id:guid}/processing-status")]
    public async Task<IActionResult> GetProcessingStatus([FromRoute] Guid id, CancellationToken ct)
    {
        var result = await _documentService.GetProcessingStatusAsync(id, ct);
        return Ok(ApiResponse<ProcessingStatusResponse>.Ok(result.Data!, GetTraceId()));
    }

    [HttpGet("{id:guid}/processing-logs")]
    public async Task<IActionResult> GetProcessingLogs([FromRoute] Guid id, CancellationToken ct)
    {
        var result = await _documentService.GetProcessingLogsAsync(id, ct);
        return Ok(ApiResponse<List<ProcessingLogItem>>.Ok(result.Data!, GetTraceId()));
    }

    [HttpGet("{documentId:guid}/chunks")]
    public async Task<IActionResult> GetChunks([FromRoute] Guid documentId, CancellationToken ct)
    {
        var document = await _db.Documents.FirstOrDefaultAsync(d => d.Id == documentId, ct);
        if (document == null)
        {
            return NotFound(ApiResponse<object>.FailObject("NOT_FOUND", "Document not found", GetTraceId()));
        }

        var chunks = await _db.DocumentChunks
            .Where(chunk => chunk.DocumentId == documentId)
            .OrderBy(chunk => chunk.ChunkIndex)
            .Select(chunk => new
            {
                chunk.Id,
                chunk.DocumentId,
                chunk.ChunkIndex,
                chunk.ChunkUid,
                chunk.ChunkTitle,
                chunk.HeadingPath,
                chunk.SectionLevel,
                chunk.Content,
                chunk.ContentMarkdown,
                chunk.ContentOriginal,
                chunk.ContentNormalized,
                chunk.DetectedLanguage,
                chunk.LanguageConfidence,
                chunk.LanguageDistribution,
                chunk.ContentType,
                chunk.ProcessingRoute,
                chunk.LocalizationRequired,
                chunk.ChunkGroupId,
                chunk.ParentChunkId,
                chunk.ParagraphStart,
                chunk.ParagraphEnd,
                chunk.BoundingBox,
                chunk.ContentHash,
                chunk.TokenCount,
                chunk.CharCount,
                chunk.StartOffset,
                chunk.EndOffset,
                chunk.PrevChunkId,
                chunk.NextChunkId,
                chunk.PageStart,
                chunk.PageEnd,
                chunk.EmbeddingStatus,
                chunk.EmbeddingModel,
                chunk.IndexStatus,
                chunk.Metadata,
                chunk.CreatedAt,
                chunk.UpdatedAt
            })
            .ToListAsync(ct);

        return Ok(ApiResponse<object>.Ok(chunks, GetTraceId()));
    }

    [HttpPost("~/api/chunks/{chunkId:guid}/translate")]
    public async Task<IActionResult> TranslateChunk([FromRoute] Guid chunkId, [FromBody] TranslateChunkRequest? request, CancellationToken ct)
    {
        var userId = _currentUser.UserId ?? throw new UnauthorizedAccessException("User context is required");
        try
        {
            var result = await _chunkLocalization.TranslateAsync(userId, chunkId,
                new ChunkTranslationRequest(request?.LanguageCode ?? "zh-CN", request?.Force ?? false, "machine"), ct);
            return Ok(ApiResponse<KnowledgeEngine.Domain.Entities.ChunkLocalization>.Ok(result, GetTraceId()));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(ApiResponse<object>.FailObject("NOT_FOUND", "Chunk not found", GetTraceId()));
        }
    }

    [HttpPost("{documentId:guid}/translate-chunks")]
    public async Task<IActionResult> TranslateDocumentChunks([FromRoute] Guid documentId,
        [FromBody] ChunkBatchRequest? request, CancellationToken ct)
    {
        var userId = _currentUser.UserId ?? throw new UnauthorizedAccessException("User context is required");
        try
        {
            var result = await _batchJobs.EnqueueAsync(userId, documentId, "translate",
                request?.Force ?? false, request?.MaxChunks ?? 500, ct);
            return Accepted(ApiResponse<KnowledgeEngine.Domain.Entities.MultilingualBatchJob>.Ok(result, GetTraceId()));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(ApiResponse<object>.FailObject("NOT_FOUND", "Document not found", GetTraceId()));
        }
    }

    [HttpPost("~/api/chunks/{chunkId:guid}/enrich")]
    public async Task<IActionResult> EnrichChunk([FromRoute] Guid chunkId,
        [FromBody] EnrichChunkRequest? request, CancellationToken ct)
    {
        var userId = _currentUser.UserId ?? throw new UnauthorizedAccessException("User context is required");
        try
        {
            var result = await _chunkEnrichment.EnrichAsync(userId, chunkId, request?.Force ?? false, ct);
            return Ok(ApiResponse<KnowledgeEngine.Domain.Entities.ChunkEnrichment>.Ok(result, GetTraceId()));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(ApiResponse<object>.FailObject("NOT_FOUND", "Chunk not found", GetTraceId()));
        }
    }

    [HttpGet("~/api/chunks/{chunkId:guid}/enrichments")]
    public async Task<IActionResult> GetChunkEnrichments([FromRoute] Guid chunkId, CancellationToken ct)
    {
        var userId = _currentUser.UserId ?? throw new UnauthorizedAccessException("User context is required");
        var result = await _chunkEnrichment.ListAsync(userId, chunkId, ct);
        return Ok(ApiResponse<IReadOnlyList<KnowledgeEngine.Domain.Entities.ChunkEnrichment>>.Ok(result, GetTraceId()));
    }

    [HttpPost("{documentId:guid}/enrich-chunks")]
    public async Task<IActionResult> EnrichDocumentChunks([FromRoute] Guid documentId,
        [FromBody] ChunkBatchRequest? request, CancellationToken ct)
    {
        var userId = _currentUser.UserId ?? throw new UnauthorizedAccessException("User context is required");
        try
        {
            var result = await _batchJobs.EnqueueAsync(userId, documentId, "enrich",
                request?.Force ?? false, request?.MaxChunks ?? 500, ct);
            return Accepted(ApiResponse<KnowledgeEngine.Domain.Entities.MultilingualBatchJob>.Ok(result, GetTraceId()));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(ApiResponse<object>.FailObject("NOT_FOUND", "Document not found", GetTraceId()));
        }
    }

    [HttpGet("{documentId:guid}/batch-jobs")]
    public async Task<IActionResult> GetDocumentBatchJobs([FromRoute] Guid documentId, CancellationToken ct)
    {
        var userId = _currentUser.UserId ?? throw new UnauthorizedAccessException("User context is required");
        return Ok(ApiResponse<IReadOnlyList<KnowledgeEngine.Domain.Entities.MultilingualBatchJob>>.Ok(
            await _batchJobs.ListAsync(userId, documentId, ct), GetTraceId()));
    }

    [HttpPost("{documentId:guid}/rebuild-multi-vectors")]
    public async Task<IActionResult> RebuildMultiVectors([FromRoute] Guid documentId,
        [FromBody] ChunkBatchRequest? request, CancellationToken ct)
    {
        var userId = _currentUser.UserId ?? throw new UnauthorizedAccessException("User context is required");
        try
        {
            var job = await _batchJobs.EnqueueAsync(userId, documentId, "multi_vector",
                request?.Force ?? false, request?.MaxChunks ?? 500, ct);
            return Accepted(ApiResponse<KnowledgeEngine.Domain.Entities.MultilingualBatchJob>.Ok(job, GetTraceId()));
        }
        catch (KeyNotFoundException) { return NotFound(ApiResponse<object>.FailObject("NOT_FOUND", "Document not found", GetTraceId())); }
    }

    [HttpGet("~/api/multilingual-jobs/{jobId:guid}")]
    public async Task<IActionResult> GetBatchJob([FromRoute] Guid jobId, CancellationToken ct)
    {
        var userId = _currentUser.UserId ?? throw new UnauthorizedAccessException("User context is required");
        var job = await _batchJobs.GetAsync(userId, jobId, ct);
        return job == null ? NotFound(ApiResponse<object>.FailObject("NOT_FOUND", "Job not found", GetTraceId()))
            : Ok(ApiResponse<KnowledgeEngine.Domain.Entities.MultilingualBatchJob>.Ok(job, GetTraceId()));
    }

    [HttpPost("~/api/multilingual-jobs/{jobId:guid}/{action}")]
    public async Task<IActionResult> ControlBatchJob([FromRoute] Guid jobId, [FromRoute] string action, CancellationToken ct)
    {
        var userId = _currentUser.UserId ?? throw new UnauthorizedAccessException("User context is required");
        try
        {
            var job = action.ToLowerInvariant() switch
            {
                "pause" => await _batchJobs.PauseAsync(userId, jobId, ct),
                "resume" => await _batchJobs.ResumeAsync(userId, jobId, ct),
                "retry" => await _batchJobs.RetryAsync(userId, jobId, ct),
                _ => throw new ArgumentException("Unsupported action")
            };
            return Ok(ApiResponse<KnowledgeEngine.Domain.Entities.MultilingualBatchJob>.Ok(job, GetTraceId()));
        }
        catch (KeyNotFoundException) { return NotFound(ApiResponse<object>.FailObject("NOT_FOUND", "Job not found", GetTraceId())); }
        catch (ArgumentException ex) { return BadRequest(ApiResponse<object>.FailObject("INVALID_ACTION", ex.Message, GetTraceId())); }
    }

    [HttpGet("~/api/chunks/{chunkId:guid}/localizations")]
    public async Task<IActionResult> GetChunkLocalizations([FromRoute] Guid chunkId, CancellationToken ct)
    {
        var userId = _currentUser.UserId ?? throw new UnauthorizedAccessException("User context is required");
        var result = await _chunkLocalization.ListAsync(userId, chunkId, ct);
        return Ok(ApiResponse<IReadOnlyList<KnowledgeEngine.Domain.Entities.ChunkLocalization>>.Ok(result, GetTraceId()));
    }

    [HttpPost("~/api/chunks/{chunkId:guid}/localizations/{localizationId:guid}/review")]
    public async Task<IActionResult> ReviewChunkLocalization([FromRoute] Guid chunkId, [FromRoute] Guid localizationId,
        [FromBody] ReviewChunkLocalizationRequest request, CancellationToken ct)
    {
        var userId = _currentUser.UserId ?? throw new UnauthorizedAccessException("User context is required");
        try
        {
            var result = await _chunkLocalization.ReviewAsync(userId, chunkId, localizationId,
                request.HeadingLocalized ?? string.Empty, request.ContentLocalized, request.Approved, ct);
            return Ok(ApiResponse<KnowledgeEngine.Domain.Entities.ChunkLocalization>.Ok(result, GetTraceId()));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(ApiResponse<object>.FailObject("NOT_FOUND", "Localization not found", GetTraceId()));
        }
    }

    [HttpGet("chunks/{chunkId:guid}")]
    public async Task<IActionResult> GetChunk([FromRoute] Guid chunkId, CancellationToken ct)
    {
        var chunk = await _db.DocumentChunks.FirstOrDefaultAsync(item => item.Id == chunkId, ct);
        if (chunk == null)
        {
            return NotFound(ApiResponse<object>.FailObject("NOT_FOUND", "Chunk not found", GetTraceId()));
        }
        return Ok(ApiResponse<object>.Ok(chunk, GetTraceId()));
    }

    [HttpGet("chunks/{chunkId:guid}/embedding")]
    public async Task<IActionResult> GetChunkEmbedding([FromRoute] Guid chunkId, CancellationToken ct)
    {
        var embedding = await _db.ChunkEmbeddings
            .Where(item => item.ChunkId == chunkId)
            .OrderByDescending(item => item.UpdatedAt)
            .Select(item => new
            {
                item.Id,
                item.ChunkId,
                item.Provider,
                item.Model,
                item.ModelVersion,
                item.Dimension,
                item.Status,
                item.ErrorMessage,
                item.RetryCount,
                item.ChunkContentHash,
                item.CreatedAt,
                item.UpdatedAt
            })
            .FirstOrDefaultAsync(ct);
        if (embedding == null)
        {
            return NotFound(ApiResponse<object>.FailObject("NOT_FOUND", "Chunk embedding not found", GetTraceId()));
        }
        return Ok(ApiResponse<object>.Ok(embedding, GetTraceId()));
    }

    [HttpPost("{id:guid}/resummarize")]
    public async Task<IActionResult> Resummarize([FromRoute] Guid id, [FromBody] ResummarizeRequestDto? request, CancellationToken ct)
    {
        var result = await _documentService.ResummarizeAsync(id, request, ct);
        return Ok(ApiResponse<bool>.Ok(result.Data!, GetTraceId()));
    }

    // ================================================================
    // Phase 4 Action API endpoints (workspace-scoped re-processing triggers)
    // ================================================================

    /// <summary>
    /// Re-generate tags for a document. Calls TagWorker to run AI tag
    /// recommendation and persist new Tag + DocumentTag records.
    /// </summary>
    [HttpPost("~/api/workspaces/{workspaceId}/documents/{documentId}/actions/regenerate-tags")]
    [HttpPost("~/api/documents/{documentId}/actions/regenerate-tags")]
    public async Task<IActionResult> RegenerateTags(
        [FromRoute] Guid? workspaceId,
        [FromRoute] Guid documentId,
        CancellationToken ct)
    {
        var document = await _db.Documents.FirstOrDefaultAsync(d => d.Id == documentId, ct);
        if (document == null)
        {
            return NotFound(ApiResponse<object>.FailObject("NOT_FOUND", "Document not found", GetTraceId()));
        }

        // Run TagWorker to regenerate tags via AI
        await _tagWorker.ProcessDocumentAsync(documentId, ct);

        return Ok(ApiResponse<object>.Ok(
            new { documentId, action = "regenerate-tags", status = "done" },
            GetTraceId()));
    }

    /// <summary>
    /// Re-generate entities for a document. Calls EntityWorker to run AI
    /// entity extraction and persist new Entity + DocumentEntity records.
    /// </summary>
    [HttpPost("~/api/workspaces/{workspaceId}/documents/{documentId}/actions/regenerate-entities")]
    [HttpPost("~/api/documents/{documentId}/actions/regenerate-entities")]
    public async Task<IActionResult> RegenerateEntities(
        [FromRoute] Guid? workspaceId,
        [FromRoute] Guid documentId,
        CancellationToken ct)
    {
        var document = await _db.Documents.FirstOrDefaultAsync(d => d.Id == documentId, ct);
        if (document == null)
        {
            return NotFound(ApiResponse<object>.FailObject("NOT_FOUND", "Document not found", GetTraceId()));
        }

        // Run EntityWorker to regenerate entities via AI
        await _entityWorker.ProcessDocumentAsync(documentId, ct);

        return Ok(ApiResponse<object>.Ok(
            new { documentId, action = "regenerate-entities", status = "done" },
            GetTraceId()));
    }

    /// <summary>
    /// Re-chunk a document. Deletes existing document_chunks and resets
    /// ChunkStatus = "pending" so ChunkWorker picks it up again.
    /// </summary>
    [HttpPost("~/api/workspaces/{workspaceId}/documents/{documentId}/actions/rechunk")]
    [HttpPost("~/api/documents/{documentId}/actions/rechunk")]
    public async Task<IActionResult> Rechunk(
        [FromRoute] Guid? workspaceId,
        [FromRoute] Guid documentId,
        CancellationToken ct)
    {
        var document = await _db.Documents.FirstOrDefaultAsync(d => d.Id == documentId, ct);
        if (document == null)
        {
            return NotFound(ApiResponse<object>.FailObject("NOT_FOUND", "Document not found", GetTraceId()));
        }

        // Delete existing document_chunks for the document
        var existingChunks = await _db.DocumentChunks
            .Where(c => c.DocumentId == documentId)
            .ToListAsync(ct);
        if (existingChunks.Count > 0)
        {
            _db.DocumentChunks.RemoveRange(existingChunks);
        }

        // Set ChunkStatus = "pending" so ChunkWorker picks it up again
        document.ChunkStatus = "pending";
        document.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);

        return Ok(ApiResponse<object>.Ok(
            new { documentId, action = "rechunk", status = "queued" },
            GetTraceId()));
    }

    /// <summary>
    /// Re-embed all chunks for a document. Sets EmbeddingStatus = "pending"
    /// on every document_chunk so EmbeddingWorker re-processes them.
    /// </summary>
    [HttpPost("~/api/workspaces/{workspaceId}/documents/{documentId}/actions/reembed")]
    [HttpPost("~/api/documents/{documentId}/actions/reembed")]
    public async Task<IActionResult> Reembed(
        [FromRoute] Guid? workspaceId,
        [FromRoute] Guid documentId,
        CancellationToken ct)
    {
        var document = await _db.Documents.FirstOrDefaultAsync(d => d.Id == documentId, ct);
        if (document == null)
        {
            return NotFound(ApiResponse<object>.FailObject("NOT_FOUND", "Document not found", GetTraceId()));
        }

        // Set all document_chunks' EmbeddingStatus = "pending"
        var chunks = await _db.DocumentChunks
            .Where(c => c.DocumentId == documentId)
            .ToListAsync(ct);

        var now = DateTime.UtcNow;
        foreach (var chunk in chunks)
        {
            chunk.EmbeddingStatus = "pending";
            chunk.UpdatedAt = now;
        }

        document.EmbeddingStatus = "processing";
        document.IndexStatus = "processing";
        document.UpdatedAt = now;

        await _db.SaveChangesAsync(ct);

        return Ok(ApiResponse<object>.Ok(
            new { documentId, action = "reembed", chunkCount = chunks.Count, status = "queued" },
            GetTraceId()));
    }

    /// <summary>
    /// Rebuild the entire workspace vector index. Marks all embeddings as
    /// stale via IVectorStore.RebuildAsync, which triggers re-embedding.
    /// </summary>
    [HttpPost("~/api/workspaces/{workspaceId}/actions/rebuild-index")]
    public async Task<IActionResult> RebuildIndex(
        [FromRoute] Guid workspaceId,
        CancellationToken ct)
    {
        await _vectorStore.RebuildAsync(workspaceId.ToString(), ct);

        return Ok(ApiResponse<object>.Ok(
            new { workspaceId, action = "rebuild-index", status = "queued" },
            GetTraceId()));
    }
}

public sealed class UpdateDocumentLanguageRequest
{
    public string PrimaryLanguage { get; set; } = string.Empty;
    public bool IsMultilingual { get; set; }
}

public sealed class UpdateLocalizedMetadataRequest
{
    public string TitleZh { get; set; } = string.Empty;
    public string SummaryZh { get; set; } = string.Empty;
    public string[]? KeywordsZh { get; set; }
    public bool Approved { get; set; } = true;
}

public sealed class TranslateChunkRequest
{
    public string LanguageCode { get; set; } = "zh-CN";
    public bool Force { get; set; }
}

public sealed class EnrichChunkRequest
{
    public bool Force { get; set; }
}

public sealed class ChunkBatchRequest
{
    public bool Force { get; set; }
    public int MaxChunks { get; set; } = 500;
}

public sealed class ReviewChunkLocalizationRequest
{
    public string? HeadingLocalized { get; set; }
    public string ContentLocalized { get; set; } = string.Empty;
    public bool Approved { get; set; } = true;
}

public sealed class UpdateLocalizationLevelRequest
{
    public string? Level { get; set; }
}
