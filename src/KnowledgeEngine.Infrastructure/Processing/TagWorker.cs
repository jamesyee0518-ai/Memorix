using System.Text.Json;
using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KnowledgeEngine.Infrastructure.Processing;

/// <summary>
/// On-demand AI tag recommendation worker (Phase 4).
/// Invoked explicitly via the regenerate-tags API action endpoint.
/// </summary>
public class TagWorker : ITagWorker
{
    private readonly IAppDbContext _db;
    private readonly IAISummaryService _aiSummaryService;
    private readonly ILogger<TagWorker> _logger;

    public TagWorker(
        IAppDbContext db,
        IAISummaryService aiSummaryService,
        ILogger<TagWorker> logger)
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
            _logger.LogWarning("TagWorker: Document {DocumentId} not found", documentId);
            return;
        }

        if (string.IsNullOrWhiteSpace(document.ContentText))
        {
            _logger.LogWarning("TagWorker: Document {DocumentId} has no content text", documentId);
            return;
        }

        _logger.LogInformation("TagWorker: Regenerating tags for document {DocumentId}", documentId);

        // Call AI summary service to get tag recommendations
        var aiResult = await _aiSummaryService.SummarizeAsync(
            document.Title,
            document.ContentText,
            document.SourceType ?? "text",
            ct);

        if (aiResult.Tags == null || aiResult.Tags.Count == 0)
        {
            _logger.LogWarning("TagWorker: AI returned no tags for document {DocumentId}", documentId);
            return;
        }

        // Delete existing document_tags for the document
        var existingTags = await _db.DocumentTags
            .Where(dt => dt.DocumentId == documentId)
            .ToListAsync(ct);
        if (existingTags.Count > 0)
        {
            _db.DocumentTags.RemoveRange(existingTags);
            await _db.SaveChangesAsync(ct);
        }

        var workspaceId = "default";
        var now = DateTime.UtcNow;

        foreach (var tagDto in aiResult.Tags)
        {
            if (string.IsNullOrWhiteSpace(tagDto.Name)) continue;

            // §7.6 confidence threshold: skip tags below 0.5 confidence
            if (tagDto.Confidence.HasValue && tagDto.Confidence < 0.5m)
            {
                _logger.LogDebug("TagWorker: Skipping tag '{TagName}' with confidence {Confidence} < 0.5",
                    tagDto.Name, tagDto.Confidence);
                continue;
            }

            var name = tagDto.Name.Trim();
            var type = string.IsNullOrWhiteSpace(tagDto.Type) ? "topic" : tagDto.Type.Trim();
            var normalizedName = name.ToLowerInvariant();

            // Check if tag already exists (dedup: user_id + name + type)
            var tag = await _db.Tags.FirstOrDefaultAsync(
                t => t.UserId == document.UserId && t.Name == name && t.Type == type, ct);

            if (tag == null)
            {
                tag = new Tag
                {
                    Id = Guid.NewGuid(),
                    UserId = document.UserId,
                    Name = name,
                    Type = type,
                    Description = tagDto.Description,
                    CreatedAt = now,
                    // Phase 4 fields
                    WorkspaceId = workspaceId,
                    NormalizedName = normalizedName,
                    DisplayName = name,
                    TagType = type,
                    Source = "ai",
                    UsageCount = 0,
                    IsSystem = false,
                    IsArchived = false,
                    UpdatedAt = now
                };
                _db.Tags.Add(tag);
                await _db.SaveChangesAsync(ct);
            }

            // Create document-tag association with Phase 4 fields
            var alreadyLinked = await _db.DocumentTags.FirstOrDefaultAsync(
                dt => dt.DocumentId == documentId && dt.TagId == tag.Id, ct);

            if (alreadyLinked == null)
            {
                _db.DocumentTags.Add(new DocumentTag
                {
                    DocumentId = documentId,
                    TagId = tag.Id,
                    Source = "ai",
                    Confidence = tagDto.Confidence ?? 0.85m,
                    Reason = tagDto.Reason,
                    IsConfirmed = false,
                    CreatedAt = now
                });

                // Increment usage count
                tag.UsageCount++;
                tag.UpdatedAt = now;
            }
        }

        // Update document's RecommendedTags
        document.RecommendedTags = JsonSerializer.Serialize(aiResult.Tags.Select(t => t.Name).ToList());
        document.UpdatedAt = now;

        // Status writeback: mark tag processing as done
        document.TagStatus = "done";
        document.UpdatedAt = now;

        _logger.LogInformation("TagWorker: Tag processing done for document {DocumentId}, tag_status → done", documentId);

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("TagWorker: Generated {Count} tags for document {DocumentId}",
            aiResult.Tags.Count, documentId);
    }
}
