namespace KnowledgeEngine.Application.Settings;

public sealed class EntityResolutionSettings
{
    public bool Enabled { get; set; } = true;
    public bool EnableExactAliasLink { get; set; } = true;
    public bool EnableScoredAutoLink { get; set; } = true;
    public bool EnableVectorCandidates { get; set; } = true;
    public int SemanticPoolSize { get; set; } = 50;
    public int CandidateTopK { get; set; } = 20;
    public bool EnableAutoLink { get; set; } = true;
    public bool EnableAutoMerge { get; set; } = false;
    public bool ShadowMode { get; set; } = false;
    public bool EnableLlmDisambiguation { get; set; } = true;
    public decimal LlmMinimumCandidateScore { get; set; } = 0.78m;
    public decimal LlmLinkConfidence { get; set; } = 0.90m;
    public int LlmTimeoutSeconds { get; set; } = 30;
    public string? LlmModel { get; set; }
    public bool EnableEntitySearchExpansion { get; set; } = true;
    public bool EnableGraphBackend { get; set; } = true;
}
