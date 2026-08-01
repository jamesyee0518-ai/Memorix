using System.Collections.Concurrent;
using KnowledgeEngine.Application.DTOs;
using KnowledgeEngine.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace KnowledgeEngine.Infrastructure.Audio;

/// <summary>
/// Monitors TTS provider health and provides an automatic degradation chain
/// for selecting the best available provider. The chain order is:
/// <c>FishSpeech → Piper → SystemTTS</c>.
/// <para>
/// Health check results are cached with a configurable TTL to avoid excessive
/// probing. When the highest-quality provider is unhealthy, the monitor falls
/// back to the next provider in the chain, ensuring TTS is always available.
/// </para>
/// </summary>
public class TtsDegradationMonitor
{
    /// <summary>
    /// The degradation chain in priority order (highest quality first).
    /// </summary>
    private static readonly string[] DegradationChain = ["fish_speech", "piper", "system_tts"];

    private static readonly TimeSpan HealthCacheTtl = TimeSpan.FromSeconds(30);

    private readonly IProviderRegistry _registry;
    private readonly ILogger<TtsDegradationMonitor> _logger;

    // Cached health results keyed by provider ID.
    private readonly ConcurrentDictionary<string, CachedHealth> _healthCache = new();

    /// <summary>
    /// Creates a new <see cref="TtsDegradationMonitor"/>.
    /// </summary>
    /// <param name="registry">The provider registry for discovering registered TTS providers.</param>
    /// <param name="logger">Logger for diagnostics.</param>
    public TtsDegradationMonitor(
        IProviderRegistry registry,
        ILogger<TtsDegradationMonitor> logger)
    {
        _registry = registry;
        _logger = logger;
    }

    /// <summary>
    /// Returns the ordered list of TTS providers in degradation-chain order,
    /// including only those currently registered.
    /// </summary>
    public async Task<List<ITtsProvider>> GetDegradationChainAsync(CancellationToken ct)
    {
        var providers = await _registry.GetTtsProvidersAsync(ct);
        var ordered = new List<ITtsProvider>();

        // Add providers in degradation-chain order.
        foreach (var providerId in DegradationChain)
        {
            var provider = providers.FirstOrDefault(p => GetProviderId(p, ct) == providerId);
            if (provider != null)
            {
                ordered.Add(provider);
            }
        }

        // Add any remaining providers not in the predefined chain.
        foreach (var provider in providers)
        {
            var pid = GetProviderId(provider, ct);
            if (!DegradationChain.Contains(pid))
            {
                ordered.Add(provider);
            }
        }

        return ordered;
    }

    /// <summary>
    /// Returns the best available (healthy) TTS provider according to the
    /// degradation chain. Falls back to the first registered provider if none
    /// report as healthy (better to try than to fail).
    /// </summary>
    public async Task<ITtsProvider?> GetBestAvailableAsync(CancellationToken ct)
    {
        var chain = await GetDegradationChainAsync(ct);
        if (chain.Count == 0)
        {
            _logger.LogWarning("TtsDegradationMonitor: no TTS providers registered.");
            return null;
        }

        foreach (var provider in chain)
        {
            var health = await GetCachedHealthAsync(provider, ct);
            if (health.IsHealthy)
            {
                _logger.LogInformation(
                    "TtsDegradationMonitor: selected healthy provider {ProviderId} (latency={LatencyMs}ms).",
                    health.ProviderId, health.LatencyMs);
                return provider;
            }

            _logger.LogWarning(
                "TtsDegradationMonitor: provider {ProviderId} is unhealthy: {StatusMessage}. Falling back.",
                health.ProviderId, health.StatusMessage);
        }

        // No provider reported healthy — return the first in the chain as a last resort.
        _logger.LogWarning(
            "TtsDegradationMonitor: all TTS providers are unhealthy. Using first provider as last resort.");
        return chain[0];
    }

    /// <summary>
    /// Returns the health status of all TTS providers in degradation-chain order.
    /// Useful for dashboard / monitoring endpoints.
    /// </summary>
    public async Task<List<ProviderHealth>> GetHealthStatusAsync(CancellationToken ct)
    {
        var chain = await GetDegradationChainAsync(ct);
        var results = new List<ProviderHealth>();

        foreach (var provider in chain)
        {
            var health = await GetCachedHealthAsync(provider, ct);
            results.Add(health);
        }

        return results;
    }

    /// <summary>
    /// Forces a refresh of all cached health results on the next query.
    /// </summary>
    public void InvalidateCache()
    {
        _healthCache.Clear();
        _logger.LogDebug("TtsDegradationMonitor: health cache invalidated.");
    }

    // ── Private helpers ──

    /// <summary>
    /// Returns a cached health result if fresh, otherwise performs a live health check
    /// and caches the result.
    /// </summary>
    private async Task<ProviderHealth> GetCachedHealthAsync(ITtsProvider provider, CancellationToken ct)
    {
        var providerId = GetProviderId(provider, ct);

        if (_healthCache.TryGetValue(providerId, out var cached) &&
            cached.CheckedAt + HealthCacheTtl > DateTime.UtcNow)
        {
            return cached.Health;
        }

        var health = await provider.HealthCheckAsync(ct);

        _healthCache[providerId] = new CachedHealth
        {
            Health = health,
            CheckedAt = DateTime.UtcNow
        };

        return health;
    }

    /// <summary>
    /// Gets the provider ID from a TTS provider's descriptor.
    /// Uses a synchronous wait since GetDescriptorAsync is typically trivial.
    /// </summary>
    private static string GetProviderId(ITtsProvider provider, CancellationToken ct)
    {
        // GetDescriptorAsync is typically a trivial Task.FromResult, so
        // blocking briefly is acceptable in this singleton context.
        return provider.GetDescriptorAsync(ct).GetAwaiter().GetResult().ProviderId;
    }

    private sealed record CachedHealth
    {
        public required ProviderHealth Health { get; init; }
        public required DateTime CheckedAt { get; init; }
    }
}
