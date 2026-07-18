namespace KnowledgeEngine.Domain.Entities;

public class ApiCallLog
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid? ApiKeyId { get; set; }

    public string Endpoint { get; set; } = string.Empty;
    public string? RequestMethod { get; set; }
    public string? RequestSummary { get; set; } // JSONB

    public int? StatusCode { get; set; }
    public string? ErrorCode { get; set; }
    public int? LatencyMs { get; set; }

    public int? InputTokens { get; set; }
    public int? OutputTokens { get; set; }
    public int? RetrievedCount { get; set; }

    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }

    public DateTime CreatedAt { get; set; }
}
