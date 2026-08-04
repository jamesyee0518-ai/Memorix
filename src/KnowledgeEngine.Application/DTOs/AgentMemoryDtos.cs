namespace KnowledgeEngine.Application.DTOs;

public class SessionDto
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid UserId { get; set; }
    public Guid? AgentProfileId { get; set; }
    public string ExternalSessionKey { get; set; } = string.Empty;
    public string TaskTitle { get; set; } = string.Empty;
    public string Status { get; set; } = "active";
    public DateTime StartedAt { get; set; }
    public DateTime LastActiveAt { get; set; }
    public DateTime? ClosedAt { get; set; }
    public Guid? TopicId { get; set; }
}

public class MemoryItemDto
{
    public Guid Id { get; set; }
    public Guid? SessionId { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid OwnerUserId { get; set; }
    public Guid? AgentProfileId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Content { get; set; }
    public string? Summary { get; set; }
    public string AdmissionState { get; set; } = "candidate";
    public decimal Confidence { get; set; }
    public string Visibility { get; set; } = "agent";
    public int Importance { get; set; }
    public DateTime? FreshnessAt { get; set; }
    public string Status { get; set; } = "active";
    public DateTime CreatedAt { get; set; }
    public List<EvidenceDto> Evidence { get; set; } = new();
}

public class CaptureMemoryInput
{
    public Guid? SessionId { get; set; }
    public string Kind { get; set; } = "task_state";
    public string Title { get; set; } = string.Empty;
    public string? Content { get; set; }
    public string? Summary { get; set; }
    public decimal? Confidence { get; set; }
    public string Visibility { get; set; } = "agent";
    public int Importance { get; set; } = 5;
    public List<EvidenceInput>? Evidence { get; set; }
}

public class EvidenceInput
{
    public string EvidenceKind { get; set; } = "manual_confirmation";
    public string ReferenceId { get; set; } = string.Empty;
    public string? Locator { get; set; }
    public string? Relation { get; set; }
}

public class SearchMemoryInput
{
    public string Query { get; set; } = string.Empty;
    public Guid? SessionId { get; set; }
    public Guid? TopicId { get; set; }
    public string? Kind { get; set; }
    public string? AdmissionState { get; set; }
    public int Limit { get; set; } = 20;
    public int Offset { get; set; } = 0;
}

public class ContextPackDto
{
    public Guid SessionId { get; set; }
    public int TokenBudget { get; set; }
    public int TokenUsed { get; set; }
    public List<ContextLayerDto> L1 { get; set; } = new();
    public List<ContextLayerDto> L2 { get; set; } = new();
    public List<ContextLayerDto> L3 { get; set; } = new();
}

public class ContextLayerDto
{
    public string Type { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Content { get; set; }
    public decimal? Confidence { get; set; }
    public string? AdmissionState { get; set; }
    public string? EvidenceRef { get; set; }
}

public class EvidenceDto
{
    public Guid Id { get; set; }
    public Guid MemoryItemId { get; set; }
    public string EvidenceKind { get; set; } = string.Empty;
    public string ReferenceId { get; set; } = string.Empty;
    public string? Locator { get; set; }
    public string? Relation { get; set; }
    public DateTime CapturedAt { get; set; }
}

public class FeedbackDto
{
    public Guid Id { get; set; }
    public Guid MemoryItemId { get; set; }
    public Guid UserId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string? Note { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class AccessLogDto
{
    public Guid Id { get; set; }
    public Guid? MemoryItemId { get; set; }
    public Guid? SessionId { get; set; }
    public Guid? AgentProfileId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string? TraceId { get; set; }
    public DateTime CreatedAt { get; set; }
}
