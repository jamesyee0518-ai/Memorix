namespace KnowledgeEngine.Application.DTOs;

public sealed class EntityGraphNodeDto
{
    public Guid Id { get; set; }
    public string Label { get; set; } = string.Empty;
    public string CanonicalName { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public int MentionCount { get; set; }
    public int SourceCount { get; set; }
    public int Degree { get; set; }
    public IReadOnlyList<Guid> DocumentIds { get; set; } = [];
}

public sealed class EntityGraphEdgeDto
{
    public Guid SourceEntityId { get; set; }
    public Guid TargetEntityId { get; set; }
    public string RelationType { get; set; } = string.Empty;
    public int Weight { get; set; }
    public int EvidenceDocumentCount { get; set; }
    public IReadOnlyList<Guid> EvidenceDocumentIds { get; set; } = [];
}

public sealed class EntityGraphDto
{
    public IReadOnlyList<EntityGraphNodeDto> Nodes { get; set; } = [];
    public IReadOnlyList<EntityGraphEdgeDto> Edges { get; set; } = [];
    public int DocumentCount { get; set; }
}

public sealed class EntityGraphDocumentDto
{
    public Guid DocumentId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? OriginalMention { get; set; }
    public string DisplayEntityName { get; set; } = string.Empty;
    public int MentionCount { get; set; }
    public string? Evidence { get; set; }
}
