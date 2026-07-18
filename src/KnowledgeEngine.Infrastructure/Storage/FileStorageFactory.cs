using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Application.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KnowledgeEngine.Infrastructure.Storage;

/// <summary>
/// Factory that resolves the correct IFileStorageProvider based on workspace mode.
/// - local mode → LocalFileStorageProvider
/// - cloud mode → MinioStorageProvider
/// </summary>
public class FileStorageFactory : IFileStorageFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IConfigService _configService;
    private readonly ILogger<FileStorageFactory> _logger;
    private readonly bool _isLocalDatabase;

    public FileStorageFactory(
        IServiceProvider serviceProvider,
        IConfigService configService,
        IConfiguration configuration,
        ILogger<FileStorageFactory> logger)
    {
        _serviceProvider = serviceProvider;
        _configService = configService;
        _logger = logger;
        _isLocalDatabase = string.Equals(
            configuration["DatabaseProvider"], "sqlite", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<IFileStorageProvider> GetProviderAsync(CancellationToken ct = default)
    {
        var configWsId = await _configService.GetCurrentWorkspaceIdAsync(ct);
        if (configWsId != null)
        {
            return await GetProviderForWorkspaceAsync(configWsId, ct);
        }

        if (_isLocalDatabase)
        {
            return _serviceProvider.GetRequiredService<LocalFileStorageProvider>();
        }

        // Default: return MinIO only in cloud mode.
        return _serviceProvider.GetRequiredService<MinioStorageProvider>();
    }

    public async Task<IFileStorageProvider> GetProviderForWorkspaceAsync(string workspaceId, CancellationToken ct = default)
    {
        // Resolve the workspace from DB to check its fileProvider
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();

        if (Guid.TryParse(workspaceId, out var wid))
        {
            var workspace = await db.Workspaces.FirstOrDefaultAsync(w => w.Id == wid, ct);
            if (workspace != null)
            {
                if (workspace.FileProvider == "local_fs")
                {
                    _logger.LogDebug("Using LocalFileStorageProvider for workspace {Id}", workspaceId);
                    if (!string.IsNullOrWhiteSpace(workspace.LocalVaultPath))
                    {
                        return new LocalFileStorageProvider(
                            workspace.LocalVaultPath,
                            _serviceProvider.GetRequiredService<ILogger<LocalFileStorageProvider>>());
                    }
                    return _serviceProvider.GetRequiredService<LocalFileStorageProvider>();
                }
            }
        }

        // File objects currently store the owning user id in WorkspaceId. That id
        // is not necessarily the runtime Workspace entity id. In desktop/SQLite
        // mode, falling through to MinIO would make parsing contact localhost:9000.
        // Resolve the active local workspace so its selected Vault is preserved.
        if (_isLocalDatabase)
        {
            var currentWorkspaceId = await _configService.GetCurrentWorkspaceIdAsync(ct);
            if (Guid.TryParse(currentWorkspaceId, out var currentId))
            {
                var currentWorkspace = await db.Workspaces.FirstOrDefaultAsync(w => w.Id == currentId, ct);
                if (currentWorkspace?.FileProvider == "local_fs"
                    && !string.IsNullOrWhiteSpace(currentWorkspace.LocalVaultPath))
                {
                    _logger.LogDebug(
                        "Using active workspace Vault for local file owned by {Id}", workspaceId);
                    return new LocalFileStorageProvider(
                        currentWorkspace.LocalVaultPath,
                        _serviceProvider.GetRequiredService<ILogger<LocalFileStorageProvider>>());
                }
            }

            _logger.LogDebug("Using default local storage for file owned by {Id}", workspaceId);
            return _serviceProvider.GetRequiredService<LocalFileStorageProvider>();
        }

        // Default: MinIO only for cloud deployments.
        _logger.LogDebug("Using MinioStorageProvider for workspace {Id}", workspaceId);
        return _serviceProvider.GetRequiredService<MinioStorageProvider>();
    }
}
