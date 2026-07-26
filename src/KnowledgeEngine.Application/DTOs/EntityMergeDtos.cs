namespace KnowledgeEngine.Application.DTOs;

public sealed class EntityMergePreviewRequest
{
    public Guid WorkspaceId { get; set; }
    public Guid EntityIdA { get; set; }
    public Guid EntityIdB { get; set; }
}

public sealed class EntityMergePreview
{
    public Guid SourceEntityId { get; set; }
    public Guid TargetEntityId { get; set; }
    public string RecommendationReason { get; set; } = string.Empty;
    public long SourceVersion { get; set; }
    public long TargetVersion { get; set; }
    public int MentionCount { get; set; }
    public int AliasCount { get; set; }
    public int ExternalIdCount { get; set; }
    public int DocumentAssociationCount { get; set; }
    public int RelationCount { get; set; }
    public int AliasConflictCount { get; set; }
    public int ExternalIdConflictCount { get; set; }
    public int SelfLoopCount { get; set; }
    public IReadOnlyList<string> HardBlocks { get; set; } = [];
    public IReadOnlyList<string> AffectedIndexes { get; set; } =
        ["search", "qa", "report", "graph", "entity_vector"];
    public int EstimatedMilliseconds { get; set; }
    public bool CanExecute { get; set; }
}

public sealed class ExecuteEntityMergeRequest
{
    public Guid WorkspaceId { get; set; }
    public Guid SourceEntityId { get; set; }
    public Guid TargetEntityId { get; set; }
    public long ExpectedSourceVersion { get; set; }
    public long ExpectedTargetVersion { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string Method { get; set; } = "manual";
    public decimal? Score { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public string? DeviceId { get; set; }
    public string? RequestId { get; set; }
}

public sealed class EntityMergeResult
{
    public Guid MergeId { get; set; }
    public Guid SourceEntityId { get; set; }
    public Guid TargetEntityId { get; set; }
    public string Status { get; set; } = string.Empty;
    public bool IdempotentReplay { get; set; }
    public DateTime CompletedAt { get; set; }
}

public sealed class EntityMergeHistoryItem
{
    public Guid MergeId { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid SourceEntityId { get; set; }
    public Guid TargetEntityId { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string Method { get; set; } = string.Empty;
    public decimal? Score { get; set; }
    public Guid? OperatorId { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime? RevertedAt { get; set; }
}

public sealed class RevertEntityMergeRequest
{
    public string IdempotencyKey { get; set; } = string.Empty;
    public string? RequestId { get; set; }
}

public sealed class AddEntityMergeBlockRequest
{
    public Guid WorkspaceId { get; set; }
    public Guid EntityIdA { get; set; }
    public Guid EntityIdB { get; set; }
    public string Reason { get; set; } = string.Empty;
    public bool IsPermanent { get; set; } = true;
    public DateTime? ValidUntil { get; set; }
}
