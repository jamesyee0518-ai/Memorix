namespace KnowledgeEngine.Domain.Entities;

public class AiJob
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string? ClientJobId { get; set; }
    public Guid? WorkspaceId { get; set; }
    public Guid? BillingAccountId { get; set; }
    public Guid? PricePlanVersionId { get; set; }
    public Guid? DeviceId { get; set; }

    public string JobType { get; set; } = string.Empty;
    public string TargetType { get; set; } = string.Empty;
    public Guid TargetId { get; set; }

    public string Status { get; set; } = "pending";
    public string ExecutionMode { get; set; } = AiExecutionModes.Local;
    public string BillingMode { get; set; } = AiBillingModes.LocalFree;
    public string? Model { get; set; }
    public string? PromptVersion { get; set; }
    public string? DataPolicy { get; set; }
    public string? ModelPolicy { get; set; }

    public int? InputTokens { get; set; }
    public int? OutputTokens { get; set; }
    public decimal? CostEstimate { get; set; }
    public decimal EstimatedCredits { get; set; }
    public decimal ActualCredits { get; set; }
    public decimal EstimatedAmount { get; set; }
    public decimal ActualAmount { get; set; }
    public decimal? BudgetLimit { get; set; }
    public string Currency { get; set; } = "CNY";

    public string? ErrorMessage { get; set; }
    public int RetryCount { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
}
