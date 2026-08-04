using KnowledgeEngine.Domain.Enums;

namespace KnowledgeEngine.Domain.Entities;

public class AgentMemoryEvidence
{
    public Guid Id { get; set; }
    public Guid MemoryItemId { get; set; }
    public EvidenceKind EvidenceKind { get; set; }
    public string ReferenceId { get; set; } = string.Empty;
    public string? Locator { get; set; }
    public string? Relation { get; set; }
    public string? SnapshotHash { get; set; }
    public DateTime CapturedAt { get; set; }

    // Navigation
    public AgentMemoryItem? MemoryItem { get; set; }
}
