namespace KnowledgeEngine.Domain.Entities;

public class EntityResolutionCandidate
{
    public Guid Id { get; set; }
    public Guid MentionId { get; set; }
    public Guid CandidateEntityId { get; set; }
    public string WorkspaceId { get; set; } = string.Empty;
    public int Rank { get; set; }
    public decimal NameScore { get; set; }
    public decimal AliasScore { get; set; }
    public decimal DescriptionScore { get; set; }
    public decimal ContextScore { get; set; }
    public decimal RelationScore { get; set; }
    public decimal SourceScore { get; set; }
    public decimal TotalScore { get; set; }
    public string Decision { get; set; } = "PENDING";
    public string? ReasonCodes { get; set; }
    public string ResolverVersion { get; set; } = "entity_resolver_v1";
    public string? LlmDecision { get; set; }
    public decimal? LlmConfidence { get; set; }
    public string? LlmExplanation { get; set; }
    public string? LlmModel { get; set; }
    public string? LlmPromptVersion { get; set; }
    public int? LlmInputTokens { get; set; }
    public int? LlmOutputTokens { get; set; }
    public DateTime CreatedAt { get; set; }
}
