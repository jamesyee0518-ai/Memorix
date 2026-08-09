namespace KnowledgeEngine.Domain.Entities;

/// <summary>
/// A durable, metered request to create media from a Memorix workspace.
/// The actual model execution happens in the isolated Python media service.
/// </summary>
public class MediaJob
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid WorkspaceId { get; set; }
    /// <summary>Linked control-plane billing job; null only for legacy records.</summary>
    public Guid? BillingJobId { get; set; }
    public string Capability { get; set; } = string.Empty;
    public string Status { get; set; } = MediaJobStatuses.Created;
    public string Route { get; set; } = "local_first";
    public string? ProviderId { get; set; }
    public string? ModelId { get; set; }
    public string? RunnerId { get; set; }
    public string ParametersJson { get; set; } = "{}";
    public string InputAssetIdsJson { get; set; } = "[]";
    public string OutputAssetIdsJson { get; set; } = "[]";
    public string EventsJson { get; set; } = "[]";
    public bool CancellationRequested { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}

public static class MediaJobStatuses
{
    public const string Created = "created";
    public const string Quoted = "quoted";
    public const string Queued = "queued";
    public const string Leased = "leased";
    public const string Running = "running";
    public const string Uploading = "uploading";
    public const string Completed = "completed";
    public const string Failed = "failed";
    public const string Cancelled = "cancelled";
}
