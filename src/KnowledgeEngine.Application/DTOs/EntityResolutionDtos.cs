namespace KnowledgeEngine.Application.DTOs;

public sealed class StartEntityScanRequest
{
    public Guid WorkspaceId { get; set; }
    public string? EntityType { get; set; }
    public int BatchSize { get; set; } = 50;
    public string? IdempotencyKey { get; set; }
}

public sealed class StartEntityMaintenanceRequest
{
    public Guid WorkspaceId { get; set; }
    public string Operation { get; set; } = string.Empty;
    public int BatchSize { get; set; } = 50;
    public string? IdempotencyKey { get; set; }
}

public sealed class EntityGovernanceTaskDto
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public string TaskType { get; set; } = string.Empty;
    public Guid? ParentTaskId { get; set; }
    public Guid? SubjectEntityId { get; set; }
    public Guid? CandidateEntityId { get; set; }
    public Guid? MentionId { get; set; }
    public string Status { get; set; } = string.Empty;
    public int Priority { get; set; }
    public string? Cursor { get; set; }
    public int TotalItems { get; set; }
    public int ProcessedItems { get; set; }
    public int SucceededItems { get; set; }
    public int FailedItems { get; set; }
    public decimal? Score { get; set; }
    public IReadOnlyList<string> ReasonCodes { get; set; } = [];
    public string? ErrorMessage { get; set; }
    public int RetryCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}

public sealed class EntityGovernanceDecisionRequest
{
    public string Decision { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
}

public sealed class EntityQualityMetrics
{
    public Guid? WorkspaceId { get; set; }
    public int ActiveEntityCount { get; set; }
    public int MergedEntityCount { get; set; }
    public int AliasCount { get; set; }
    public int MentionCount { get; set; }
    public int LinkedMentionCount { get; set; }
    public int UnresolvedMentionCount { get; set; }
    public decimal MentionLinkRate { get; set; }
    public decimal UnresolvedRate { get; set; }
    public int PendingReviewCount { get; set; }
    public int DuplicateCandidateCount { get; set; }
    public int CompletedMergeCount { get; set; }
    public int RevertedMergeCount { get; set; }
    public decimal MergeRevertRate { get; set; }
    public decimal EstimatedDuplicateRate { get; set; }
    public int PendingOutboxCount { get; set; }
    public int FailedOutboxCount { get; set; }
    public double? OldestPendingOutboxSeconds { get; set; }
    public IReadOnlyDictionary<string, int> EntityTypeDistribution { get; set; }
        = new Dictionary<string, int>();
    public IReadOnlyDictionary<string, int> NormalizationVersionDistribution { get; set; }
        = new Dictionary<string, int>();
}
