namespace KnowledgeEngine.Application.Interfaces;

/// <summary>
/// Checks the health of runtime components:
/// database, file storage, model services, job queue.
/// </summary>
public interface IRuntimeHealthService
{
    Task<RuntimeHealthStatus> CheckHealthAsync(CancellationToken ct = default);
    Task<LocalModelDetectionStatus> DetectLocalModelsAsync(CancellationToken ct = default);
}

public class RuntimeHealthStatus
{
    public string Database { get; set; } = "unknown";
    public string FileStorage { get; set; } = "unknown";
    public string JobQueue { get; set; } = "unknown";
    public string LlmService { get; set; } = "unknown";
    public string EmbeddingService { get; set; } = "unknown";
    public string Ollama { get; set; } = "not_configured";
    public string LmStudio { get; set; } = "not_configured";
    public string CloudApi { get; set; } = "not_configured";
    public string Overall { get; set; } = "unknown";
    public string? WorkspaceMode { get; set; }
    public DateTime CheckedAt { get; set; } = DateTime.UtcNow;
}

public class LocalModelDetectionStatus
{
    public LocalModelProviderStatus Ollama { get; set; } = new();
    public LocalModelProviderStatus LmStudio { get; set; } = new();
    public DateTime CheckedAt { get; set; } = DateTime.UtcNow;
}

public class LocalModelProviderStatus
{
    public bool Available { get; set; }
    public string Status { get; set; } = "not_running";
    public string Endpoint { get; set; } = string.Empty;
}
