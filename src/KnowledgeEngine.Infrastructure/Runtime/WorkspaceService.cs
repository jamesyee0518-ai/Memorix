using System.Text.Json;
using KnowledgeEngine.Application.DTOs;
using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Domain.Entities;
using KnowledgeEngine.Application.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KnowledgeEngine.Infrastructure.Runtime;

/// <summary>
/// Manages workspace lifecycle: creation, switching, configuration.
/// Bridges the database Workspace entity and the local config file.
/// </summary>
public class WorkspaceService : IWorkspaceService
{
    private readonly IAppDbContext _db;
    private readonly IConfigService _configService;
    private readonly ILogger<WorkspaceService> _logger;

    public WorkspaceService(
        IAppDbContext db,
        IConfigService configService,
        ILogger<WorkspaceService> logger)
    {
        _db = db;
        _configService = configService;
        _logger = logger;
    }

    public async Task<WorkspaceDto> CreateWorkspaceAsync(CreateWorkspaceDto input, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var workspace = new Workspace
        {
            Id = Guid.NewGuid(),
            Name = input.Name,
            Mode = input.Mode,
            StorageProvider = input.StorageProvider,
            FileProvider = input.FileProvider,
            JobProvider = input.JobProvider,
            ModelProvider = input.ModelProvider,
            LocalDbPath = input.LocalDbPath,
            LocalVaultPath = input.LocalVaultPath,
            CloudApiBaseUrl = input.CloudApiBaseUrl,
            CloudWorkspaceId = input.CloudWorkspaceId,
            SyncEnabled = input.SyncEnabled,
            InboxEnabled = input.InboxEnabled,
            ModelConfig = input.ModelConfig,
            CreatedAt = now,
            UpdatedAt = now
        };

        _db.Workspaces.Add(workspace);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Created workspace {Id} ({Name}, mode={Mode})", workspace.Id, workspace.Name, workspace.Mode);

        return MapToDto(workspace);
    }

    public async Task<WorkspaceDto> InitializeLocalWorkspaceAsync(InitLocalWorkspaceDto input, CancellationToken ct = default)
    {
        // Create Vault directory structure
        if (!string.IsNullOrEmpty(input.VaultPath))
        {
            var vaultDirs = new[] { "inbox", "sources", "documents", "attachments", "exports", "reports", "snapshots" };
            foreach (var dir in vaultDirs)
            {
                Directory.CreateDirectory(Path.Combine(input.VaultPath, dir));
            }
            _logger.LogInformation("Initialized Vault at {Path}", input.VaultPath);
        }

        var createDto = new CreateWorkspaceDto
        {
            Name = input.Name,
            Mode = "local",
            StorageProvider = "postgres", // Phase 1 still uses PostgreSQL for the web app
            FileProvider = "local_fs",
            JobProvider = "local_queue",
            ModelProvider = input.ModelProvider ?? "lmstudio",
            LocalVaultPath = input.VaultPath,
            ModelConfig = input.ModelConfig,
            InboxEnabled = true
        };

        var workspace = await CreateWorkspaceAsync(createDto, ct);

        // Save to local config file
        var config = await _configService.LoadConfigAsync(ct);
        config.Workspaces.Add(new LocalWorkspaceEntry
        {
            Id = workspace.Id.ToString(),
            Name = workspace.Name,
            Mode = "local",
            LocalVaultPath = workspace.LocalVaultPath
        });
        config.CurrentWorkspaceId = workspace.Id.ToString();
        await _configService.SaveConfigAsync(config, ct);

        return workspace;
    }

    public async Task<WorkspaceDto?> GetWorkspaceAsync(Guid id, CancellationToken ct = default)
    {
        var workspace = await _db.Workspaces.FirstOrDefaultAsync(w => w.Id == id, ct);
        return workspace != null ? MapToDto(workspace) : null;
    }

    public async Task<WorkspaceDto?> GetCurrentWorkspaceAsync(Guid userId, CancellationToken ct = default)
    {
        // Try to get from local config first
        var configWorkspaceId = await _configService.GetCurrentWorkspaceIdAsync(ct);
        if (configWorkspaceId != null && Guid.TryParse(configWorkspaceId, out var wsId))
        {
            var ws = await _db.Workspaces.FirstOrDefaultAsync(w => w.Id == wsId, ct);
            if (ws != null) return MapToDto(ws);
        }

        // Fallback: get the first workspace for this user, or the first workspace overall
        var workspace = await _db.Workspaces
            .Where(w => w.UserId == userId || w.UserId == null)
            .OrderBy(w => w.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (workspace != null)
        {
            await _configService.SetCurrentWorkspaceIdAsync(workspace.Id.ToString(), ct);
            return MapToDto(workspace);
        }

        return null;
    }

    public async Task<List<WorkspaceDto>> ListWorkspacesAsync(Guid userId, CancellationToken ct = default)
    {
        var workspaces = await _db.Workspaces
            .Where(w => w.UserId == userId || w.UserId == null)
            .OrderByDescending(w => w.CreatedAt)
            .ToListAsync(ct);

        return workspaces.Select(MapToDto).ToList();
    }

    public async Task<WorkspaceDto> UpdateWorkspaceAsync(Guid id, UpdateWorkspaceDto input, CancellationToken ct = default)
    {
        var workspace = await _db.Workspaces.FirstOrDefaultAsync(w => w.Id == id, ct)
            ?? throw new InvalidOperationException($"Workspace {id} not found");

        if (input.Name != null) workspace.Name = input.Name;
        if (input.ModelProvider != null) workspace.ModelProvider = input.ModelProvider;
        if (input.ModelConfig != null) workspace.ModelConfig = input.ModelConfig;
        if (input.SyncEnabled.HasValue) workspace.SyncEnabled = input.SyncEnabled.Value;
        if (input.InboxEnabled.HasValue) workspace.InboxEnabled = input.InboxEnabled.Value;
        if (input.LocalVaultPath != null)
        {
            if (string.IsNullOrWhiteSpace(input.LocalVaultPath))
            {
                throw new ArgumentException("Vault path cannot be empty.", nameof(input.LocalVaultPath));
            }

            var vaultPath = Path.GetFullPath(input.LocalVaultPath);
            var vaultDirs = new[] { "inbox", "sources", "documents", "attachments", "exports", "reports", "snapshots" };
            foreach (var dir in vaultDirs)
            {
                Directory.CreateDirectory(Path.Combine(vaultPath, dir));
            }
            workspace.LocalVaultPath = vaultPath;
        }
        if (input.CloudApiBaseUrl != null) workspace.CloudApiBaseUrl = input.CloudApiBaseUrl;
        if (input.CloudWorkspaceId != null) workspace.CloudWorkspaceId = input.CloudWorkspaceId;

        workspace.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        if (input.LocalVaultPath != null)
        {
            var config = await _configService.LoadConfigAsync(ct);
            var entry = config.Workspaces.FirstOrDefault(w => w.Id == workspace.Id.ToString());
            if (entry != null)
            {
                entry.LocalVaultPath = workspace.LocalVaultPath;
                await _configService.SaveConfigAsync(config, ct);
            }
        }

        return MapToDto(workspace);
    }

    public async Task DeleteWorkspaceAsync(Guid id, CancellationToken ct = default)
    {
        var workspace = await _db.Workspaces.FirstOrDefaultAsync(w => w.Id == id, ct);
        if (workspace != null)
        {
            _db.Workspaces.Remove(workspace);
            await _db.SaveChangesAsync(ct);

            // Remove from local config
            var config = await _configService.LoadConfigAsync(ct);
            config.Workspaces.RemoveAll(w => w.Id == id.ToString());
            if (config.CurrentWorkspaceId == id.ToString())
            {
                config.CurrentWorkspaceId = config.Workspaces.FirstOrDefault()?.Id ?? "";
            }
            await _configService.SaveConfigAsync(config, ct);

            _logger.LogInformation("Deleted workspace {Id}", id);
        }
    }

    public async Task SetCurrentWorkspaceAsync(Guid userId, Guid workspaceId, CancellationToken ct = default)
    {
        await _configService.SetCurrentWorkspaceIdAsync(workspaceId.ToString(), ct);
        _logger.LogInformation("Set current workspace to {Id}", workspaceId);
    }

    private static WorkspaceDto MapToDto(Workspace w) => new()
    {
        Id = w.Id,
        Name = w.Name,
        Mode = w.Mode,
        StorageProvider = w.StorageProvider,
        FileProvider = w.FileProvider,
        JobProvider = w.JobProvider,
        ModelProvider = w.ModelProvider,
        LocalDbPath = w.LocalDbPath,
        LocalVaultPath = w.LocalVaultPath,
        CloudApiBaseUrl = w.CloudApiBaseUrl,
        CloudWorkspaceId = w.CloudWorkspaceId,
        SyncEnabled = w.SyncEnabled,
        InboxEnabled = w.InboxEnabled,
        ModelConfig = w.ModelConfig,
        UserId = w.UserId,
        CreatedAt = w.CreatedAt,
        UpdatedAt = w.UpdatedAt
    };
}
