using KnowledgeEngine.Application.Services;
using Microsoft.Extensions.DependencyInjection;

namespace KnowledgeEngine.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        // Phase 1 services
        services.AddScoped<AuthService>();
        services.AddScoped<TopicService>();
        services.AddScoped<SourceService>();
        services.AddScoped<FileStorageService>();
        services.AddScoped<IngestJobService>();

        // Phase 2 services
        services.AddScoped<DocumentService>();
        services.AddScoped<EntityService>();
        services.AddScoped<TagService>();
        services.AddScoped<AiJobService>();

        // Phase 2 Inbox services (§17.2, §17.4)
        services.AddScoped<InboxService>();
        services.AddScoped<CloudInboxSyncService>();
        services.AddScoped<MediaProcessingService>();

        return services;
    }
}
