using System.Text.Json;
using KnowledgeEngine.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace KnowledgeEngine.Infrastructure.Runtime;

/// <summary>
/// Manages the local configuration file at ~/.knowledge-engine/config.json.
/// Stores the workspace list, current workspace ID, and app version.
/// </summary>
public class ConfigService : IConfigService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _configDir;
    private readonly string _configPath;
    private readonly ILogger<ConfigService> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public ConfigService(ILogger<ConfigService> logger)
    {
        _configDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".knowledge-engine");
        _configPath = Path.Combine(_configDir, "config.json");
        _logger = logger;
    }

    public async Task<LocalConfig> LoadConfigAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            if (!File.Exists(_configPath))
            {
                _logger.LogInformation("Config file not found, creating default at {Path}", _configPath);
                var defaultConfig = new LocalConfig();
                await SaveConfigInternalAsync(defaultConfig, ct);
                return defaultConfig;
            }

            var json = await File.ReadAllTextAsync(_configPath, ct);
            var config = JsonSerializer.Deserialize<LocalConfig>(json, JsonOptions);
            return config ?? new LocalConfig();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SaveConfigAsync(LocalConfig config, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            await SaveConfigInternalAsync(config, ct);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<string?> GetCurrentWorkspaceIdAsync(CancellationToken ct = default)
    {
        var config = await LoadConfigAsync(ct);
        return string.IsNullOrEmpty(config.CurrentWorkspaceId) ? null : config.CurrentWorkspaceId;
    }

    public async Task SetCurrentWorkspaceIdAsync(string workspaceId, CancellationToken ct = default)
    {
        var config = await LoadConfigAsync(ct);
        config.CurrentWorkspaceId = workspaceId;
        await SaveConfigAsync(config, ct);
    }

    private async Task SaveConfigInternalAsync(LocalConfig config, CancellationToken ct)
    {
        Directory.CreateDirectory(_configDir);
        var json = JsonSerializer.Serialize(config, JsonOptions);
        await File.WriteAllTextAsync(_configPath, json, ct);
        _logger.LogDebug("Config saved to {Path}", _configPath);
    }
}
