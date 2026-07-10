namespace KnowledgeEngine.Domain.Entities;

public class EntityRelation
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid SourceEntityId { get; set; }
    public Guid TargetEntityId { get; set; }
    public string RelationType { get; set; } = string.Empty;
    public Guid? EvidenceDocumentId { get; set; }
    public string? EvidenceText { get; set; }
    public decimal? Confidence { get; set; }
    public DateTime CreatedAt { get; set; }
}
