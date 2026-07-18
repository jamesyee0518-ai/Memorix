using KnowledgeEngine.Application.DTOs;
using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Application.Mapping;
using KnowledgeEngine.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KnowledgeEngine.Infrastructure.Processing;

/// <summary>
/// Service for logging and retrieving document processing step logs.
/// Each log entry records a single step (parse, clean, ai, chunk, index)
/// in the document processing pipeline.
/// </summary>
public class ProcessingLogService : IProcessingLogService
{
    private readonly IAppDbContext _db;
    private readonly ILogger<ProcessingLogService> _logger;

    public ProcessingLogService(IAppDbContext db, ILogger<ProcessingLogService> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task LogAsync(
        string workspaceId,
        Guid? sourceId,
        Guid? documentId,
        string stepName,
        string status,
        string? message = null,
        string? errorCode = null,
        string? errorStack = null,
        int? durationMs = null,
        CancellationToken ct = default)
    {
        try
        {
            var now = DateTime.UtcNow;
            var log = new DocumentProcessingLog
            {
                Id = Guid.NewGuid(),
                WorkspaceId = workspaceId,
                SourceId = sourceId,
                DocumentId = documentId,
                StepName = stepName,
                Status = status,
                Message = message,
                ErrorCode = errorCode,
                ErrorStack = errorStack,
                StartedAt = durationMs.HasValue ? now.AddMilliseconds(-durationMs.Value) : now,
                FinishedAt = now,
                DurationMs = durationMs,
                CreatedAt = now
            };

            _db.DocumentProcessingLogs.Add(log);
            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            // Logging failures should never break the pipeline
            _logger.LogError(ex,
                "Failed to write processing log: workspace={WorkspaceId}, source={SourceId}, document={DocumentId}, step={StepName}, status={Status}",
                workspaceId, sourceId, documentId, stepName, status);
        }
    }

    /// <inheritdoc />
    public async Task<List<ProcessingLogItem>> GetLogsByDocumentAsync(Guid documentId, CancellationToken ct = default)
    {
        var logs = await _db.DocumentProcessingLogs
            .Where(l => l.DocumentId == documentId)
            .OrderByDescending(l => l.CreatedAt)
            .ToListAsync(ct);

        return logs.Select(Mapper.ToProcessingLogItem).ToList();
    }

    /// <inheritdoc />
    public async Task<List<ProcessingLogItem>> GetLogsBySourceAsync(Guid sourceId, CancellationToken ct = default)
    {
        var logs = await _db.DocumentProcessingLogs
            .Where(l => l.SourceId == sourceId)
            .OrderByDescending(l => l.CreatedAt)
            .ToListAsync(ct);

        return logs.Select(Mapper.ToProcessingLogItem).ToList();
    }
}
