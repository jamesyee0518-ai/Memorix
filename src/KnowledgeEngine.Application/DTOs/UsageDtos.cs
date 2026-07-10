using System.Text.Json.Serialization;

namespace KnowledgeEngine.Application.DTOs;

// ===== Usage Responses =====

public class UsageResponse
{
    [JsonPropertyName("today")]
    public UsageDailyItem Today { get; set; } = new();

    [JsonPropertyName("last7Days")]
    public List<UsageDailyItem> Last7Days { get; set; } = new();

    [JsonPropertyName("totals")]
    public UsageTotals Totals { get; set; } = new();

    [JsonPropertyName("traceId")]
    public string? TraceId { get; set; }
}

public class UsageDailyItem
{
    [JsonPropertyName("date")]
    public DateTime Date { get; set; }

    [JsonPropertyName("importedCount")]
    public int ImportedCount { get; set; }

    [JsonPropertyName("documentCount")]
    public int DocumentCount { get; set; }

    [JsonPropertyName("searchCount")]
    public int SearchCount { get; set; }

    [JsonPropertyName("qaCount")]
    public int QaCount { get; set; }

    [JsonPropertyName("reportCount")]
    public int ReportCount { get; set; }

    [JsonPropertyName("exportCount")]
    public int ExportCount { get; set; }

    [JsonPropertyName("apiCallCount")]
    public int ApiCallCount { get; set; }

    [JsonPropertyName("agentCallCount")]
    public int AgentCallCount { get; set; }

    [JsonPropertyName("agentSearchCount")]
    public int AgentSearchCount { get; set; }

    [JsonPropertyName("agentQaCount")]
    public int AgentQaCount { get; set; }

    [JsonPropertyName("agentWriteCount")]
    public int AgentWriteCount { get; set; }

    [JsonPropertyName("agentSuccessCount")]
    public int AgentSuccessCount { get; set; }

    [JsonPropertyName("agentFailedCount")]
    public int AgentFailedCount { get; set; }

    [JsonPropertyName("inputTokens")]
    public int InputTokens { get; set; }

    [JsonPropertyName("outputTokens")]
    public int OutputTokens { get; set; }

    [JsonPropertyName("embeddingTokens")]
    public int EmbeddingTokens { get; set; }

    [JsonPropertyName("storageBytes")]
    public long StorageBytes { get; set; }
}

public class UsageTotals
{
    [JsonPropertyName("documentCount")]
    public int DocumentCount { get; set; }

    [JsonPropertyName("searchCount")]
    public int SearchCount { get; set; }

    [JsonPropertyName("qaCount")]
    public int QaCount { get; set; }

    [JsonPropertyName("reportCount")]
    public int ReportCount { get; set; }

    [JsonPropertyName("apiCallCount")]
    public int ApiCallCount { get; set; }

    [JsonPropertyName("agentCallCount")]
    public int AgentCallCount { get; set; }

    [JsonPropertyName("inputTokens")]
    public int InputTokens { get; set; }

    [JsonPropertyName("outputTokens")]
    public int OutputTokens { get; set; }
}

/// <summary>
/// Usage types that can be recorded
/// </summary>
public enum UsageType
{
    Search,
    Qa,
    Report,
    Export,
    ApiCall,
    Import,
    Document
}
