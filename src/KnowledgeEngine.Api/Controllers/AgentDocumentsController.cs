using KnowledgeEngine.Application.DTOs;
using KnowledgeEngine.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeEngine.Api.Controllers;

[Route("api/agent/documents")]
public class AgentDocumentsController : AgentApiControllerBase
{
    private readonly IAppDbContext _db;

    public AgentDocumentsController(IAppDbContext db)
    {
        _db = db;
    }

    [HttpGet("{documentId:guid}")]
    public async Task<IActionResult> GetDocument([FromRoute] Guid documentId, CancellationToken ct)
    {
        var userId = AgentUserId;
        if (userId == null)
        {
            return Unauthorized();
        }

        // Check action permission
        if (!CheckActionAllowed("documents:read"))
        {
            return Forbidden("ACTION_NOT_ALLOWED", "This API key does not have permission for 'documents:read'.");
        }

        var document = await _db.Documents
            .FirstOrDefaultAsync(d => d.Id == documentId && d.UserId == userId, ct);

        if (document == null)
        {
            return NotFound(new
            {
                success = false,
                error = new { code = "DOCUMENT_NOT_FOUND", message = "Document not found." },
                trace_id = GetTraceId()
            });
        }

        // Check topic permission
        if (document.TopicId.HasValue && !CheckTopicAllowed(document.TopicId.Value))
        {
            return Forbidden("TOPIC_NOT_ALLOWED", "This API key does not have access to this document's topic.");
        }

        var result = new AgentDocumentResult
        {
            Id = document.Id,
            TopicId = document.TopicId,
            Title = document.Title,
            Summary = document.Summary,
            OneSentenceConclusion = document.OneSentenceConclusion,
            KeyPoints = document.KeyPoints,
            BusinessSignals = document.BusinessSignals,
            TechnicalSignals = document.TechnicalSignals,
            Risks = document.Risks,
            Opportunities = document.Opportunities,
            ValueScore = document.ValueScore,
            AiStatus = document.AiStatus,
            ContentText = document.ContentText,
            CreatedAt = document.CreatedAt,
            UpdatedAt = document.UpdatedAt,
            TraceId = GetTraceId()
        };

        return Ok(new
        {
            id = result.Id,
            topic_id = result.TopicId,
            title = result.Title,
            summary = result.Summary,
            one_sentence_conclusion = result.OneSentenceConclusion,
            key_points = result.KeyPoints,
            business_signals = result.BusinessSignals,
            technical_signals = result.TechnicalSignals,
            risks = result.Risks,
            opportunities = result.Opportunities,
            value_score = result.ValueScore,
            ai_status = result.AiStatus,
            content_text = result.ContentText,
            created_at = result.CreatedAt,
            updated_at = result.UpdatedAt,
            trace_id = result.TraceId
        });
    }

    [HttpGet("{documentId:guid}/chunks")]
    public async Task<IActionResult> GetDocumentChunks([FromRoute] Guid documentId, CancellationToken ct)
    {
        var userId = AgentUserId;
        if (userId == null)
        {
            return Unauthorized();
        }

        // Check action permission
        if (!CheckActionAllowed("documents:read"))
        {
            return Forbidden("ACTION_NOT_ALLOWED", "This API key does not have permission for 'documents:read'.");
        }

        var document = await _db.Documents
            .FirstOrDefaultAsync(d => d.Id == documentId && d.UserId == userId, ct);

        if (document == null)
        {
            return NotFound(new
            {
                success = false,
                error = new { code = "DOCUMENT_NOT_FOUND", message = "Document not found." },
                trace_id = GetTraceId()
            });
        }

        // Check topic permission
        if (document.TopicId.HasValue && !CheckTopicAllowed(document.TopicId.Value))
        {
            return Forbidden("TOPIC_NOT_ALLOWED", "This API key does not have access to this document's topic.");
        }

        var chunks = await _db.DocumentChunks
            .Where(c => c.DocumentId == documentId && c.UserId == userId)
            .OrderBy(c => c.ChunkIndex)
            .ToListAsync(ct);

        var result = new AgentChunkResult
        {
            DocumentId = documentId,
            Chunks = chunks.Select(c => new AgentChunkResultItem
            {
                Id = c.Id,
                ChunkIndex = c.ChunkIndex,
                ChunkTitle = c.ChunkTitle,
                Content = c.Content,
                TokenCount = c.TokenCount,
                CharCount = c.CharCount,
                CreatedAt = c.CreatedAt
            }).ToList(),
            TraceId = GetTraceId()
        };

        return Ok(new
        {
            document_id = result.DocumentId,
            chunks = result.Chunks,
            trace_id = result.TraceId
        });
    }
}
