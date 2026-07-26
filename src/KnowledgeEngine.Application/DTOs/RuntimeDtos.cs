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

public class WorkspaceRuntimeHealthDto
{
    public Guid? WorkspaceId { get; set; }
    public string? WorkspaceName { get; set; }
    public string? WorkspaceMode { get; set; }
    public string KnowledgeStorage { get; set; } = "unknown";
    public string FileStorage { get; set; } = "unknown";
    public string BackgroundProcessing { get; set; } = "unknown";
    public string AiService { get; set; } = "unknown";
    public string EmbeddingService { get; set; } = "unknown";
    public string CloudSync { get; set; } = "not_configured";
    public string Overall { get; set; } = "unknown";
    public List<string> Issues { get; set; } = [];
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
    public string Status { get; set; } = "coming_soon";
    public string? Badge { get; set; }
    public string? Reason { get; set; }
    public bool RequiresAuthentication { get; set; }
    public string? MinimumCloudApiVersion { get; set; }
}

public class DesktopCapabilitiesDto
{
    public List<WorkspaceModeOption> Modes { get; set; } = [];
    public DesktopFeatureCapabilityDto CloudInbox { get; set; } = new();
    public string CapabilityVersion { get; set; } = "1.0";
    public DateTime CheckedAt { get; set; }
}

public class DesktopFeatureCapabilityDto
{
    public string Feature { get; set; } = string.Empty;
    public bool Available { get; set; }
    public string Status { get; set; } = "coming_soon";
    public string? Badge { get; set; }
    public string? Reason { get; set; }
    public bool RequiresAuthentication { get; set; }
}

public class CloudApiCapabilitiesDto
{
    public string ApiVersion { get; set; } = "1.0";
    public List<string> Features { get; set; } = [];
}

public class DesktopCloudConnectionStatusDto
{
    public string Status { get; set; } = "not_connected";
    public Guid? CloudAccountBindingId { get; set; }
    public string? AccountDisplayName { get; set; }
    public string? AccountEmailMasked { get; set; }
    public string? CloudApiHost { get; set; }
    public string? CloudWorkspaceId { get; set; }
    public DateTime? LastAuthenticatedAt { get; set; }
    public bool RequiresReauthentication { get; set; }
}

public class DesktopRuntimeStateDto
{
    public Guid? LocalWorkspaceId { get; set; }
    public string? WorkspaceName { get; set; }
    public string Mode { get; set; } = "unconfigured";
    public string RouteTarget { get; set; } = "none";
    public string ConnectionStatus { get; set; } = "not_connected";
    public string? CloudWorkspaceId { get; set; }
    public long Generation { get; set; }
    public bool LocalFallbackAllowed { get; set; }
}

public class ModelProviderOption
{
    public string Provider { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string? DefaultBaseUrl { get; set; }
    public bool RequiresApiKey { get; set; }
}
