namespace KnowledgeEngine.Domain.Entities;

/// <summary>
/// Represents a LAN compute node that can execute audio capabilities
/// (transcription, synthesis, etc.) on behalf of the local device.
/// Nodes are discovered via UDP broadcast / mDNS or manually registered.
/// </summary>
public class LanNode
{
    public Guid Id { get; set; }

    /// <summary>Human-friendly node name (e.g. " workstation-gpu-01").</summary>
    public string NodeName { get; set; } = string.Empty;

    /// <summary>Base URL of the LAN node's HTTP API (e.g. http://192.168.1.50:8080).</summary>
    public string EndpointUrl { get; set; } = string.Empty;

    /// <summary>online / offline / health_checking — see <see cref="LanNodeStatuses"/>.</summary>
    public string NodeStatus { get; set; } = LanNodeStatuses.Online;

    /// <summary>
    /// Comma-separated capability identifiers this node supports
    /// (e.g. "audio.transcription,audio.synthesis").
    /// </summary>
    public string Capabilities { get; set; } = string.Empty;

    /// <summary>
    /// Comma-separated provider IDs available on the node
    /// (e.g. "whisper_cpp,funasr").
    /// </summary>
    public string ProviderIds { get; set; } = string.Empty;

    /// <summary>Available GPU memory in MB, if the node has a GPU.</summary>
    public long? AvailableGpuMemory { get; set; }

    /// <summary>Number of CPU cores available on the node.</summary>
    public int? CpuCores { get; set; }

    /// <summary>UTC timestamp of the last heartbeat received from the node.</summary>
    public DateTime? LastHeartbeatAt { get; set; }

    public DateTime RegisteredAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// Status values for <see cref="LanNode.NodeStatus"/>.
/// </summary>
public static class LanNodeStatuses
{
    public const string Online = "online";
    public const string Offline = "offline";
    public const string HealthChecking = "health_checking";
}
