using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Application.Services;
using KnowledgeEngine.Application.Settings;
using KnowledgeEngine.Infrastructure.Ai;
using KnowledgeEngine.Infrastructure.Agent;
using KnowledgeEngine.Infrastructure.Db;
using KnowledgeEngine.Infrastructure.Exports;
using KnowledgeEngine.Infrastructure.Mcp;
using KnowledgeEngine.Infrastructure.Notifications;
using KnowledgeEngine.Infrastructure.Processing;
using KnowledgeEngine.Infrastructure.Processing.Processors;
using KnowledgeEngine.Infrastructure.Reports;
using KnowledgeEngine.Infrastructure.Runtime;
using KnowledgeEngine.Infrastructure.Search;
using KnowledgeEngine.Infrastructure.Security;
using KnowledgeEngine.Infrastructure.Services;
using KnowledgeEngine.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace KnowledgeEngine.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // Settings
        services.Configure<JwtSettings>(configuration.GetSection("Jwt"));
        services.Configure<MinioSettings>(configuration.GetSection("Minio"));
        services.Configure<LlmSettings>(configuration.GetSection("Llm"));
        services.Configure<EmbeddingSettings>(configuration.GetSection("Embedding"));
        services.Configure<LocalFileStorageSettings>(configuration.GetSection("LocalFileStorage"));

        // DbContext
        var databaseProvider = configuration["DatabaseProvider"] ?? "postgres";
        if (string.Equals(databaseProvider, "sqlite", StringComparison.OrdinalIgnoreCase))
        {
            var dbPath = configuration["AppDatabasePath"];
            if (string.IsNullOrWhiteSpace(dbPath))
            {
                dbPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".knowledge-engine",
                    "memorix.db");
            }
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(dbPath))!);
            services.AddDbContext<AppDbContext>(options =>
                options.UseSqlite($"Data Source={dbPath}"));
        }
        else
        {
            var connectionString = configuration.GetConnectionString("DefaultConnection");
            services.AddDbContext<AppDbContext>(options =>
                options.UseNpgsql(connectionString)
                    .UseSnakeCaseNamingConvention());
        }

        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());

        // Security
        services.AddSingleton<IPasswordHasher, PasswordHasher>();
        services.AddSingleton<IJwtTokenService, JwtTokenService>();
        services.AddScoped<ICurrentUserContext, CurrentUserContext>();
        services.AddScoped<IWorkspaceAuthorizationService, WorkspaceAuthorizationService>();
        services.AddSingleton<ICredentialStore, PlatformCredentialStore>();
        services.AddScoped<ILocalIdentityService, LocalIdentityService>();
        services.AddScoped<IBindingService, BindingService>();
        services.AddScoped<IOAuthBindingService, OAuthBindingService>();
        services.AddSingleton<CloudInboxScheduleMonitor>();
        services.AddHostedService<CloudInboxPullWorker>();
        services.AddHttpContextAccessor();

        // Storage
        services.AddSingleton<MinioStorageProvider>();
        services.AddSingleton<LocalFileStorageProvider>();
        if (string.Equals(databaseProvider, "sqlite", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IFileStorageProvider>(sp => sp.GetRequiredService<LocalFileStorageProvider>());
        }
        else
        {
            services.AddSingleton<IFileStorageProvider>(sp => sp.GetRequiredService<MinioStorageProvider>());
        }
        services.AddScoped<IFileStorageFactory, FileStorageFactory>();

        // HTTP clients
        services.AddHttpClient("ContentFetcher");
        services.AddHttpClient("ExpoPush");
        services.AddScoped<IPushNotificationService, ExpoPushNotificationService>();

        // LLM Service
        services.AddHttpClient<OpenAiLlmService>();
        services.AddScoped<ILlmService>(sp =>
        {
            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
            var httpClient = httpClientFactory.CreateClient(nameof(OpenAiLlmService));
            var settings = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<LlmSettings>>();
            var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<OpenAiLlmService>>();
            return new OpenAiLlmService(httpClient, settings, logger);
        });

        // Content Processing
        services.AddScoped<IContentProcessor, ContentProcessor>();

        // Document Pipeline
        services.AddScoped<IDocumentPipeline, DocumentPipeline>();

        // Phase 3 Pipeline Components
        services.AddScoped<ISourceProcessor, UrlProcessor>();
        services.AddScoped<ISourceProcessor, PdfProcessor>();
        services.AddScoped<ISourceProcessor, FileDocumentProcessor>();
        services.AddScoped<ISourceProcessor, TextProcessor>();
        services.AddScoped<SourceProcessorFactory>();
        services.AddScoped<IContentCleaner, ContentCleaner>();
        services.AddScoped<IMarkdownNormalizer, MarkdownNormalizer>();
        services.AddScoped<IAISummaryService, AISummaryService>();
        services.AddSingleton<ILanguageDetectionService, LanguageDetectionService>();
        services.AddSingleton<IContentClassificationService, ContentClassificationService>();
        services.AddSingleton<IChineseNormalizationService, ChineseNormalizationService>();
        services.AddSingleton<IChineseTokenizer, ChineseTokenizer>();
        services.AddScoped<ITerminologyService, TerminologyService>();
        services.AddScoped<IChineseFullTextIndexService, ChineseFullTextIndexService>();
        services.AddScoped<IL1LocalizationService, L1LocalizationService>();
        services.AddSingleton<ILocalizationQualityService, LocalizationQualityService>();
        services.AddScoped<IChunkLocalizationService, ChunkLocalizationService>();
        services.AddScoped<IChunkEnrichmentService, ChunkEnrichmentService>();
        services.AddScoped<IMultiVectorEmbeddingService, MultiVectorEmbeddingService>();
        services.AddScoped<IMultilingualBatchJobService, MultilingualBatchJobService>();

        // Background Service - Phase 2
        services.AddHostedService<DocumentProcessingBackgroundService>();
        services.AddHostedService<MediaProcessingWorker>();
        services.AddHostedService<PushNotificationWorker>();
        services.AddHostedService<MultilingualBatchWorker>();

        // ===== Phase 3 Services =====

        // Embedding Service
        services.AddHttpClient<OpenAiEmbeddingService>();
        services.AddScoped<IEmbeddingService>(sp =>
        {
            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
            var httpClient = httpClientFactory.CreateClient(nameof(OpenAiEmbeddingService));
            var settings = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<EmbeddingSettings>>();
            var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<OpenAiEmbeddingService>>();
            return new OpenAiEmbeddingService(httpClient, settings, logger);
        });

        // Chunking Service
        services.AddScoped<IChunkingService, ChunkingService>();

        // Search Service
        services.AddScoped<ISearchService, SearchService>();
        services.AddSingleton<IRetrievalFusionService, RetrievalFusionService>();
        services.AddSingleton<IRerankerService, HeuristicRerankerService>();

        // Vector Store (pgvector backend for cloud mode)
        services.AddScoped<IVectorStore, PgVectorStore>();

        // QA Service (RAG)
        services.AddScoped<IQaService, QaService>();

        // Background Services - Phase 3
        services.AddHostedService<ChunkWorker>();
        services.AddHostedService<EmbeddingWorker>();

        // Phase 3 Support Services
        services.AddScoped<ISummaryPromptManager, SummaryPromptManager>();
        services.AddScoped<IQualityScorer, QualityScorer>();
        services.AddScoped<IProcessingLogService, ProcessingLogService>();

        // Phase 4 On-demand Workers (invoked via API action endpoints)
        services.AddScoped<ITagWorker, TagWorker>();
        services.AddScoped<IEntityWorker, EntityWorker>();

        // ===== Phase 4 Services =====

        // Report Service
        services.AddScoped<IReportService, ReportService>();

        // Export Service
        services.AddScoped<IExportService, ExportService>();

        // Background Services - Phase 4
        services.AddHostedService<ReportWorker>();
        services.AddHostedService<ExportWorker>();

        // ===== Phase 5 Services =====

        // API Key Service
        services.AddScoped<IApiKeyService, ApiKeyService>();

        // Usage Service
        services.AddScoped<IUsageService, UsageService>();

        // ===== Agent Services =====
        services.AddScoped<IAgentToolService, AgentToolService>();
        services.AddScoped<IAgentPermissionGuard, AgentPermissionGuard>();

        // MCP Server (stdio JSON-RPC 2.0)
        services.AddSingleton<McpServer>();

        // ===== Dual-mode Foundation Services =====

        // Config Service (local config file)
        services.AddScoped<IConfigService, ConfigService>();

        // Workspace Service
        services.AddScoped<IWorkspaceService, WorkspaceService>();

        // Runtime Health Service
        services.AddScoped<IRuntimeHealthService, RuntimeHealthService>();

        // Runtime Router (core dispatch)
        services.AddScoped<RuntimeRouter>();

        // SQLite Initializer
        services.AddSingleton<SqliteInitializer>();

        // Cloud Runtime implementations (stubs for Phase 1)
        services.AddScoped<CloudKnowledgeRepository>();
        services.AddScoped<ICloudKnowledgeRepository>(sp => sp.GetRequiredService<CloudKnowledgeRepository>());
        services.AddScoped<CloudJobQueue>();

        // Local Job Queue (SQLite-backed). LocalJobQueue requires a workspace dbPath,
        // so we resolve it via a factory using the default workspace SQLite path.
        services.AddScoped<IJobQueue>(sp =>
        {
            var dbPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".knowledge-engine",
                "workspace-default.db");
            var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<LocalJobQueue>>();
            return new LocalJobQueue(dbPath, logger);
        });

        // Unified Model Provider
        services.AddScoped<IModelProvider, UnifiedModelProvider>();

        // Knowledge Repository (facade that routes to Local/Cloud via RuntimeRouter)
        services.AddScoped<IKnowledgeRepository, RuntimeRepositoryFacade>();

        // ===== Phase 2 Inbox Services =====

        // Type detector (stateless, can be singleton)
        services.AddSingleton<TypeDetector>();

        // Import service (creates inbox items from text/url/file/mixed)
        services.AddScoped<ImportService>();

        // Duplicate checker (checks for duplicate URLs, content, files)
        services.AddScoped<DuplicateChecker>();

        // Inbox import service (imports inbox items into sources)
        services.AddScoped<InboxImportService>();

        // Topic suggestor (keyword-based topic and title suggestions)
        services.AddScoped<TopicSuggestor>();

        return services;
    }
}
