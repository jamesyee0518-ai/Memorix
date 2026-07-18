using KnowledgeEngine.Application.DTOs;

namespace KnowledgeEngine.Application.Interfaces;

public interface IReportService
{
    Task<ApiResponse<CreateReportResponse>> CreateDailyReportAsync(Guid userId, CreateDailyReportRequest request, CancellationToken ct = default);
    Task<ApiResponse<CreateReportResponse>> CreateWeeklyReportAsync(Guid userId, CreateWeeklyReportRequest request, CancellationToken ct = default);
    Task<ApiResponse<CreateReportResponse>> CreateTopicReportAsync(Guid userId, CreateTopicReportRequest request, CancellationToken ct = default);
    Task<ApiResponse<PagedResult<ReportListItem>>> GetAllAsync(Guid userId, Guid? topicId, string? reportType, CancellationToken ct = default);
    Task<ApiResponse<ReportDetail>> GetByIdAsync(Guid userId, Guid reportId, CancellationToken ct = default);
    Task<ApiResponse<CreateReportResponse>> RegenerateAsync(Guid userId, Guid reportId, CancellationToken ct = default);
    Task<ApiResponse<ReportDetail>> UpdateAsync(Guid userId, Guid reportId, UpdateReportRequest request, CancellationToken ct = default);
    Task<ApiResponse<object>> ArchiveAsync(Guid userId, Guid reportId, CancellationToken ct = default);
    Task<ApiResponse<object>> DeleteAsync(Guid userId, Guid reportId, CancellationToken ct = default);
    Task<ApiResponse<ReportJobStatusResponse>> GetJobStatusAsync(Guid userId, Guid jobId, CancellationToken ct = default);
}
