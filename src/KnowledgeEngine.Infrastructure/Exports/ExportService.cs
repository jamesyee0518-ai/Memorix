using System.Text.Json;
using System.Text.Json.Serialization;
using KnowledgeEngine.Application.DTOs;
using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Application.Mapping;
using KnowledgeEngine.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KnowledgeEngine.Infrastructure.Exports;

public class ExportService : IExportService
{
    private readonly IAppDbContext _db;
    private readonly IFileStorageProvider _fileStorage;
    private readonly ILogger<ExportService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public ExportService(
        IAppDbContext db,
        IFileStorageProvider fileStorage,
        ILogger<ExportService> logger)
    {
        _db = db;
        _fileStorage = fileStorage;
        _logger = logger;
    }

    // ===== Export Document Markdown =====

    public async Task<ApiResponse<ExportJobResponse>> ExportDocumentMarkdownAsync(
        Guid userId,
        ExportDocumentRequest request,
        CancellationToken ct = default)
    {
        try
        {
            // Validate document exists
            var doc = await _db.Documents
                .FirstOrDefaultAsync(d => d.Id == request.DocumentId && d.UserId == userId, ct);

            if (doc == null)
            {
                return ApiResponse<ExportJobResponse>.Fail("document_not_found", "Document not found");
            }

            var now = DateTime.UtcNow;
            var parameters = JsonSerializer.Serialize(new
            {
                include_ai_summary = request.IncludeAiSummary,
                include_metadata = request.IncludeMetadata
            }, JsonOptions);

            var job = new ExportJob
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                TopicId = doc.TopicId,
                ExportType = "markdown",
                TargetType = "document",
                TargetId = request.DocumentId,
                Status = "pending",
                Params = parameters,
                CreatedAt = now
            };

            _db.ExportJobs.Add(job);
            await _db.SaveChangesAsync(ct);

            return ApiResponse<ExportJobResponse>.Ok(Mapper.ToExportJobResponse(job));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create document export job");
            return ApiResponse<ExportJobResponse>.Fail("export_document_error", ex.Message);
        }
    }

    // ===== Export Report Markdown =====

    public async Task<ApiResponse<ExportJobResponse>> ExportReportMarkdownAsync(
        Guid userId,
        ExportReportRequest request,
        CancellationToken ct = default)
    {
        try
        {
            var report = await _db.Reports
                .FirstOrDefaultAsync(r => r.Id == request.ReportId && r.UserId == userId, ct);

            if (report == null)
            {
                return ApiResponse<ExportJobResponse>.Fail("report_not_found", "Report not found");
            }

            var now = DateTime.UtcNow;

            var job = new ExportJob
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                TopicId = report.TopicId,
                ExportType = "markdown",
                TargetType = "report",
                TargetId = request.ReportId,
                Status = "pending",
                Params = null,
                CreatedAt = now
            };

            _db.ExportJobs.Add(job);
            await _db.SaveChangesAsync(ct);

            return ApiResponse<ExportJobResponse>.Ok(Mapper.ToExportJobResponse(job));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create report export job");
            return ApiResponse<ExportJobResponse>.Fail("export_report_error", ex.Message);
        }
    }

    // ===== Export Report JSON =====

    public async Task<ApiResponse<ExportJobResponse>> ExportReportJsonAsync(
        Guid userId,
        ExportReportJsonRequest request,
        CancellationToken ct = default)
    {
        try
        {
            var report = await _db.Reports
                .FirstOrDefaultAsync(r => r.Id == request.ReportId && r.UserId == userId, ct);

            if (report == null)
            {
                return ApiResponse<ExportJobResponse>.Fail("report_not_found", "Report not found");
            }

            var now = DateTime.UtcNow;

            var job = new ExportJob
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                TopicId = report.TopicId,
                ExportType = "json",
                TargetType = "report",
                TargetId = request.ReportId,
                Status = "pending",
                Params = null,
                CreatedAt = now
            };

            _db.ExportJobs.Add(job);
            await _db.SaveChangesAsync(ct);

            return ApiResponse<ExportJobResponse>.Ok(Mapper.ToExportJobResponse(job));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create report JSON export job");
            return ApiResponse<ExportJobResponse>.Fail("export_report_json_error", ex.Message);
        }
    }

    // ===== Export Topic Obsidian =====

    public async Task<ApiResponse<ExportJobResponse>> ExportTopicObsidianAsync(
        Guid userId,
        ExportTopicRequest request,
        CancellationToken ct = default)
    {
        try
        {
            var topic = await _db.Topics
                .FirstOrDefaultAsync(t => t.Id == request.TopicId && t.UserId == userId, ct);

            if (topic == null)
            {
                return ApiResponse<ExportJobResponse>.Fail("topic_not_found", "Topic not found");
            }

            var now = DateTime.UtcNow;
            var parameters = JsonSerializer.Serialize(new
            {
                include_documents = request.IncludeDocuments,
                include_reports = request.IncludeReports,
                include_ai_summary = request.IncludeAiSummary
            }, JsonOptions);

            var job = new ExportJob
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                TopicId = request.TopicId,
                ExportType = "obsidian",
                TargetType = "topic",
                TargetId = request.TopicId,
                Status = "pending",
                Params = parameters,
                CreatedAt = now
            };

            _db.ExportJobs.Add(job);
            await _db.SaveChangesAsync(ct);

            return ApiResponse<ExportJobResponse>.Ok(Mapper.ToExportJobResponse(job));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create topic export job");
            return ApiResponse<ExportJobResponse>.Fail("export_topic_error", ex.Message);
        }
    }

    // ===== Export Search JSON =====

    public async Task<ApiResponse<ExportJobResponse>> ExportSearchJsonAsync(
        Guid userId,
        ExportSearchRequest request,
        CancellationToken ct = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.Query))
            {
                return ApiResponse<ExportJobResponse>.Fail("invalid_request", "Query is required");
            }

            var now = DateTime.UtcNow;
            var parameters = JsonSerializer.Serialize(new
            {
                topic_id = request.TopicId,
                query = request.Query,
                filters = request.Filters
            }, JsonOptions);

            var job = new ExportJob
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                TopicId = request.TopicId,
                ExportType = "json",
                TargetType = "search",
                TargetId = null,
                Status = "pending",
                Params = parameters,
                CreatedAt = now
            };

            _db.ExportJobs.Add(job);
            await _db.SaveChangesAsync(ct);

            return ApiResponse<ExportJobResponse>.Ok(Mapper.ToExportJobResponse(job));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create search export job");
            return ApiResponse<ExportJobResponse>.Fail("export_search_error", ex.Message);
        }
    }

    // ===== Get Export Job =====

    public async Task<ApiResponse<ExportJobDetail>> GetExportJobAsync(
        Guid userId,
        Guid jobId,
        CancellationToken ct = default)
    {
        try
        {
            var job = await _db.ExportJobs
                .FirstOrDefaultAsync(j => j.Id == jobId && j.UserId == userId, ct);

            if (job == null)
            {
                return ApiResponse<ExportJobDetail>.Fail("export_job_not_found", "Export job not found");
            }

            string? downloadUrl = null;

            // If file_id is set, generate a presigned download URL
            if (job.FileId.HasValue)
            {
                var file = await _db.Files
                    .FirstOrDefaultAsync(f => f.Id == job.FileId.Value && f.WorkspaceId == userId, ct);

                if (file != null)
                {
                    try
                    {
                        downloadUrl = await _fileStorage.GetPresignedDownloadUrlAsync(
                            file.Bucket, file.ObjectKey, 3600, ct);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to generate presigned URL for file {FileId}", file.Id);
                    }
                }
            }

            return ApiResponse<ExportJobDetail>.Ok(Mapper.ToExportJobDetail(job, downloadUrl));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get export job {JobId}", jobId);
            return ApiResponse<ExportJobDetail>.Fail("get_export_job_error", ex.Message);
        }
    }
}
