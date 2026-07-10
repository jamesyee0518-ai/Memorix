namespace KnowledgeEngine.Domain.Entities;

public class Report
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid? TopicId { get; set; }

    public string ReportType { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Slug { get; set; }

    public string ContentMarkdown { get; set; } = string.Empty;
    public string? Summary { get; set; }
    public string? OneSentenceConclusion { get; set; }

    public string? Query { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }

    public string? SourceDocumentIds { get; set; } // JSONB
    public string? SourceChunkIds { get; set; }    // JSONB
    public string? SourceReportIds { get; set; }   // JSONB
    public string? Citations { get; set; }         // JSONB

    public Guid? TemplateId { get; set; }
    public string GenerationMode { get; set; } = "manual";
    public string? GeneratedByModel { get; set; }
    public string? PromptVersion { get; set; }
    public string? ModelConfigSnapshot { get; set; }

    public string Status { get; set; } = "pending";
    public int? QualityScore { get; set; }
    public double? CitationCoverage { get; set; }
    public int? EvidenceCount { get; set; }

    public string ExportStatus { get; set; } = "not_exported";
    public DateTime? LastExportedAt { get; set; }

    public string? ErrorMessage { get; set; }

    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
