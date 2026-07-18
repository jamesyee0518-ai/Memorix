using System.Text.Json.Serialization;

namespace KnowledgeEngine.Application.DTOs;

// ===== Export Requests =====

public class ExportDocumentRequest
{
    public Guid DocumentId { get; set; }
    public bool IncludeAiSummary { get; set; } = true;
    public bool IncludeMetadata { get; set; } = true;
}

public class ExportReportRequest
{
    public Guid ReportId { get; set; }
}

public class ExportReportJsonRequest
{
    public Guid ReportId { get; set; }
}

public class ExportTopicRequest
{
    public Guid TopicId { get; set; }
    public bool IncludeDocuments { get; set; } = true;
    public bool IncludeReports { get; set; } = true;
    public bool IncludeAiSummary { get; set; } = true;
}

public class ExportSearchRequest
{
    public Guid? TopicId { get; set; }
    public string Query { get; set; } = string.Empty;
    public SearchFilters? Filters { get; set; }
}

// ===== Export Responses =====

public class ExportJobResponse
{
    public Guid JobId { get; set; }
    public string Status { get; set; } = "pending";
    public string ExportType { get; set; } = string.Empty;
    public string TargetType { get; set; } = string.Empty;
    public Guid? TargetId { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class ExportJobDetail
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid? TopicId { get; set; }
    public string ExportType { get; set; } = string.Empty;
    public string TargetType { get; set; } = string.Empty;
    public Guid? TargetId { get; set; }
    public string Status { get; set; } = string.Empty;
    public Guid? FileId { get; set; }
    public string? DownloadUrl { get; set; }
    public string? ErrorMessage { get; set; }
    public int RetryCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
}
