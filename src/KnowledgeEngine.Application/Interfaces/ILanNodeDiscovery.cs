using KnowledgeEngine.Domain.Entities;

namespace KnowledgeEngine.Application.Interfaces;

/// <summary>
/// LAN node discovery and lifecycle management.
/// Discovers compute nodes on the local network, tracks heartbeats,
/// and provides healthy node selection for capability delegation.
/// </summary>
public interface ILanNodeDiscovery
{
    /// <summary>
    /// Discovers LAN nodes by pinging configured endpoints and returning
    /// all reachable nodes. New nodes are registered in the database.
    /// </summary>
    Task<List<LanNode>> DiscoverNodesAsync(CancellationToken ct);

    /// <summary>
    /// Registers (or re-registers) a LAN node at the given endpoint URL.
    /// If a node with the same endpoint already exists, its status is updated.
    /// </summary>
    Task<LanNode> RegisterNodeAsync(string endpoint, CancellationToken ct);

    /// <summary>
    /// Unregisters (removes) a LAN node by ID.
    /// Returns true if the node was found and removed.
    /// </summary>
    Task<bool> UnregisterNodeAsync(Guid nodeId, CancellationToken ct);

    /// <summary>
    /// Finds a healthy online node that supports the requested capability.
    /// A node is considered healthy if its last heartbeat is within the
    /// configured freshness window (default 60 seconds).
    /// </summary>
    Task<LanNode?> GetHealthyNodeAsync(string capability, CancellationToken ct);

    /// <summary>
    /// Updates the heartbeat timestamp for the given node, marking it as online.
    /// </summary>
    Task UpdateHeartbeatAsync(Guid nodeId, CancellationToken ct);
}
