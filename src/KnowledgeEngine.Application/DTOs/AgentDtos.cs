using System.Text.Json.Serialization;

namespace KnowledgeEngine.Application.DTOs;

// ===== Agent Topic =====

public class AgentTopicItem
{
    [JsonPropertyName("id")]
    public Guid Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("domain")]
    public string? Domain { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("document_count")]
    public int DocumentCount { get; set; }

    [JsonPropertyName("report_count")]
    public int ReportCount { get; set; }

    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; }
}

// ===== Agent Search =====

public class AgentSearchRequest
{
    [JsonPropertyName("topic_id")]
    public Guid? TopicId { get; set; }

    [JsonPropertyName("query")]
    public string Query { get; set; } = string.Empty;

    [JsonPropertyName("search_type")]
    public string SearchType { get; set; } = "hybrid";

    [JsonPropertyName("limit")]
    public int Limit { get; set; } = 10;
}

public class AgentSearchResultItem
{
    [JsonPropertyName("document_id")]
    public Guid DocumentId { get; set; }

    [JsonPropertyName("chunk_id")]
    public Guid ChunkId { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("snippet")]
    public string Snippet { get; set; } = string.Empty;

    [JsonPropertyName("source_type")]
    public string? SourceType { get; set; }

    [JsonPropertyName("source_url")]
    public string? SourceUrl { get; set; }

    [JsonPropertyName("source_domain")]
    public string? SourceDomain { get; set; }

    [JsonPropertyName("published_at")]
    public DateTime? PublishedAt { get; set; }

    [JsonPropertyName("value_score")]
    public int? ValueScore { get; set; }

    [JsonPropertyName("score")]
    public double Score { get; set; }
}

public class AgentSearchMetadata
{
    [JsonPropertyName("total")]
    public int Total { get; set; }

    [JsonPropertyName("latency_ms")]
    public int LatencyMs { get; set; }
}

public class AgentSearchResult
{
    [JsonPropertyName("items")]
    public List<AgentSearchResultItem> Items { get; set; } = new();

    [JsonPropertyName("metadata")]
    public AgentSearchMetadata Metadata { get; set; } = new();

    [JsonPropertyName("trace_id")]
    public string? TraceId { get; set; }
}

// ===== Agent QA =====

public class AgentQaRequest
{
    [JsonPropertyName("topic_id")]
    public Guid? TopicId { get; set; }

    [JsonPropertyName("query")]
    public string Query { get; set; } = string.Empty;

    [JsonPropertyName("answer_style")]
    public string? AnswerStyle { get; set; } // concise / research_brief / bullet_points
}

public class AgentQaCitation
{
    [JsonPropertyName("index")]
    public int Index { get; set; }

    [JsonPropertyName("document_id")]
    public Guid DocumentId { get; set; }

    [JsonPropertyName("chunk_id")]
    public Guid ChunkId { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("source_url")]
    public string? SourceUrl { get; set; }

    [JsonPropertyName("source_domain")]
    public string? SourceDomain { get; set; }

    [JsonPropertyName("source_type")]
    public string? SourceType { get; set; }

    [JsonPropertyName("snippet")]
    public string Snippet { get; set; } = string.Empty;

    [JsonPropertyName("score")]
    public double Score { get; set; }
}

public class AgentQaMetadata
{
    [JsonPropertyName("topic_id")]
    public Guid? TopicId { get; set; }

    [JsonPropertyName("retrieved_count")]
    public int RetrievedCount { get; set; }

    [JsonPropertyName("used_count")]
    public int UsedCount { get; set; }

    [JsonPropertyName("model")]
    public string? Model { get; set; }

    [JsonPropertyName("latency_ms")]
    public int LatencyMs { get; set; }
}

public class AgentWarning
{
    [JsonPropertyName("code")]
    public string Code { get; set; } = string.Empty;

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;
}

public class AgentQaResult
{
    [JsonPropertyName("answer")]
    public string Answer { get; set; } = string.Empty;

    [JsonPropertyName("citations")]
    public List<AgentQaCitation> Citations { get; set; } = new();

    [JsonPropertyName("metadata")]
    public AgentQaMetadata Metadata { get; set; } = new();

    [JsonPropertyName("warnings")]
    public List<AgentWarning> Warnings { get; set; } = new();

    [JsonPropertyName("trace_id")]
    public string? TraceId { get; set; }
}

// ===== Agent Document =====

public class AgentDocumentResult
{
    [JsonPropertyName("id")]
    public Guid Id { get; set; }

    [JsonPropertyName("topic_id")]
    public Guid? TopicId { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("summary")]
    public string? Summary { get; set; }

    [JsonPropertyName("one_sentence_conclusion")]
    public string? OneSentenceConclusion { get; set; }

    [JsonPropertyName("key_points")]
    public string? KeyPoints { get; set; }

    [JsonPropertyName("business_signals")]
    public string? BusinessSignals { get; set; }

    [JsonPropertyName("technical_signals")]
    public string? TechnicalSignals { get; set; }

    [JsonPropertyName("risks")]
    public string? Risks { get; set; }

    [JsonPropertyName("opportunities")]
    public string? Opportunities { get; set; }

    [JsonPropertyName("value_score")]
    public int? ValueScore { get; set; }

    [JsonPropertyName("ai_status")]
    public string AiStatus { get; set; } = "pending";

    [JsonPropertyName("content_text")]
    public string? ContentText { get; set; }

    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("updated_at")]
    public DateTime UpdatedAt { get; set; }

    [JsonPropertyName("trace_id")]
    public string? TraceId { get; set; }
}

public class AgentChunkResultItem
{
    [JsonPropertyName("id")]
    public Guid Id { get; set; }

    [JsonPropertyName("chunk_index")]
    public int ChunkIndex { get; set; }

    [JsonPropertyName("chunk_title")]
    public string? ChunkTitle { get; set; }

    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;

    [JsonPropertyName("token_count")]
    public int? TokenCount { get; set; }

    [JsonPropertyName("char_count")]
    public int? CharCount { get; set; }

    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; }
}

public class AgentChunkResult
{
    [JsonPropertyName("document_id")]
    public Guid DocumentId { get; set; }

    [JsonPropertyName("chunks")]
    public List<AgentChunkResultItem> Chunks { get; set; } = new();

    [JsonPropertyName("trace_id")]
    public string? TraceId { get; set; }
}

// ===== Agent Report =====

public class AgentReportListItem
{
    [JsonPropertyName("id")]
    public Guid Id { get; set; }

    [JsonPropertyName("topic_id")]
    public Guid? TopicId { get; set; }

    [JsonPropertyName("report_type")]
    public string ReportType { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("quality_score")]
    public int? QualityScore { get; set; }

    [JsonPropertyName("generated_by_model")]
    public string? GeneratedByModel { get; set; }

    [JsonPropertyName("start_date")]
    public DateTime? StartDate { get; set; }

    [JsonPropertyName("end_date")]
    public DateTime? EndDate { get; set; }

    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; }
}

public class AgentReportDetail
{
    [JsonPropertyName("id")]
    public Guid Id { get; set; }

    [JsonPropertyName("topic_id")]
    public Guid? TopicId { get; set; }

    [JsonPropertyName("report_type")]
    public string ReportType { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("content_markdown")]
    public string ContentMarkdown { get; set; } = string.Empty;

    [JsonPropertyName("query")]
    public string? Query { get; set; }

    [JsonPropertyName("start_date")]
    public DateTime? StartDate { get; set; }

    [JsonPropertyName("end_date")]
    public DateTime? EndDate { get; set; }

    [JsonPropertyName("generated_by_model")]
    public string? GeneratedByModel { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("quality_score")]
    public int? QualityScore { get; set; }

    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("updated_at")]
    public DateTime UpdatedAt { get; set; }

    [JsonPropertyName("trace_id")]
    public string? TraceId { get; set; }
}

public class AgentReportListResult
{
    [JsonPropertyName("items")]
    public List<AgentReportListItem> Items { get; set; } = new();

    [JsonPropertyName("trace_id")]
    public string? TraceId { get; set; }
}
