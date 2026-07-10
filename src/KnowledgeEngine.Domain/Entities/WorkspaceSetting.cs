namespace KnowledgeEngine.Domain.Entities;

/// <summary>
/// Key-value settings store, scoped to a workspace.
/// Used for model config, feature flags, etc.
/// </summary>
public class WorkspaceSetting
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; }
}
