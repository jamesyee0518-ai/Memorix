using System.Net;
using System.Text.Json;
using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KnowledgeEngine.Infrastructure.Processing;

public class DocumentPipeline : IDocumentPipeline
{
    private readonly IAppDbContext _db;
    private readonly SourceProcessorFactory _processorFactory;
    private readonly IContentCleaner _contentCleaner;
    private readonly IAISummaryService _aiSummaryService;
    private readonly IQualityScorer _qualityScorer;
    private readonly IProcessingLogService _logService;
    private readonly IJobQueue _jobQueue;
    private readonly ILogger<DocumentPipeline> _logger;

    public DocumentPipeline(
        IAppDbContext db,
        SourceProcessorFactory processorFactory,
        IContentCleaner contentCleaner,
        IAISummaryService aiSummaryService,
        IQualityScorer qualityScorer,
        IProcessingLogService logService,
        IJobQueue jobQueue,
        ILogger<DocumentPipeline> logger)
    {
        _db = db;
        _processorFactory = processorFactory;
        _contentCleaner = contentCleaner;
        _aiSummaryService = aiSummaryService;
        _qualityScorer = qualityScorer;
        _logService = logService;
        _jobQueue = jobQueue;
        _logger = logger;
    }

    public async Task ProcessSourceAsync(Guid sourceId, Guid userId, CancellationToken ct = default)
    {
        var source = await _db.Sources.FirstOrDefaultAsync(s => s.Id == sourceId && s.UserId == userId, ct);
        if (source == null)
        {
            _logger.LogWarning("Source not found: {SourceId}", sourceId);
            return;
        }

        // Avoid duplicate processing
        var existingDoc = await _db.Documents.FirstOrDefaultAsync(d => d.SourceId == sourceId, ct);
        if (existingDoc != null && existingDoc.AiStatus == "done")
        {
            _logger.LogInformation("Source {SourceId} already has a completed document, skipping", sourceId);
            source.Status = "done";
            source.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            return;
        }

        // Source entity has no WorkspaceId field; use "default" per §8.3.1
        var workspaceId = "default";
        var now = DateTime.UtcNow;
        Guid? documentId = existingDoc?.Id;
        var sourceType = string.IsNullOrWhiteSpace(source.SourceType) ? "text" : source.SourceType;

        // Determine parse step name based on source type (§8.3.1)
        var parseStepName = sourceType.ToLowerInvariant() switch
        {
            "url" => "parse_url",
            "pdf" => "parse_pdf",
            _ => "parse_text"
        };

        // Track current step + error code for the outer catch handler
        var currentStep = "init";
        string? errorCode = null;

        try
        {
            // ================================================================
            // Step 1: Parse content
            // ================================================================
            currentStep = parseStepName;

            // Source state machine (§17.1): queued → fetching → parsing
            source.Status = "fetching";
            source.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);

            source.Status = "parsing";
            source.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);

            // Document parse status → processing
            if (existingDoc != null)
            {
                existingDoc.ParseStatus = "processing";
                existingDoc.UpdatedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync(ct);
            }

            var stepStartTime = DateTime.UtcNow;
            await _logService.LogAsync(workspaceId, sourceId, documentId, parseStepName, "started", ct: ct);
            ParseResult parseResult;
            try
            {
                var processor = _processorFactory.GetProcessor(sourceType);
                if (processor == null)
                {
                    throw new InvalidOperationException($"No processor found for source type: {sourceType}");
                }

                parseResult = await processor.ParseAsync(source, ct);

                if (string.IsNullOrWhiteSpace(parseResult.RawText))
                {
                    throw new InvalidOperationException("No content could be extracted from the source");
                }

                var duration = (int)(DateTime.UtcNow - stepStartTime).TotalMilliseconds;
                await _logService.LogAsync(workspaceId, sourceId, documentId, parseStepName, "success", durationMs: duration, ct: ct);
            }
            catch (Exception ex)
            {
                var duration = (int)(DateTime.UtcNow - stepStartTime).TotalMilliseconds;
                errorCode = MapErrorCode(ex, parseStepName);
                await _logService.LogAsync(workspaceId, sourceId, documentId, parseStepName, "failed", ex.Message, errorCode, ex.StackTrace, duration, ct: ct);
                throw;
            }

            // Document parse status → done
            if (existingDoc != null)
            {
                existingDoc.ParseStatus = "done";
            }

            // ================================================================
            // Step 2: Clean content
            // ================================================================
            currentStep = "clean_content";

            // Source state machine: parsing → cleaning
            source.Status = "cleaning";
            source.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);

            if (existingDoc != null)
            {
                existingDoc.CleanStatus = "processing";
                existingDoc.UpdatedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync(ct);
            }

            stepStartTime = DateTime.UtcNow;
            await _logService.LogAsync(workspaceId, sourceId, documentId, "clean_content", "started", ct: ct);
            CleanResult cleanResult;
            try
            {
                cleanResult = await _contentCleaner.CleanAsync(
                    parseResult.RawText, parseResult.RawHtml, parseResult.Markdown, ct);

                if (string.IsNullOrWhiteSpace(cleanResult.CleanedText))
                {
                    throw new InvalidOperationException("No content could be cleaned from the source");
                }

                var duration = (int)(DateTime.UtcNow - stepStartTime).TotalMilliseconds;
                await _logService.LogAsync(workspaceId, sourceId, documentId, "clean_content", "success", durationMs: duration, ct: ct);
            }
            catch (Exception ex)
            {
                var duration = (int)(DateTime.UtcNow - stepStartTime).TotalMilliseconds;
                errorCode = MapErrorCode(ex, "clean_content");
                await _logService.LogAsync(workspaceId, sourceId, documentId, "clean_content", "failed", ex.Message, errorCode, ex.StackTrace, duration, ct: ct);
                throw;
            }

            var normalizedText = cleanResult.CleanedText;
            var wordCount = CountWords(normalizedText);
            var readingTime = Math.Max(1, wordCount / 250);

            // ================================================================
            // Step 3: Create or update Document record with P3 fields
            // ================================================================
            currentStep = "create_document";

            stepStartTime = DateTime.UtcNow;
            await _logService.LogAsync(workspaceId, sourceId, documentId, "create_document", "started", ct: ct);
            Document document;
            try
            {
                document = existingDoc ?? new Document
                {
                    Id = Guid.NewGuid(),
                    SourceId = sourceId,
                    UserId = userId,
                    TopicId = source.TopicId,
                    Title = parseResult.Title ?? source.Title ?? "Untitled",
                    CreatedAt = now,
                    UpdatedAt = now
                };

                if (existingDoc == null)
                {
                    _db.Documents.Add(document);
                }
                documentId = document.Id;

                // Core content fields
                document.ContentMarkdown = cleanResult.CleanedMarkdown;
                document.ContentText = normalizedText;
                document.Title = parseResult.Title ?? source.Title ?? document.Title;
                document.WordCount = wordCount;
                document.ReadingTimeMinutes = readingTime;
                document.Language = DetectLanguage(normalizedText);

                // P3 source metadata fields
                document.SourceType = source.SourceType;
                document.SourceUrl = source.Url;
                document.SourceDomain = parseResult.Domain ?? source.Domain;
                document.Author = parseResult.Author ?? source.Author;
                document.PublishedAt = parseResult.PublishedAt ?? source.PublishedAt;
                document.ParserName = parseResult.ParserName;
                document.ParserVersion = parseResult.ParserVersion;
                document.CleanerVersion = cleanResult.CleanerVersion;

                // P3 status fields
                document.ParseStatus = "done";
                document.CleanStatus = "done";
                document.AiStatus = "processing";
                document.TagStatus = "pending";
                document.EntityStatus = "pending";
                document.EmbeddingStatus = "pending";
                document.UpdatedAt = now;

                // Source state machine: cleaning → document_created
                source.Status = "document_created";
                source.UpdatedAt = now;
                await _db.SaveChangesAsync(ct);

                var duration = (int)(DateTime.UtcNow - stepStartTime).TotalMilliseconds;
                await _logService.LogAsync(workspaceId, sourceId, documentId, "create_document", "success", durationMs: duration, ct: ct);
            }
            catch (Exception ex)
            {
                var duration = (int)(DateTime.UtcNow - stepStartTime).TotalMilliseconds;
                errorCode = MapErrorCode(ex, "create_document");
                await _logService.LogAsync(workspaceId, sourceId, documentId, "create_document", "failed", ex.Message, errorCode, ex.StackTrace, duration, ct: ct);
                throw;
            }

            // ================================================================
            // Step 4: AI Summarize
            // ================================================================
            currentStep = "ai_summarize";

            // Source state machine: document_created → ai_processing
            source.Status = "ai_processing";
            source.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);

            // Document AI status → processing
            document.AiStatus = "processing";
            document.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);

            // Create AiJob record
            var aiJob = new AiJob
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                JobType = "document_analysis",
                TargetType = "source",
                TargetId = sourceId,
                Status = "running",
                StartedAt = now,
                CreatedAt = now
            };
            _db.AiJobs.Add(aiJob);
            await _db.SaveChangesAsync(ct);

            stepStartTime = DateTime.UtcNow;
            await _logService.LogAsync(workspaceId, sourceId, documentId, "ai_summarize", "started", ct: ct);
            AiSummaryResult aiResult;
            try
            {
                aiResult = await _aiSummaryService.SummarizeAsync(
                    document.Title, normalizedText, source.SourceType, ct);

                var duration = (int)(DateTime.UtcNow - stepStartTime).TotalMilliseconds;
                await _logService.LogAsync(workspaceId, sourceId, documentId, "ai_summarize", "success", durationMs: duration, ct: ct);
            }
            catch (Exception ex)
            {
                var duration = (int)(DateTime.UtcNow - stepStartTime).TotalMilliseconds;
                errorCode = MapErrorCode(ex, "ai_summarize");
                await _logService.LogAsync(workspaceId, sourceId, documentId, "ai_summarize", "failed", ex.Message, errorCode, ex.StackTrace, duration, ct: ct);
                throw;
            }

            // Update AiJob with token usage
            aiJob.InputTokens = aiResult.InputTokens;
            aiJob.OutputTokens = aiResult.OutputTokens;
            aiJob.Model = aiResult.AiModel;
            aiJob.PromptVersion = aiResult.PromptVersion;
            aiJob.CostEstimate = EstimateCost(aiResult.InputTokens ?? 0, aiResult.OutputTokens ?? 0);

            // Update Document with AI analysis results
            document.Summary = aiResult.Summary;
            document.OneSentenceConclusion = aiResult.OneSentenceConclusion;
            // KeyPoints is now a pre-serialized JSON string (not List<string>)
            document.KeyPoints = aiResult.KeyPoints;
            document.BusinessSignals = SerializeList(aiResult.BusinessSignals);
            document.TechnicalSignals = SerializeList(aiResult.TechnicalSignals);
            document.Risks = SerializeList(aiResult.Risks);
            document.Opportunities = SerializeList(aiResult.Opportunities);
            document.ReusableMaterials = SerializeList(aiResult.ReusableMaterials);
            document.ValueScore = aiResult.ValueScore;
            document.QualityScore = aiResult.QualityScore;
            document.ValueScoreReason = aiResult.ValueScoreReason;
            document.ShouldDeepProcess = aiResult.ShouldDeepProcess;
            document.RecommendedTags = SerializeList(aiResult.RecommendedTags);
            document.AiRawOutput = aiResult.AiRawOutput;
            document.AiModel = aiResult.AiModel;
            document.PromptVersion = aiResult.PromptVersion;

            // ================================================================
            // Step 5: Quality Score (system rules override AI score)
            // ================================================================
            currentStep = "quality_score";

            stepStartTime = DateTime.UtcNow;
            await _logService.LogAsync(workspaceId, sourceId, documentId, "quality_score", "started", ct: ct);
            try
            {
                var (systemQualityScore, finalValueScore, qualityReason) = _qualityScorer.CalculateScores(
                    document.ContentText ?? "", document.Title, document.WordCount ?? 0,
                    document.SourceType ?? "text", aiResult.ValueScore);

                // System quality score overrides AI quality score
                document.QualityScore = systemQualityScore;

                // If AI didn't provide value_score (or system estimated differently), use system estimate
                if (finalValueScore != aiResult.ValueScore)
                    document.ValueScore = finalValueScore;

                // Store quality reason if AI didn't provide a value_score_reason
                if (string.IsNullOrWhiteSpace(document.ValueScoreReason))
                    document.ValueScoreReason = qualityReason;

                var duration = (int)(DateTime.UtcNow - stepStartTime).TotalMilliseconds;
                await _logService.LogAsync(workspaceId, sourceId, documentId, "quality_score", "success",
                    message: qualityReason, durationMs: duration, ct: ct);
            }
            catch (Exception ex)
            {
                var duration = (int)(DateTime.UtcNow - stepStartTime).TotalMilliseconds;
                errorCode = MapErrorCode(ex, "quality_score");
                await _logService.LogAsync(workspaceId, sourceId, documentId, "quality_score", "failed",
                    ex.Message, errorCode, ex.StackTrace, duration, ct: ct);
                // Quality scoring failure should not fail the entire pipeline
                _logger.LogWarning(ex, "Quality scoring failed for document {DocumentId}, continuing", document.Id);
            }

            // Document AI status → done
            document.AiStatus = "done";
            document.ProcessedAt = DateTime.UtcNow;
            document.UpdatedAt = DateTime.UtcNow;

            // ================================================================
            // Step 6: Create Tags (dedup: user_id + name + type)
            // ================================================================
            if (aiResult.Tags != null && aiResult.Tags.Count > 0)
            {
                await CreateTagsAsync(document.Id, userId, aiResult.Tags, ct);
            }
            document.TagStatus = "done";

            // ================================================================
            // Step 7: Create Entities (dedup: user_id + normalized_name + entity_type)
            // ================================================================
            if (aiResult.Entities != null && aiResult.Entities.Count > 0)
            {
                await CreateEntitiesAsync(document.Id, userId, aiResult.Entities, ct);
            }
            document.EntityStatus = "done";

            // ================================================================
            // Step 8: Finalize statuses
            // ================================================================
            // Source state machine: ai_processing → done
            source.Status = "done";
            source.UpdatedAt = DateTime.UtcNow;

            aiJob.Status = "done";
            aiJob.FinishedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync(ct);

            _logger.LogInformation("Document pipeline completed for source {SourceId}, document {DocumentId}",
                sourceId, document.Id);

            // ================================================================
            // Step 9: Enqueue Phase 4 jobs (tags, entities, chunking can run in parallel)
            // ================================================================
            try
            {
                var wsId = "default";
                var docIdStr = document.Id.ToString();

                await _jobQueue.EnqueueAsync(new CreateJobInput
                {
                    WorkspaceId = wsId,
                    JobType = "tagging",
                    TargetType = "document",
                    TargetId = docIdStr
                }, ct);

                await _jobQueue.EnqueueAsync(new CreateJobInput
                {
                    WorkspaceId = wsId,
                    JobType = "entity_extraction",
                    TargetType = "document",
                    TargetId = docIdStr
                }, ct);

                await _jobQueue.EnqueueAsync(new CreateJobInput
                {
                    WorkspaceId = wsId,
                    JobType = "chunking",
                    TargetType = "document",
                    TargetId = docIdStr
                }, ct);

                _logger.LogInformation("Enqueued Phase 4 jobs (tagging, entity_extraction, chunking) for document {DocumentId}", document.Id);
            }
            catch (Exception jobEx)
            {
                _logger.LogWarning(jobEx, "Failed to enqueue Phase 4 jobs for document {DocumentId}, pipeline continues", document.Id);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Document pipeline failed for source {SourceId} at step {Step}", sourceId, currentStep);

            var failTime = DateTime.UtcNow;
            source.Status = "failed";
            source.ErrorMessage = errorCode != null
                ? $"[{errorCode}] {Truncate(ex.Message, 1900)}"
                : Truncate(ex.Message, 2000);
            source.RetryCount += 1;
            source.UpdatedAt = failTime;

            // Update document status based on which step failed
            var doc = await _db.Documents.FirstOrDefaultAsync(d => d.SourceId == sourceId, ct);
            if (doc != null)
            {
                var formattedError = errorCode != null
                    ? $"[{errorCode}] {Truncate(ex.Message, 1900)}"
                    : Truncate(ex.Message, 2000);

                // Set the appropriate status field to "failed" based on current step
                if (IsParseStep(currentStep))
                {
                    doc.ParseStatus = "failed";
                    doc.AiErrorMessage = formattedError;
                }
                else if (currentStep == "clean_content")
                {
                    doc.CleanStatus = "failed";
                    doc.AiErrorMessage = formattedError;
                }
                else if (currentStep is "ai_summarize" or "quality_score")
                {
                    doc.AiStatus = "failed";
                    doc.AiErrorMessage = formattedError;
                }
                else
                {
                    // create_document or other — set AI status as catch-all
                    doc.AiStatus = "failed";
                    doc.AiErrorMessage = formattedError;
                }

                // Also mark Phase 4 statuses as failed if the AI step failed
                if (currentStep is "ai_summarize" or "quality_score")
                {
                    doc.TagStatus = "failed";
                    doc.EntityStatus = "failed";
                }
                doc.UpdatedAt = failTime;
            }

            // Update AiJob status
            var job = await _db.AiJobs
                .Where(j => j.TargetId == sourceId && j.TargetType == "source")
                .OrderByDescending(j => j.CreatedAt)
                .FirstOrDefaultAsync(ct);
            if (job != null)
            {
                job.Status = "failed";
                job.ErrorMessage = Truncate(ex.Message, 2000);
                job.RetryCount += 1;
                job.FinishedAt = failTime;
            }

            try
            {
                await _db.SaveChangesAsync(ct);
            }
            catch (Exception saveEx)
            {
                _logger.LogError(saveEx, "Failed to save error state for source {SourceId}", sourceId);
            }

            // Auto-retry if retry_count < 3
            if (source.RetryCount < 3)
            {
                _logger.LogInformation("Scheduling retry #{RetryCount} for source {SourceId}",
                    source.RetryCount, sourceId);
                source.Status = "queued";
                source.UpdatedAt = DateTime.UtcNow;
                try
                {
                    await _db.SaveChangesAsync(ct);
                }
                catch (Exception retrySaveEx)
                {
                    _logger.LogError(retrySaveEx, "Failed to schedule retry for source {SourceId}", sourceId);
                }
            }
        }
    }

    /// <summary>
    /// Maps an exception to a Phase 3 processing error code (§24) based on
    /// the exception type and the step in which it occurred.
    /// </summary>
    private static string MapErrorCode(Exception ex, string stepName)
    {
        // AI step errors should map to AI-specific codes
        if (stepName is "ai_summarize")
            return MapAiErrorCode(ex);

        // Fetch / network errors (primarily during parse_url / parse_pdf)
        if (ex is HttpRequestException hre)
        {
            return hre.StatusCode switch
            {
                HttpStatusCode.NotFound => ProcessingErrorCodes.FetchNotFound,
                HttpStatusCode.Forbidden => ProcessingErrorCodes.FetchForbidden,
                HttpStatusCode.RequestEntityTooLarge => ProcessingErrorCodes.FetchTooLarge,
                _ => ProcessingErrorCodes.FetchTimeout
            };
        }

        if (ex is TaskCanceledException)
            return ProcessingErrorCodes.FetchTimeout;

        // Parse errors
        if (ex is InvalidOperationException ioe)
        {
            var msg = ioe.Message ?? string.Empty;
            if (msg.Contains("No content", StringComparison.OrdinalIgnoreCase))
                return ProcessingErrorCodes.ParseEmptyContent;
            if (msg.Contains("No processor", StringComparison.OrdinalIgnoreCase))
                return ProcessingErrorCodes.ParseUnsupportedType;
            if (msg.Contains("scanned", StringComparison.OrdinalIgnoreCase))
                return ProcessingErrorCodes.ParsePdfScanned;
            if (msg.Contains("too large", StringComparison.OrdinalIgnoreCase))
                return ProcessingErrorCodes.ParsePdfTooLarge;
        }

        // Step-specific fallbacks
        return stepName switch
        {
            "clean_content" => ProcessingErrorCodes.CleanFailed,
            "create_document" => ProcessingErrorCodes.DocumentCreateFailed,
            "quality_score" => ProcessingErrorCodes.UnknownError,
            _ => ProcessingErrorCodes.UnknownError
        };
    }

    /// <summary>
    /// Maps an AI-related exception to the appropriate error code.
    /// </summary>
    private static string MapAiErrorCode(Exception ex)
    {
        var msg = ex.Message ?? string.Empty;
        if (ex is TimeoutException || msg.Contains("timeout", StringComparison.OrdinalIgnoreCase))
            return ProcessingErrorCodes.AiTimeout;
        if (ex is TaskCanceledException)
            return ProcessingErrorCodes.AiTimeout;
        if (msg.Contains("unavailable", StringComparison.OrdinalIgnoreCase) ||
            msg.Contains("service", StringComparison.OrdinalIgnoreCase))
            return ProcessingErrorCodes.AiModelUnavailable;
        if (msg.Contains("json", StringComparison.OrdinalIgnoreCase) ||
            msg.Contains("deserialize", StringComparison.OrdinalIgnoreCase) ||
            msg.Contains("parse", StringComparison.OrdinalIgnoreCase))
            return ProcessingErrorCodes.AiInvalidJson;
        if (msg.Contains("too long", StringComparison.OrdinalIgnoreCase) ||
            msg.Contains("content length", StringComparison.OrdinalIgnoreCase) ||
            msg.Contains("token limit", StringComparison.OrdinalIgnoreCase))
            return ProcessingErrorCodes.AiContentTooLong;
        return ProcessingErrorCodes.AiModelUnavailable;
    }

    /// <summary>
    /// Returns true if the step name is a parse step.
    /// </summary>
    private static bool IsParseStep(string stepName)
        => stepName is "parse_url" or "parse_pdf" or "parse_text";

    private async Task CreateTagsAsync(Guid documentId, Guid userId, List<TagResult> tags, CancellationToken ct)
    {
        var now = DateTime.UtcNow;

        foreach (var tagDto in tags)
        {
            if (string.IsNullOrWhiteSpace(tagDto.Name)) continue;

            var name = tagDto.Name.Trim();
            var type = string.IsNullOrWhiteSpace(tagDto.Type) ? "topic" : tagDto.Type.Trim();
            var normalizedName = name.ToLowerInvariant();

            // Check if tag already exists (dedup: user_id + name + type)
            var tag = await _db.Tags.FirstOrDefaultAsync(
                t => t.UserId == userId && t.Name == name && t.Type == type, ct);

            if (tag == null)
            {
                tag = new Tag
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    Name = name,
                    Type = type,
                    Description = tagDto.Description,
                    CreatedAt = now
                };
                _db.Tags.Add(tag);
                await _db.SaveChangesAsync(ct);
            }

            // Check if document-tag association already exists
            var existingAssoc = await _db.DocumentTags.FirstOrDefaultAsync(
                dt => dt.DocumentId == documentId && dt.TagId == tag.Id, ct);

            if (existingAssoc == null)
            {
                _db.DocumentTags.Add(new DocumentTag
                {
                    DocumentId = documentId,
                    TagId = tag.Id,
                    Source = "ai",
                    Confidence = tagDto.Confidence ?? 0.85m,
                    Reason = tagDto.Reason,
                    CreatedAt = now
                });
            }
        }

        await _db.SaveChangesAsync(ct);
    }

    private async Task CreateEntitiesAsync(Guid documentId, Guid userId, List<EntityResult> entities, CancellationToken ct)
    {
        var now = DateTime.UtcNow;

        foreach (var entityDto in entities)
        {
            if (string.IsNullOrWhiteSpace(entityDto.Name)) continue;

            var name = entityDto.Name.Trim();
            var entityType = string.IsNullOrWhiteSpace(entityDto.EntityType) ? "concept" : entityDto.EntityType.Trim();
            var normalizedName = name.ToLowerInvariant();

            // Check if entity already exists (dedup: user_id + normalized_name + entity_type)
            var entity = await _db.Entities.FirstOrDefaultAsync(
                e => e.UserId == userId && e.NormalizedName == normalizedName && e.EntityType == entityType, ct);

            if (entity == null)
            {
                entity = new Entity
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    Name = name,
                    NormalizedName = normalizedName,
                    EntityType = entityType,
                    Description = entityDto.Description,
                    CreatedAt = now,
                    UpdatedAt = now
                };
                _db.Entities.Add(entity);
                await _db.SaveChangesAsync(ct);
            }

            // Check if document-entity association already exists
            var existingAssoc = await _db.DocumentEntities.FirstOrDefaultAsync(
                de => de.DocumentId == documentId && de.EntityId == entity.Id, ct);

            if (existingAssoc == null)
            {
                _db.DocumentEntities.Add(new DocumentEntity
                {
                    DocumentId = documentId,
                    EntityId = entity.Id,
                    MentionCount = entityDto.MentionCount > 0 ? entityDto.MentionCount : 1,
                    Confidence = entityDto.Confidence ?? 0.8m,
                    Importance = entityDto.Importance ?? 0.5m,
                    Evidence = entityDto.Description,
                    CreatedAt = now
                });
            }
        }

        await _db.SaveChangesAsync(ct);
    }

    private static int CountWords(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;

        // For CJK text, count characters; for Latin text, count words
        var cjkCount = 0;
        var latinText = new System.Text.StringBuilder();

        foreach (char c in text)
        {
            if (c >= 0x4E00 && c <= 0x9FFF || c >= 0x3400 && c <= 0x4DBF)
            {
                cjkCount++;
            }
            else
            {
                latinText.Append(c);
            }
        }

        var latinWords = latinText.ToString().Split(new[] { ' ', '\t', '\n', '\r' },
            StringSplitOptions.RemoveEmptyEntries);

        return cjkCount + latinWords.Length;
    }

    private static string? DetectLanguage(string text)
    {
        if (string.IsNullOrEmpty(text)) return null;

        var cjkCount = 0;
        foreach (char c in text)
        {
            if (c >= 0x4E00 && c <= 0x9FFF || c >= 0x3400 && c <= 0x4DBF)
            {
                cjkCount++;
            }
        }

        if (cjkCount > text.Length * 0.1)
        {
            return "zh";
        }

        return "en";
    }

    private static string SerializeList(List<string>? list)
    {
        if (list == null || list.Count == 0)
        {
            return "[]";
        }
        return JsonSerializer.Serialize(list);
    }

    private static decimal EstimateCost(int inputTokens, int outputTokens)
    {
        // Rough cost estimate: gpt-4o-mini pricing
        // Input: $0.150 / 1M tokens, Output: $0.600 / 1M tokens
        return Math.Round((inputTokens * 0.150m + outputTokens * 0.600m) / 1_000_000m, 6);
    }

    private static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        return value.Length <= maxLength ? value : value.Substring(0, maxLength);
    }
}
