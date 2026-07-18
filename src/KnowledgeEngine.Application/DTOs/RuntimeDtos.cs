using System.Text.Json.Serialization;

namespace KnowledgeEngine.Application.DTOs;

public class RuntimeHealthDto
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
    public DateTime CheckedAt { get; set; }
}

public class LocalModelDetectionDto
{
    public LocalModelProviderDetectionDto Ollama { get; set; } = new();
    public LocalModelProviderDetectionDto LmStudio { get; set; } = new();
    public DateTime CheckedAt { get; set; }
}

public class LocalModelProviderDetectionDto
{
    public bool Available { get; set; }
    public string Status { get; set; } = "not_running";
    public string Endpoint { get; set; } = string.Empty;
}

public class WorkspaceModeOption
{
    public string Mode { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool Available { get; set; }
}

public class ModelProviderOption
{
    public string Provider { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string? DefaultBaseUrl { get; set; }
    public bool RequiresApiKey { get; set; }
}
