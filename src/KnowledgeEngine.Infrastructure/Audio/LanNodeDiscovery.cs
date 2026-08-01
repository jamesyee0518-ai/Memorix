using System.Net.Http.Json;
using System.Text.Json;
using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Application.Settings;
using KnowledgeEngine.Domain.Entities;
using KnowledgeEngine.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KnowledgeEngine.Infrastructure.Audio;

/// <summary>
/// LAN node discovery service that probes configured endpoints, manages
/// node lifecycle in the database, and provides healthy node selection
/// for capability delegation.
/// </summary>
public class LanNodeDiscovery : ILanNodeDiscovery
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IAppDbContext _db;
    private readonly AudioSettings _settings;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<LanNodeDiscovery> _logger;

    /// <summary>
    /// Creates a new <see cref="LanNodeDiscovery"/>.
    /// </summary>
    /// <param name="db">Application database context for node persistence.</param>
    /// <param name="options">Audio settings (provides <see cref="AudioSettings.LanNodeEndpoints"/>).</param>
    /// <param name="httpClientFactory">HTTP client factory for probing node endpoints.</param>
    /// <param name="logger">Logger for diagnostics.</param>
    public LanNodeDiscovery(
        IAppDbContext db,
        IOptions<AudioSettings> options,
        IHttpClientFactory httpClientFactory,
        ILogger<LanNodeDiscovery> logger)
    {
        _db = db;
        _settings = options.Value;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<List<LanNode>> DiscoverNodesAsync(CancellationToken ct)
    {
        var endpoints = ParseEndpoints(_settings.LanNodeEndpoints);
        var discovered = new List<LanNode>();

        _logger.LogInformation("Starting LAN node discovery for {Count} configured endpoint(s).", endpoints.Count);

        foreach (var endpoint in endpoints)
        {
            try
            {
                var node = await ProbeAndRegisterAsync(endpoint, ct);
                if (node != null)
                {
                    discovered.Add(node);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to discover LAN node at {Endpoint}: {Message}", endpoint, ex.Message);
            }
        }

        _logger.LogInformation("LAN node discovery completed: {Count} node(s) online.", discovered.Count);
        return discovered;
    }

    /// <inheritdoc/>
    public async Task<LanNode> RegisterNodeAsync(string endpoint, CancellationToken ct)
    {
        var normalized = endpoint.TrimEnd('/');

        // Check if a node with the same endpoint already exists.
        var existing = await _db.LanNodes
            .FirstOrDefaultAsync(n => n.EndpointUrl == normalized, ct);

        if (existing != null)
        {
            existing.NodeStatus = LanNodeStatuses.Online;
            existing.LastHeartbeatAt = DateTime.UtcNow;
            existing.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("Re-registered LAN node {NodeId} at {Endpoint}.", existing.Id, normalized);
            return existing;
        }

        var node = new LanNode
        {
            Id = Guid.NewGuid(),
            NodeName = ExtractNodeName(normalized),
            EndpointUrl = normalized,
            NodeStatus = LanNodeStatuses.Online,
            Capabilities = AudioCapabilities.Transcription,
            ProviderIds = string.Empty,
            LastHeartbeatAt = DateTime.UtcNow,
            RegisteredAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _db.LanNodes.Add(node);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Registered new LAN node {NodeId} at {Endpoint}.", node.Id, normalized);
        return node;
    }

    /// <inheritdoc/>
    public async Task<bool> UnregisterNodeAsync(Guid nodeId, CancellationToken ct)
    {
        var node = await _db.LanNodes.FirstOrDefaultAsync(n => n.Id == nodeId, ct);
        if (node == null)
        {
            return false;
        }

        _db.LanNodes.Remove(node);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Unregistered LAN node {NodeId}.", nodeId);
        return true;
    }

    /// <inheritdoc/>
    public async Task<LanNode?> GetHealthyNodeAsync(string capability, CancellationToken ct)
    {
        var timeoutSec = _settings.LanNodeHeartbeatTimeoutSec;
        var cutoff = DateTime.UtcNow.AddSeconds(-timeoutSec);

        // Query online nodes that advertise the requested capability.
        var candidates = await _db.LanNodes
            .Where(n => n.NodeStatus == LanNodeStatuses.Online)
            .ToListAsync(ct);

        var healthy = candidates
            .Where(n => n.LastHeartbeatAt.HasValue && n.LastHeartbeatAt.Value >= cutoff)
            .Where(n => ContainsCapability(n.Capabilities, capability))
            .OrderByDescending(n => n.AvailableGpuMemory ?? 0)
            .ThenByDescending(n => n.CpuCores ?? 0)
            .FirstOrDefault();

        if (healthy != null)
        {
            _logger.LogDebug(
                "Selected healthy LAN node {NodeId} for capability '{Capability}'.",
                healthy.Id, capability);
        }

        return healthy;
    }

    /// <inheritdoc/>
    public async Task UpdateHeartbeatAsync(Guid nodeId, CancellationToken ct)
    {
        var node = await _db.LanNodes.FirstOrDefaultAsync(n => n.Id == nodeId, ct);
        if (node == null)
        {
            _logger.LogWarning("Heartbeat update failed: LAN node {NodeId} not found.", nodeId);
            return;
        }

        node.LastHeartbeatAt = DateTime.UtcNow;
        node.NodeStatus = LanNodeStatuses.Online;
        node.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    // ── Private helpers ──

    /// <summary>
    /// Probes a single endpoint by issuing a health-check HTTP GET.
    /// If the node responds, it is registered (or re-registered) in the database.
    /// </summary>
    private async Task<LanNode?> ProbeAndRegisterAsync(string endpoint, CancellationToken ct)
    {
        using var httpClient = _httpClientFactory.CreateClient();
        httpClient.Timeout = TimeSpan.FromSeconds(5);

        try
        {
            _logger.LogDebug("Probing LAN node at {Endpoint}...", endpoint);

            using var response = await httpClient.GetAsync(
                $"{endpoint.TrimEnd('/')}/health",
                ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "LAN node at {Endpoint} returned HTTP {StatusCode}.",
                    endpoint, (int)response.StatusCode);

                // Mark as offline if already registered.
                await MarkOfflineAsync(endpoint, ct);
                return null;
            }

            // Try to read node info from the health response body.
            LanNodeInfo? nodeInfo = null;
            try
            {
                nodeInfo = await response.Content.ReadFromJsonAsync<LanNodeInfo>(JsonOptions, ct);
            }
            catch
            {
                // Non-JSON or empty body — use defaults.
            }

            var node = await RegisterNodeAsync(endpoint, ct);

            // Enrich with info from the node's health response.
            if (nodeInfo != null)
            {
                node.NodeName = !string.IsNullOrWhiteSpace(nodeInfo.NodeName)
                    ? nodeInfo.NodeName
                    : node.NodeName;
                node.Capabilities = !string.IsNullOrWhiteSpace(nodeInfo.Capabilities)
                    ? nodeInfo.Capabilities
                    : node.Capabilities;
                node.ProviderIds = nodeInfo.ProviderIds ?? node.ProviderIds;
                node.AvailableGpuMemory = nodeInfo.AvailableGpuMemory ?? node.AvailableGpuMemory;
                node.CpuCores = nodeInfo.CpuCores ?? node.CpuCores;
                node.UpdatedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync(ct);
            }

            return node;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("LAN node probe failed for {Endpoint}: {Message}", endpoint, ex.Message);
            await MarkOfflineAsync(endpoint, ct);
            return null;
        }
    }

    /// <summary>
    /// Marks a node at the given endpoint as offline (if registered).
    /// </summary>
    private async Task MarkOfflineAsync(string endpoint, CancellationToken ct)
    {
        var normalized = endpoint.TrimEnd('/');
        var existing = await _db.LanNodes
            .FirstOrDefaultAsync(n => n.EndpointUrl == normalized, ct);

        if (existing != null && existing.NodeStatus != LanNodeStatuses.Offline)
        {
            existing.NodeStatus = LanNodeStatuses.Offline;
            existing.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
        }
    }

    /// <summary>
    /// Parses a comma-separated endpoint string into a list of URLs.
    /// </summary>
    private static List<string> ParseEndpoints(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return [];
        }

        return raw
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(u => Uri.TryCreate(u, UriKind.Absolute, out _))
            .ToList();
    }

    /// <summary>
    /// Checks whether the comma-separated capability list contains the requested capability.
    /// </summary>
    private static bool ContainsCapability(string capabilitiesCsv, string capability)
    {
        if (string.IsNullOrWhiteSpace(capabilitiesCsv))
        {
            return false;
        }

        return capabilitiesCsv
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(c => string.Equals(c, capability, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Extracts a human-friendly node name from the endpoint URL.
    /// </summary>
    private static string ExtractNodeName(string endpoint)
    {
        return Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)
            ? $"lan-node-{uri.Host}"
            : "lan-node";
    }

    /// <summary>
    /// Lightweight DTO for deserializing the LAN node health response.
    /// </summary>
    private sealed class LanNodeInfo
    {
        public string? NodeName { get; set; }
        public string? Capabilities { get; set; }
        public string? ProviderIds { get; set; }
        public long? AvailableGpuMemory { get; set; }
        public int? CpuCores { get; set; }
    }
}
