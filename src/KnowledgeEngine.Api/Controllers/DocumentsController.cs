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
                chunk.ContentHash,
                chunk.TokenCount,
                chunk.CharCount,
                chunk.StartOffset,
                chunk.EndOffset,
                chunk.PrevChunkId,
                chunk.NextChunkId,
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
