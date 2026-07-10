using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Application.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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

    public FileStorageFactory(
        IServiceProvider serviceProvider,
        IConfigService configService,
        ILogger<FileStorageFactory> logger)
    {
        _serviceProvider = serviceProvider;
        _configService = configService;
        _logger = logger;
    }

    public async Task<IFileStorageProvider> GetProviderAsync(CancellationToken ct = default)
    {
        var configWsId = await _configService.GetCurrentWorkspaceIdAsync(ct);
        if (configWsId != null)
        {
            return await GetProviderForWorkspaceAsync(configWsId, ct);
        }

        // Default: return MinIO (cloud mode)
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

        // Default: MinIO
        _logger.LogDebug("Using MinioStorageProvider for workspace {Id}", workspaceId);
        return _serviceProvider.GetRequiredService<MinioStorageProvider>();
    }
}
