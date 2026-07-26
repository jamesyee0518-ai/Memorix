using KnowledgeEngine.Domain.Entities;

namespace KnowledgeEngine.Application.DTOs;

public sealed class TerminologyQuery
{
    public string? Query { get; set; }
    public string? SourceLanguage { get; set; }
    public string? TargetLanguage { get; set; }
    public string? Domain { get; set; }
    public string? ReviewStatus { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

public sealed class TerminologyBulkRequest
{
    public List<Terminology> Items { get; set; } = new();
    public bool SkipConflicts { get; set; } = true;
}

public sealed class TerminologyBulkResult
{
    public int Created { get; set; }
    public int Updated { get; set; }
    public int Skipped { get; set; }
    public int ReprocessJobsQueued { get; set; }
    public List<string> Errors { get; set; } = new();
}

public sealed class TerminologyReviewRequest
{
    public string Status { get; set; } = "approved";
}

public sealed class TerminologyConflict
{
    public string SourceLanguage { get; set; } = string.Empty;
    public string SourceTerm { get; set; } = string.Empty;
    public string TargetLanguage { get; set; } = string.Empty;
    public List<Terminology> Terms { get; set; } = new();
}

public sealed class TerminologyStats
{
    public int Total { get; set; }
    public int Approved { get; set; }
    public int PendingReview { get; set; }
    public int Rejected { get; set; }
    public int Conflicts { get; set; }
    public int PendingReprocessJobs { get; set; }
    public Dictionary<string, int> Domains { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, int> LanguagePairs { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class TerminologyUsage
{
    public Guid TerminologyId { get; set; }
    public int DocumentCount { get; set; }
    public int ChunkCount { get; set; }
}

public sealed class TerminologyUsageRequest
{
    public List<Guid> TerminologyIds { get; set; } = new();
}

public sealed class TerminologyCandidate
{
    public string SourceTerm { get; set; } = string.Empty;
    public string SourceLanguage { get; set; } = "en";
    public string TargetLanguage { get; set; } = "zh-CN";
    public string? SuggestedTargetTerm { get; set; }
    public string? Domain { get; set; }
    public int Occurrences { get; set; }
    public List<Guid> DocumentIds { get; set; } = new();
}

public sealed class TerminologyExtractionRequest
{
    public Guid? TopicId { get; set; }
    public int DocumentLimit { get; set; } = 100;
    public int CandidateLimit { get; set; } = 50;
}
