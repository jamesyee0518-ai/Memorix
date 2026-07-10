using KnowledgeEngine.Application.DTOs;

namespace KnowledgeEngine.Application.Interfaces;

public interface IExportService
{
    Task<ApiResponse<ExportJobResponse>> ExportDocumentMarkdownAsync(Guid userId, ExportDocumentRequest request, CancellationToken ct = default);
    Task<ApiResponse<ExportJobResponse>> ExportReportMarkdownAsync(Guid userId, ExportReportRequest request, CancellationToken ct = default);
    Task<ApiResponse<ExportJobResponse>> ExportReportJsonAsync(Guid userId, ExportReportJsonRequest request, CancellationToken ct = default);
    Task<ApiResponse<ExportJobResponse>> ExportTopicObsidianAsync(Guid userId, ExportTopicRequest request, CancellationToken ct = default);
    Task<ApiResponse<ExportJobResponse>> ExportSearchJsonAsync(Guid userId, ExportSearchRequest request, CancellationToken ct = default);
    Task<ApiResponse<ExportJobDetail>> GetExportJobAsync(Guid userId, Guid jobId, CancellationToken ct = default);
}
