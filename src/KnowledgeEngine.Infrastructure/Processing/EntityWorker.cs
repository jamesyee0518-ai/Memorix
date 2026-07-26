using KnowledgeEngine.Application.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KnowledgeEngine.Infrastructure.Processing;

/// <summary>
/// On-demand AI entity extraction worker (Phase 4).
/// Invoked explicitly via the regenerate-entities API action endpoint.
/// </summary>
public class EntityWorker : IEntityWorker
{
    private readonly IAppDbContext _db;
    private readonly IAISummaryService _aiSummaryService;
    private readonly IEntityResolutionOrchestrator _entityResolution;
    private readonly ILogger<EntityWorker> _logger;

    public EntityWorker(
        IAppDbContext db,
        IAISummaryService aiSummaryService,
        IEntityResolutionOrchestrator entityResolution,
        ILogger<EntityWorker> logger)
    {
        _db = db;
        _aiSummaryService = aiSummaryService;
        _entityResolution = entityResolution;
        _logger = logger;
    }

    public async Task ProcessDocumentAsync(Guid documentId, CancellationToken ct = default)
    {
        var document = await _db.Documents.FirstOrDefaultAsync(d => d.Id == documentId, ct);
        if (document == null)
        {
            _logger.LogWarning("EntityWorker: Document {DocumentId} not found", documentId);
            return;
        }

        if (string.IsNullOrWhiteSpace(document.ContentText))
        {
            _logger.LogWarning("EntityWorker: Document {DocumentId} has no content text", documentId);
            return;
        }

        _logger.LogInformation("EntityWorker: Regenerating entities for document {DocumentId}", documentId);

        // Call AI summary service to get entity extraction results
        var aiResult = await _aiSummaryService.SummarizeAsync(
            document.Title,
            document.ContentText,
            document.SourceType ?? "text",
            ct);

        var result = await _entityResolution.ResolveDocumentAsync(
            documentId,
            aiResult.Entities ?? [],
            new EntityExtractionContext
            {
                Model = aiResult.AiModel,
                PromptVersion = aiResult.PromptVersion
            },
            ct);

        _logger.LogInformation(
            "EntityWorker: resolved {Accepted}/{Extracted} entities for document {DocumentId}; linked={Linked}, created={Created}",
            result.AcceptedCount, result.ExtractedCount, documentId, result.LinkedCount, result.CreatedCount);
    }
}
