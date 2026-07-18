namespace KnowledgeEngine.Application.Interfaces;

/// <summary>
/// Manages the local configuration file at ~/.knowledge-engine/config.json.
/// Stores workspace list, current workspace, and app-level settings.
/// </summary>
public interface IConfigService
{
    Task<LocalConfig> LoadConfigAsync(CancellationToken ct = default);
    Task SaveConfigAsync(LocalConfig config, CancellationToken ct = default);
    Task<string?> GetCurrentWorkspaceIdAsync(CancellationToken ct = default);
    Task SetCurrentWorkspaceIdAsync(string workspaceId, CancellationToken ct = default);
}

/// <summary>
/// Local config file structure at ~/.knowledge-engine/config.json
/// </summary>
public class LocalConfig
{
    public string CurrentWorkspaceId { get; set; } = string.Empty;
    public List<LocalWorkspaceEntry> Workspaces { get; set; } = new();
    public string AppVersion { get; set; } = "0.1.0";
    public Dictionary<string, string> Settings { get; set; } = new();
}

public class LocalWorkspaceEntry
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Mode { get; set; } = "local";
    public string? LocalDbPath { get; set; }
    public string? LocalVaultPath { get; set; }
}
