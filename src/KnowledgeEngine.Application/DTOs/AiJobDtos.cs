namespace KnowledgeEngine.Application.DTOs;

public class AiJobListItem
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string JobType { get; set; } = string.Empty;
    public string TargetType { get; set; } = string.Empty;
    public Guid TargetId { get; set; }
    public string Status { get; set; } = "pending";
    public string? Model { get; set; }
    public int? InputTokens { get; set; }
    public int? OutputTokens { get; set; }
    public decimal? CostEstimate { get; set; }
    public int RetryCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
}

public class AiJobResponse
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string JobType { get; set; } = string.Empty;
    public string TargetType { get; set; } = string.Empty;
    public Guid TargetId { get; set; }
    public string Status { get; set; } = "pending";
    public string? Model { get; set; }
    public string? PromptVersion { get; set; }
    public int? InputTokens { get; set; }
    public int? OutputTokens { get; set; }
    public decimal? CostEstimate { get; set; }
    public string? ErrorMessage { get; set; }
    public int RetryCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
}
