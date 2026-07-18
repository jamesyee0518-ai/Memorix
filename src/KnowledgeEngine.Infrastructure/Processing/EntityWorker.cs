using System.Text.Json;
using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Domain.Entities;
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
    private readonly ILogger<EntityWorker> _logger;

    public EntityWorker(
        IAppDbContext db,
        IAISummaryService aiSummaryService,
        ILogger<EntityWorker> logger)
    {
        _db = db;
        _aiSummaryService = aiSummaryService;
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

        if (aiResult.Entities == null || aiResult.Entities.Count == 0)
        {
            _logger.LogWarning("EntityWorker: AI returned no entities for document {DocumentId}", documentId);
            return;
        }

        // Delete existing document_entities for the document
        var existingEntities = await _db.DocumentEntities
            .Where(de => de.DocumentId == documentId)
            .ToListAsync(ct);
        if (existingEntities.Count > 0)
        {
            _db.DocumentEntities.RemoveRange(existingEntities);
            await _db.SaveChangesAsync(ct);
        }

        var workspaceId = "default";
        var now = DateTime.UtcNow;

        foreach (var entityDto in aiResult.Entities)
        {
            if (string.IsNullOrWhiteSpace(entityDto.Name)) continue;

            // §8.8 threshold filtering: skip entities below importance 0.4 or confidence 0.6
            if (entityDto.Importance.HasValue && entityDto.Importance < 0.4m)
            {
                _logger.LogDebug("EntityWorker: Skipping entity '{EntityName}' with importance {Importance} < 0.4",
                    entityDto.Name, entityDto.Importance);
                continue;
            }
            if (entityDto.Confidence.HasValue && entityDto.Confidence < 0.6m)
            {
                _logger.LogDebug("EntityWorker: Skipping entity '{EntityName}' with confidence {Confidence} < 0.6",
                    entityDto.Name, entityDto.Confidence);
                continue;
            }

            var name = entityDto.Name.Trim();
            var entityType = string.IsNullOrWhiteSpace(entityDto.EntityType) ? "concept" : entityDto.EntityType.Trim();
            var normalizedName = name.ToLowerInvariant();

            // Check if entity already exists (dedup: user_id + normalized_name + entity_type)
            var entity = await _db.Entities.FirstOrDefaultAsync(
                e => e.UserId == document.UserId && e.NormalizedName == normalizedName && e.EntityType == entityType, ct);

            if (entity == null)
            {
                entity = new Entity
                {
                    Id = Guid.NewGuid(),
                    UserId = document.UserId,
                    Name = name,
                    NormalizedName = normalizedName,
                    EntityType = entityType,
                    Description = entityDto.Description,
                    CreatedAt = now,
                    UpdatedAt = now,
                    // Phase 4 fields
                    WorkspaceId = workspaceId,
                    DisplayName = name,
                    Aliases = entityDto.Aliases != null ? JsonSerializer.Serialize(entityDto.Aliases) : null,
                    Source = "ai",
                    UsageCount = 0,
                    IsVerified = false,
                    IsArchived = false
                };
                _db.Entities.Add(entity);
                await _db.SaveChangesAsync(ct);
            }

            // Create document-entity association with Phase 4 fields
            var alreadyLinked = await _db.DocumentEntities.FirstOrDefaultAsync(
                de => de.DocumentId == documentId && de.EntityId == entity.Id, ct);

            if (alreadyLinked == null)
            {
                _db.DocumentEntities.Add(new DocumentEntity
                {
                    DocumentId = documentId,
                    EntityId = entity.Id,
                    MentionCount = entityDto.MentionCount > 0 ? entityDto.MentionCount : 1,
                    Confidence = entityDto.Confidence ?? 0.8m,
                    Evidence = entityDto.Description,
                    // Phase 4 fields
                    FirstMention = entityDto.FirstMention ?? entityDto.Examples?.FirstOrDefault(),
                    MentionExamples = entityDto.Examples != null ? JsonSerializer.Serialize(entityDto.Examples) : null,
                    Importance = entityDto.Importance ?? 0.5m,
                    Role = entityDto.Role,
                    Sentiment = entityDto.Sentiment,
                    CreatedAt = now
                });

                // Increment usage count
                entity.UsageCount++;
                entity.UpdatedAt = now;
            }
        }

        // Status writeback: mark entity processing as done
        document.EntityStatus = "done";
        document.UpdatedAt = now;
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("EntityWorker: Extracted {Count} entities for document {DocumentId}",
            aiResult.Entities.Count, documentId);
    }
}
