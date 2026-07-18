namespace KnowledgeEngine.Domain.Entities;

public class SyncInboxStaging
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid? BindingId { get; set; }
    public string CloudInboxItemId { get; set; } = string.Empty;
    public long? CloudRevision { get; set; }
    public string? ContentHash { get; set; }
    public string? RemoteMetadataJson { get; set; }
    public string Status { get; set; } = "discovered";
    public Guid? LocalInboxItemId { get; set; }
    public Guid? DuplicateDocumentId { get; set; }
    public Guid? ImportBatchId { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime DiscoveredAt { get; set; }
    public DateTime? ImportedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
