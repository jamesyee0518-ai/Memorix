using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Application.Settings;
using KnowledgeEngine.Infrastructure.Ai;
using KnowledgeEngine.Infrastructure.Storage;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KnowledgeEngine.Infrastructure.Runtime;

/// <summary>
/// Runtime Router: resolves the correct implementation based on workspace mode.
///
/// local mode  → LocalKnowledgeRepository (SQLite), LocalFileStorage, LocalJobQueue, UnifiedModelProvider
/// cloud mode  → CloudKnowledgeRepository (PostgreSQL), MinioStorage, (CloudJobQueue stub), UnifiedModelProvider
/// hybrid mode → same as local for Phase 1
///
/// This is the core abstraction that lets business code switch implementations
/// without being aware of the underlying store.
/// </summary>
public class RuntimeRouter
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IConfigService _configService;
    private readonly IAppDbContext _db;
    private readonly ILogger<RuntimeRouter> _logger;
    private readonly LocalFileStorageSettings _localFsSettings;

    public RuntimeRouter(
        IServiceProvider serviceProvider,
        IConfigService configService,
        IAppDbContext db,
        ILogger<RuntimeRouter> logger,
        IOptions<LocalFileStorageSettings> localFsSettings)
    {
        _serviceProvider = serviceProvider;
        _configService = configService;
        _db = db;
        _logger = logger;
        _localFsSettings = localFsSettings.Value;
    }

    /// <summary>
    /// Gets the current workspace mode.
    /// </summary>
    public async Task<string> GetCurrentModeAsync(CancellationToken ct = default)
    {
        var ws = await GetCurrentWorkspaceAsync(ct);
        return ws?.Mode ?? "cloud";
    }

    /// <summary>
    /// Gets the file provider for the current workspace.
    /// </summary>
    public async Task<string> GetCurrentFileProviderAsync(CancellationToken ct = default)
    {
        var ws = await GetCurrentWorkspaceAsync(ct);
        return ws?.FileProvider ?? "minio";
    }

    /// <summary>
    /// Gets the model provider for the current workspace.
    /// </summary>
    public async Task<string> GetCurrentModelProviderAsync(CancellationToken ct = default)
    {
        var ws = await GetCurrentWorkspaceAsync(ct);
        return ws?.ModelProvider ?? "lmstudio";
    }

    /// <summary>
    /// Returns the appropriate IKnowledgeRepository for the current workspace.
    /// local → LocalKnowledgeRepository (SQLite)
    /// cloud → CloudKnowledgeRepository (PostgreSQL via IAppDbContext)
    /// </summary>
    public async Task<IKnowledgeRepository> GetRepositoryAsync(CancellationToken ct = default)
    {
        var ws = await GetCurrentWorkspaceAsync(ct);
        var mode = ws?.Mode ?? "cloud";

        if (mode == "local" || mode == "hybrid")
        {
            var dbPath = GetLocalDbPath(ws);
            var logger = _serviceProvider.GetRequiredService<ILogger<LocalKnowledgeRepository>>();
            _logger.LogInformation("RuntimeRouter: using LocalKnowledgeRepository (SQLite at {Path})", dbPath);
            return new LocalKnowledgeRepository(dbPath, logger);
        }

        _logger.LogInformation("RuntimeRouter: using CloudKnowledgeRepository (PostgreSQL)");
        return _serviceProvider.GetRequiredService<CloudKnowledgeRepository>();
    }

    /// <summary>
    /// Returns the appropriate IFileStorageProvider for the current workspace.
    /// local → LocalFileStorageProvider
    /// cloud → MinioStorageProvider
    /// </summary>
    public async Task<IFileStorageProvider> GetFileStorageAsync(CancellationToken ct = default)
    {
        var ws = await GetCurrentWorkspaceAsync(ct);
        var mode = ws?.Mode ?? "cloud";

        if (mode == "local" || mode == "hybrid")
        {
            _logger.LogInformation("RuntimeRouter: using LocalFileStorageProvider");
            if (!string.IsNullOrWhiteSpace(ws?.LocalVaultPath))
            {
                return new LocalFileStorageProvider(
                    ws.LocalVaultPath,
                    _serviceProvider.GetRequiredService<ILogger<LocalFileStorageProvider>>());
            }
            return _serviceProvider.GetRequiredService<LocalFileStorageProvider>();
        }

        _logger.LogInformation("RuntimeRouter: using MinioStorageProvider");
        return _serviceProvider.GetRequiredService<MinioStorageProvider>();
    }

    /// <summary>
    /// Returns the appropriate IJobQueue for the current workspace.
    /// local → LocalJobQueue (SQLite)
    /// cloud → CloudJobQueue (stub, not yet implemented)
    /// </summary>
    public async Task<IJobQueue> GetJobQueueAsync(CancellationToken ct = default)
    {
        var ws = await GetCurrentWorkspaceAsync(ct);
        var mode = ws?.Mode ?? "cloud";

        if (mode == "local" || mode == "hybrid")
        {
            var dbPath = GetLocalDbPath(ws);
            var logger = _serviceProvider.GetRequiredService<ILogger<LocalJobQueue>>();
            _logger.LogInformation("RuntimeRouter: using LocalJobQueue (SQLite at {Path})", dbPath);
            return new LocalJobQueue(dbPath, logger);
        }

        _logger.LogInformation("RuntimeRouter: using CloudJobQueue (stub)");
        return _serviceProvider.GetRequiredService<CloudJobQueue>();
    }

    /// <summary>
    /// Returns the IModelProvider for the current workspace, distinguishing local vs cloud mode.
    /// local mode  → auto-detects Ollama (:11434) or LM Studio (:1234), falls back to workspace config
    /// cloud mode  → uses configured cloud model API (OpenAI / Anthropic / custom)
    /// hybrid mode → same as local for Phase 1
    /// The actual model endpoint is configured via appsettings.json (Llm/Embedding sections).
    /// </summary>
    public async Task<IModelProvider> GetModelProviderAsync(CancellationToken ct = default)
    {
        try
        {
            var ws = await GetCurrentWorkspaceAsync(ct);
            var mode = ws?.Mode ?? "cloud";
            var providerName = ws?.ModelProvider ?? "lmstudio";

            var configuredLlm = _serviceProvider.GetRequiredService<IOptions<LlmSettings>>().Value;
            var embedding = _serviceProvider.GetRequiredService<IEmbeddingService>();
            var routedSettings = new LlmSettings
            {
                Endpoint = configuredLlm.Endpoint,
                ApiKey = configuredLlm.ApiKey,
                Model = configuredLlm.Model,
                MaxTokens = configuredLlm.MaxTokens
            };
            var embeddingSettings = _serviceProvider.GetRequiredService<IOptions<EmbeddingSettings>>();
            var httpClientFactory = _serviceProvider.GetRequiredService<IHttpClientFactory>();
            var logger = _serviceProvider.GetRequiredService<ILogger<UnifiedModelProvider>>();

            ApplyWorkspaceModelConfig(ws?.ModelConfig, routedSettings);

            if (mode == "local" || mode == "hybrid")
            {
                // Respect an explicitly selected provider. Auto-detection is only
                // used for an unset/auto selection, so Ollama cannot unexpectedly
                // override a workspace configured for LM Studio (or vice versa).
                if (string.IsNullOrWhiteSpace(providerName) || providerName == "auto")
                {
                    var detected = await DetectLocalModelProviderAsync(httpClientFactory, ct);
                    if (string.IsNullOrEmpty(detected))
                    {
                        throw new InvalidOperationException("No local model provider is running (checked Ollama and LM Studio)");
                    }
                    providerName = detected;
                }

                if (!HasExplicitBaseUrl(ws?.ModelConfig))
                {
                    routedSettings.Endpoint = DefaultEndpoint(providerName);
                }
                _logger.LogInformation("RuntimeRouter: local mode → {Provider} at {Endpoint}", providerName, routedSettings.Endpoint);
            }
            else
            {
                // Cloud mode: use configured cloud API provider
                _logger.LogInformation("RuntimeRouter: cloud mode → provider={Provider} at {Endpoint}", providerName, routedSettings.Endpoint);
            }

            var llmClient = httpClientFactory.CreateClient(nameof(OpenAiLlmService));
            var llmLogger = _serviceProvider.GetRequiredService<ILogger<OpenAiLlmService>>();
            var llm = new OpenAiLlmService(llmClient, Options.Create(routedSettings), llmLogger);
            return new UnifiedModelProvider(llm, embedding, Options.Create(routedSettings), embeddingSettings, httpClientFactory, logger, providerName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RuntimeRouter: failed to resolve model provider");
            throw new InvalidOperationException($"Failed to resolve model provider: {ex.Message}", ex);
        }
    }

    private static string DefaultEndpoint(string providerName) => providerName.ToLowerInvariant() switch
    {
        "ollama" => "http://localhost:11434",
        "lmstudio" => "http://localhost:1234",
        "openai" => "https://api.openai.com",
        _ => "http://localhost:1234"
    };

    private static bool HasExplicitBaseUrl(string? modelConfig)
    {
        if (string.IsNullOrWhiteSpace(modelConfig)) return false;
        try
        {
            using var document = JsonDocument.Parse(modelConfig);
            return document.RootElement.TryGetProperty("baseUrl", out var value)
                && value.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(value.GetString());
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static void ApplyWorkspaceModelConfig(string? modelConfig, LlmSettings settings)
    {
        if (string.IsNullOrWhiteSpace(modelConfig)) return;
        JsonDocument document;
        try { document = JsonDocument.Parse(modelConfig); }
        catch (JsonException) { return; }
        using (document)
        {
            var root = document.RootElement;
            if (root.TryGetProperty("baseUrl", out var baseUrl)
                && baseUrl.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(baseUrl.GetString()))
                settings.Endpoint = baseUrl.GetString()!;
            if (root.TryGetProperty("apiKey", out var apiKey) && apiKey.ValueKind == JsonValueKind.String)
                settings.ApiKey = apiKey.GetString() ?? string.Empty;
            if (root.TryGetProperty("chatModel", out var chatModel)
                && chatModel.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(chatModel.GetString()))
                settings.Model = chatModel.GetString()!;
        }
    }

    /// <summary>
    /// Auto-detects which local model service is running.
    /// Checks Ollama (default :11434) and LM Studio (default :1234).
    /// Returns "ollama", "lmstudio", or null if neither is reachable.
    /// </summary>
    private async Task<string?> DetectLocalModelProviderAsync(IHttpClientFactory httpClientFactory, CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(3);

        // Check Ollama (default port 11434)
        try
        {
            var resp = await client.GetAsync("http://localhost:11434/api/tags", ct);
            if (resp.IsSuccessStatusCode)
            {
                _logger.LogInformation("Auto-detect: Ollama is reachable at localhost:11434");
                return "ollama";
            }
        }
        catch { /* Ollama not running */ }

        // Check LM Studio (default port 1234)
        try
        {
            var resp = await client.GetAsync("http://localhost:1234/v1/models", ct);
            if (resp.IsSuccessStatusCode)
            {
                _logger.LogInformation("Auto-detect: LM Studio is reachable at localhost:1234");
                return "lmstudio";
            }
        }
        catch { /* LM Studio not running */ }

        return null;
    }

    /// <summary>
    /// Ensures the local runtime is initialized (SQLite database + Vault directory).
    /// Called when a local workspace is created or on startup.
    /// </summary>
    public async Task EnsureLocalRuntimeInitializedAsync(string workspaceId, string vaultPath, CancellationToken ct = default)
    {
        // Initialize SQLite database
        var dbPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".knowledge-engine",
            $"workspace-{workspaceId}.db");

        var sqliteInitializer = _serviceProvider.GetRequiredService<SqliteInitializer>();
        await sqliteInitializer.InitializeAsync(dbPath, ct);

        // Initialize Vault directory
        if (!string.IsNullOrEmpty(vaultPath))
        {
            var vaultDirs = new[] { "inbox", "sources", "documents", "attachments", "exports", "reports", "snapshots" };
            foreach (var dir in vaultDirs)
                Directory.CreateDirectory(Path.Combine(vaultPath, dir));
        }

        _logger.LogInformation("Local runtime initialized: SQLite at {DbPath}, Vault at {Vault}", dbPath, vaultPath);
    }

    // ===== Private helpers =====

    private async Task<Domain.Entities.Workspace?> GetCurrentWorkspaceAsync(CancellationToken ct)
    {
        var wsId = await _configService.GetCurrentWorkspaceIdAsync(ct);
        if (wsId != null && Guid.TryParse(wsId, out var id))
        {
            return await _db.Workspaces.FirstOrDefaultAsync(w => w.Id == id, ct);
        }
        return null;
    }

    private string GetLocalDbPath(Domain.Entities.Workspace? ws)
    {
        if (!string.IsNullOrEmpty(ws?.LocalDbPath))
            return ws.LocalDbPath;

        // Default path: ~/.knowledge-engine/workspace-{id}.db
        var workspaceId = ws?.Id.ToString() ?? "default";
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".knowledge-engine",
            $"workspace-{workspaceId}.db");
    }
}
