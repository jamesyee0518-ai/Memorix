using System.Text.Json.Serialization;

namespace KnowledgeEngine.Application.DTOs;

// ===== Report Requests =====

public class CreateDailyReportRequest
{
    public Guid? TopicId { get; set; }
    public DateTime? Date { get; set; }
}

public class CreateWeeklyReportRequest
{
    public Guid? TopicId { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
}

public class CreateTopicReportRequest
{
    public Guid? TopicId { get; set; }
    public string? Title { get; set; }
    public string Question { get; set; } = string.Empty;
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }
    public int? MinValueScore { get; set; }
    public string? Depth { get; set; } // "standard" (default) or "deep"
}

// ===== Report Responses =====

public class CreateReportResponse
{
    public Guid ReportJobId { get; set; }
    public string Status { get; set; } = "pending";
}

public class ReportListItem
{
    public Guid Id { get; set; }
    public Guid? TopicId { get; set; }
    public string ReportType { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int? QualityScore { get; set; }
    public string? GeneratedByModel { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public string? ExportStatus { get; set; }
    public double? CitationCoverage { get; set; }
    public int? EvidenceCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class ReportDetail
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid? TopicId { get; set; }
    public string ReportType { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string ContentMarkdown { get; set; } = string.Empty;
    public string? Summary { get; set; }
    public string? OneSentenceConclusion { get; set; }
    public string? Query { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }

    public List<Guid> SourceDocumentIds { get; set; } = new();
    public List<Guid> SourceChunkIds { get; set; } = new();
    public List<CitationItem> Citations { get; set; } = new();

    public string? GeneratedByModel { get; set; }
    public string? PromptVersion { get; set; }
    public string Status { get; set; } = string.Empty;
    public int? QualityScore { get; set; }
    public double? CitationCoverage { get; set; }
    public int? EvidenceCount { get; set; }
    public string? ExportStatus { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

// ===== Update Report Request =====

public class UpdateReportRequest
{
    public string? Title { get; set; }
    public string? ContentMarkdown { get; set; }
    public Guid? TopicId { get; set; }
}

// ===== Report Job Status Response =====

public class ReportJobStatusResponse
{
    public Guid Id { get; set; }
    public string Status { get; set; } = string.Empty;
    public int Progress { get; set; }
    public string? CurrentStep { get; set; }
    public Guid? ReportId { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
}

public class CitationItem
{
    [JsonPropertyName("index")]
    public int Index { get; set; }

    [JsonPropertyName("document_id")]
    public Guid DocumentId { get; set; }

    [JsonPropertyName("chunk_id")]
    public Guid? ChunkId { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("source_url")]
    public string? SourceUrl { get; set; }

    [JsonPropertyName("source_domain")]
    public string? SourceDomain { get; set; }

    [JsonPropertyName("source_type")]
    public string? SourceType { get; set; }

    [JsonPropertyName("relevance_score")]
    public double? RelevanceScore { get; set; }

    [JsonPropertyName("source_role")]
    public string? SourceRole { get; set; }
}
