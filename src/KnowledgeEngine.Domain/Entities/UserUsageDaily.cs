namespace KnowledgeEngine.Domain.Entities;

public class UserUsageDaily
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public DateTime UsageDate { get; set; }

    public int ImportedCount { get; set; }
    public int DocumentCount { get; set; }
    public int SearchCount { get; set; }
    public int QaCount { get; set; }
    public int ReportCount { get; set; }
    public int ExportCount { get; set; }
    public int ApiCallCount { get; set; }

    // Agent dimension
    public int AgentCallCount { get; set; }
    public int AgentSearchCount { get; set; }
    public int AgentQaCount { get; set; }
    public int AgentWriteCount { get; set; }
    public int AgentSuccessCount { get; set; }
    public int AgentFailedCount { get; set; }

    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public int EmbeddingTokens { get; set; }

    public long StorageBytes { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
