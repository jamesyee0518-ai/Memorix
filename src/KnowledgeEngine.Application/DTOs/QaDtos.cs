using System.Text.Json.Serialization;

namespace KnowledgeEngine.Application.DTOs;

// ===== QA Session =====

public class CreateQaSessionRequest
{
    public Guid? TopicId { get; set; }
    public string? Title { get; set; }
}

public class QaSessionResponse
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid? TopicId { get; set; }
    public string? Title { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class QaSessionListItem
{
    public Guid Id { get; set; }
    public Guid? TopicId { get; set; }
    public string? Title { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

// ===== QA Ask =====

public class QaAskRequest
{
    public Guid SessionId { get; set; }
    public string Query { get; set; } = string.Empty;
    public Guid? TopicId { get; set; }
}

public class QaAnswerResponse
{
    public string Answer { get; set; } = string.Empty;
    public List<Citation> Citations { get; set; } = new();
    public RetrievalInfo Retrieval { get; set; } = new();
    public string? Model { get; set; }
    public int? InputTokens { get; set; }
    public int? OutputTokens { get; set; }
    public int? LatencyMs { get; set; }
    public double? Confidence { get; set; }
    public QaDebugInfo? DebugInfo { get; set; }
}

public class Citation
{
    public int Index { get; set; }
    public Guid DocumentId { get; set; }
    public Guid ChunkId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? SourceUrl { get; set; }
    public string? SourceDomain { get; set; }
    public string? SourceType { get; set; }
    public string Snippet { get; set; } = string.Empty;
    public double Score { get; set; }
    public string? TitleOriginal { get; set; }
    public string? TitleZh { get; set; }
    public string? DisplaySnippet { get; set; }
    public string? OriginalSnippet { get; set; }
    public string? ContentLanguage { get; set; }
    public string DisplayContentSource { get; set; } = "original";
    public Guid? ChunkGroupId { get; set; }
    public string? Section { get; set; }
    public int? PageStart { get; set; }
    public int? PageEnd { get; set; }
    public Guid? LocalizationId { get; set; }
    public string? TranslationType { get; set; }
    public string? ReviewStatus { get; set; }
}

public class RetrievalInfo
{
    public string SearchType { get; set; } = "hybrid";
    public int RetrievedCount { get; set; }
    public int UsedCount { get; set; }
    public double TopScore { get; set; }
}

// ===== QA Messages =====

public class QaMessageResponse
{
    public Guid Id { get; set; }
    public Guid SessionId { get; set; }
    public string Role { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public List<Citation> Citations { get; set; } = new();
    public RetrievalInfo? Retrieval { get; set; }
    public string? Model { get; set; }
    public int? InputTokens { get; set; }
    public int? OutputTokens { get; set; }
    public int? LatencyMs { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class QaDebugInfo
{
    public string? QueryPlan { get; set; }
    public string? OriginalQuery { get; set; }
    public string? CompletedQuery { get; set; }
    public int? ContextTokens { get; set; }
    public List<string>? RetrievedTitles { get; set; }
    public string? SystemPrompt { get; set; }
    public EmbeddingDiagnostics? EmbeddingDiagnostics { get; set; }
    public List<string>? CitationValidationIssues { get; set; }
}

public class EmbeddingDiagnostics
{
    public int EligibleChunkCount { get; set; }
    public int TotalEmbeddingCount { get; set; }
    public int DoneCount { get; set; }
    public int PendingCount { get; set; }
    public int FailedCount { get; set; }
    public int StaleCount { get; set; }
    public double Coverage { get; set; }
    public string Status { get; set; } = "unknown";
    public string? Message { get; set; }
}
