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

    public DocumentsController(
        DocumentService documentService,
        IAppDbContext db,
        IVectorStore vectorStore,
        ITagWorker tagWorker,
        IEntityWorker entityWorker)
    {
        _documentService = documentService;
        _db = db;
        _vectorStore = vectorStore;
        _tagWorker = tagWorker;
        _entityWorker = entityWorker;
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
    public async Task<IActionResult> RegenerateTags(
        [FromRoute] Guid workspaceId,
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
    public async Task<IActionResult> RegenerateEntities(
        [FromRoute] Guid workspaceId,
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
    public async Task<IActionResult> Rechunk(
        [FromRoute] Guid workspaceId,
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
    public async Task<IActionResult> Reembed(
        [FromRoute] Guid workspaceId,
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
