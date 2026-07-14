using System.Text.Json;
using System.Text.Json.Serialization;
using KnowledgeEngine.Application.DTOs;
using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Application.Mapping;
using KnowledgeEngine.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KnowledgeEngine.Infrastructure.Reports;

public class ReportService : IReportService
{
    private readonly IAppDbContext _db;
    private readonly ILogger<ReportService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public ReportService(
        IAppDbContext db,
        ILogger<ReportService> logger)
    {
        _db = db;
        _logger = logger;
    }

    // ===== Create Daily Report =====

    public async Task<ApiResponse<CreateReportResponse>> CreateDailyReportAsync(
        Guid userId,
        CreateDailyReportRequest request,
        CancellationToken ct = default)
    {
        try
        {
            var now = DateTime.UtcNow;
            var date = request.Date?.Date ?? now.Date;

            var inputParams = JsonSerializer.Serialize(new
            {
                topic_id = request.TopicId,
                date = date
            }, JsonOptions);

            var job = new ReportJob
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                TopicId = request.TopicId,
                ReportType = "daily",
                Status = "pending",
                InputParams = inputParams,
                CreatedAt = now,
                UpdatedAt = now
            };

            _db.ReportJobs.Add(job);
            await _db.SaveChangesAsync(ct);

            return ApiResponse<CreateReportResponse>.Ok(new CreateReportResponse
            {
                ReportJobId = job.Id,
                Status = "pending"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create daily report job");
            return ApiResponse<CreateReportResponse>.Fail("create_daily_report_error", ex.Message);
        }
    }

    // ===== Create Weekly Report =====

    public async Task<ApiResponse<CreateReportResponse>> CreateWeeklyReportAsync(
        Guid userId,
        CreateWeeklyReportRequest request,
        CancellationToken ct = default)
    {
        try
        {
            var now = DateTime.UtcNow;
            var endDate = request.EndDate?.Date ?? now.Date;
            var startDate = request.StartDate?.Date ?? endDate.AddDays(-6);

            var inputParams = JsonSerializer.Serialize(new
            {
                topic_id = request.TopicId,
                start_date = startDate,
                end_date = endDate
            }, JsonOptions);

            var job = new ReportJob
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                TopicId = request.TopicId,
                ReportType = "weekly",
                Status = "pending",
                InputParams = inputParams,
                CreatedAt = now,
                UpdatedAt = now
            };

            _db.ReportJobs.Add(job);
            await _db.SaveChangesAsync(ct);

            return ApiResponse<CreateReportResponse>.Ok(new CreateReportResponse
            {
                ReportJobId = job.Id,
                Status = "pending"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create weekly report job");
            return ApiResponse<CreateReportResponse>.Fail("create_weekly_report_error", ex.Message);
        }
    }

    // ===== Create Topic Report =====

    public async Task<ApiResponse<CreateReportResponse>> CreateTopicReportAsync(
        Guid userId,
        CreateTopicReportRequest request,
        CancellationToken ct = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.Question))
            {
                return ApiResponse<CreateReportResponse>.Fail("invalid_request", "Question is required");
            }

            var now = DateTime.UtcNow;

            var inputParams = JsonSerializer.Serialize(new
            {
                topic_id = request.TopicId,
                title = request.Title,
                question = request.Question,
                date_from = request.DateFrom,
                date_to = request.DateTo,
                min_value_score = request.MinValueScore,
                depth = request.Depth ?? "standard"
            }, JsonOptions);

            var job = new ReportJob
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                TopicId = request.TopicId,
                ReportType = "topic",
                Status = "pending",
                InputParams = inputParams,
                CreatedAt = now,
                UpdatedAt = now
            };

            _db.ReportJobs.Add(job);
            await _db.SaveChangesAsync(ct);

            return ApiResponse<CreateReportResponse>.Ok(new CreateReportResponse
            {
                ReportJobId = job.Id,
                Status = "pending"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create topic report job");
            return ApiResponse<CreateReportResponse>.Fail("create_topic_report_error", ex.Message);
        }
    }

    // ===== Get All Reports =====

    public async Task<ApiResponse<PagedResult<ReportListItem>>> GetAllAsync(
        Guid userId,
        Guid? topicId,
        string? reportType,
        CancellationToken ct = default)
    {
        try
        {
            var query = _db.Reports.Where(r => r.UserId == userId);

            if (topicId.HasValue)
            {
                query = query.Where(r => r.TopicId == topicId);
            }

            if (!string.IsNullOrEmpty(reportType))
            {
                query = query.Where(r => r.ReportType == reportType);
            }

            var total = await query.CountAsync(ct);
            var reports = await query
                .OrderByDescending(r => r.CreatedAt)
                .Take(100)
                .ToListAsync(ct);

            var items = reports.Select(Mapper.ToReportListItem).ToList();

            var result = new PagedResult<ReportListItem>
            {
                Items = items,
                Total = total,
                Page = 1,
                PageSize = 100
            };

            return ApiResponse<PagedResult<ReportListItem>>.Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get reports");
            return ApiResponse<PagedResult<ReportListItem>>.Fail("get_reports_error", ex.Message);
        }
    }

    // ===== Get Report By Id =====

    public async Task<ApiResponse<ReportDetail>> GetByIdAsync(
        Guid userId,
        Guid reportId,
        CancellationToken ct = default)
    {
        try
        {
            var report = await _db.Reports
                .FirstOrDefaultAsync(r => r.Id == reportId && r.UserId == userId, ct);

            if (report == null)
            {
                return ApiResponse<ReportDetail>.Fail("report_not_found", "Report not found");
            }

            // Parse JSONB fields
            var sourceDocumentIds = ParseGuidList(report.SourceDocumentIds);
            var sourceChunkIds = ParseGuidList(report.SourceChunkIds);
            var citations = ParseCitations(report.Citations);

            return ApiResponse<ReportDetail>.Ok(
                Mapper.ToReportDetail(report, sourceDocumentIds, sourceChunkIds, citations));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get report {ReportId}", reportId);
            return ApiResponse<ReportDetail>.Fail("get_report_error", ex.Message);
        }
    }

    // ===== Regenerate Report =====

    public async Task<ApiResponse<CreateReportResponse>> RegenerateAsync(
        Guid userId,
        Guid reportId,
        CancellationToken ct = default)
    {
        try
        {
            var report = await _db.Reports
                .FirstOrDefaultAsync(r => r.Id == reportId && r.UserId == userId, ct);

            if (report == null)
            {
                return ApiResponse<CreateReportResponse>.Fail("report_not_found", "Report not found");
            }

            var now = DateTime.UtcNow;

            // Reconstruct input params based on the existing report
            object inputParams = report.ReportType switch
            {
                "daily" => new
                {
                    topic_id = report.TopicId,
                    date = report.StartDate ?? now.Date
                },
                "weekly" => new
                {
                    topic_id = report.TopicId,
                    start_date = report.StartDate,
                    end_date = report.EndDate
                },
                "topic" => new
                {
                    topic_id = report.TopicId,
                    title = report.Title,
                    question = report.Query ?? "",
                    date_from = report.StartDate,
                    date_to = report.EndDate,
                    min_value_score = (int?)null,
                    depth = "standard"
                },
                _ => new { topic_id = report.TopicId }
            };

            var inputParamsJson = JsonSerializer.Serialize(inputParams, JsonOptions);

            var job = new ReportJob
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                TopicId = report.TopicId,
                ReportType = report.ReportType,
                Status = "pending",
                InputParams = inputParamsJson,
                CreatedAt = now,
                UpdatedAt = now
            };

            _db.ReportJobs.Add(job);
            await _db.SaveChangesAsync(ct);

            return ApiResponse<CreateReportResponse>.Ok(new CreateReportResponse
            {
                ReportJobId = job.Id,
                Status = "pending"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to regenerate report {ReportId}", reportId);
            return ApiResponse<CreateReportResponse>.Fail("regenerate_report_error", ex.Message);
        }
    }

    // ===== Update Report =====

    public async Task<ApiResponse<ReportDetail>> UpdateAsync(
        Guid userId,
        Guid reportId,
        UpdateReportRequest request,
        CancellationToken ct = default)
    {
        try
        {
            var report = await _db.Reports
                .FirstOrDefaultAsync(r => r.Id == reportId && r.UserId == userId, ct);

            if (report == null)
            {
                return ApiResponse<ReportDetail>.Fail("report_not_found", "Report not found");
            }

            if (!string.IsNullOrEmpty(request.Title))
            {
                report.Title = request.Title;
            }

            if (!string.IsNullOrEmpty(request.ContentMarkdown))
            {
                report.ContentMarkdown = request.ContentMarkdown;
            }

            if (request.TopicId.HasValue)
            {
                report.TopicId = request.TopicId;
            }

            report.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);

            var sourceDocumentIds = ParseGuidList(report.SourceDocumentIds);
            var sourceChunkIds = ParseGuidList(report.SourceChunkIds);
            var citations = ParseCitations(report.Citations);

            return ApiResponse<ReportDetail>.Ok(
                Mapper.ToReportDetail(report, sourceDocumentIds, sourceChunkIds, citations));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update report {ReportId}", reportId);
            return ApiResponse<ReportDetail>.Fail("update_report_error", ex.Message);
        }
    }

    // ===== Archive Report =====

    public async Task<ApiResponse<object>> ArchiveAsync(
        Guid userId,
        Guid reportId,
        CancellationToken ct = default)
    {
        try
        {
            var report = await _db.Reports
                .FirstOrDefaultAsync(r => r.Id == reportId && r.UserId == userId, ct);

            if (report == null)
            {
                return ApiResponse<object>.Fail("report_not_found", "Report not found");
            }

            report.Status = "archived";
            report.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);

            return ApiResponse<object>.Ok(new { archived = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to archive report {ReportId}", reportId);
            return ApiResponse<object>.Fail("archive_report_error", ex.Message);
        }
    }

    // ===== Delete Report =====

    public async Task<ApiResponse<object>> DeleteAsync(
        Guid userId,
        Guid reportId,
        CancellationToken ct = default)
    {
        try
        {
            var report = await _db.Reports
                .FirstOrDefaultAsync(r => r.Id == reportId && r.UserId == userId, ct);

            if (report == null)
            {
                return ApiResponse<object>.Fail("report_not_found", "Report not found");
            }

            var sources = await _db.ReportSources.Where(x => x.ReportId == reportId).ToListAsync(ct);
            var citations = await _db.ReportCitations.Where(x => x.ReportId == reportId).ToListAsync(ct);
            var jobs = await _db.ReportJobs.Where(x => x.ReportId == reportId && x.UserId == userId).ToListAsync(ct);
            var exportJobs = await _db.ExportJobs
                .Where(x => x.TargetType == "report" && x.TargetId == reportId && x.UserId == userId)
                .ToListAsync(ct);

            _db.ReportSources.RemoveRange(sources);
            _db.ReportCitations.RemoveRange(citations);
            _db.ReportJobs.RemoveRange(jobs);
            _db.ExportJobs.RemoveRange(exportJobs);
            _db.Reports.Remove(report);
            await _db.SaveChangesAsync(ct);

            return ApiResponse<object>.Ok(new { deleted = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete report {ReportId}", reportId);
            return ApiResponse<object>.Fail("delete_report_error", ex.Message);
        }
    }

    // ===== Get Job Status =====

    public async Task<ApiResponse<ReportJobStatusResponse>> GetJobStatusAsync(
        Guid userId,
        Guid jobId,
        CancellationToken ct = default)
    {
        try
        {
            var job = await _db.ReportJobs
                .FirstOrDefaultAsync(j => j.Id == jobId && j.UserId == userId, ct);

            if (job == null)
            {
                return ApiResponse<ReportJobStatusResponse>.Fail("job_not_found", "Report job not found");
            }

            var status = new ReportJobStatusResponse
            {
                Id = job.Id,
                Status = job.Status,
                Progress = job.Progress,
                CurrentStep = job.CurrentStep,
                ReportId = job.ReportId,
                ErrorMessage = job.ErrorMessage,
                CreatedAt = job.CreatedAt,
                FinishedAt = job.FinishedAt
            };

            return ApiResponse<ReportJobStatusResponse>.Ok(status);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get job status {JobId}", jobId);
            return ApiResponse<ReportJobStatusResponse>.Fail("get_job_status_error", ex.Message);
        }
    }

    // ===== Private Helpers =====

    private static List<Guid> ParseGuidList(string? json)
    {
        if (string.IsNullOrEmpty(json))
        {
            return new List<Guid>();
        }

        try
        {
            return JsonSerializer.Deserialize<List<Guid>>(json) ?? new List<Guid>();
        }
        catch
        {
            return new List<Guid>();
        }
    }

    private static List<CitationItem> ParseCitations(string? json)
    {
        if (string.IsNullOrEmpty(json))
        {
            return new List<CitationItem>();
        }

        try
        {
            return JsonSerializer.Deserialize<List<CitationItem>>(json) ?? new List<CitationItem>();
        }
        catch
        {
            return new List<CitationItem>();
        }
    }
}
