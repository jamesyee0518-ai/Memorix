using System.Collections.Concurrent;
using KnowledgeEngine.Application.DTOs;
using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Application.Settings;
using KnowledgeEngine.Domain.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KnowledgeEngine.Infrastructure.Audio;

/// <summary>
/// Multi-cloud failover service with in-memory circuit breaker.
/// Tries providers in a computed failover chain, respecting privacy constraints:
/// STRICT_LOCAL data never routes to cloud providers (MEMORIX_CLOUD or THIRD_PARTY_CLOUD),
/// even during failover.
/// </summary>
public class MultiCloudFailover
{
    private readonly IProviderRegistry _registry;
    private readonly AudioSettings _settings;
    private readonly ILogger<MultiCloudFailover> _logger;

    /// <summary>
    /// In-memory circuit breaker state keyed by provider ID.
    /// Trips after <see cref="AudioSettings.CircuitBreakerThreshold"/> consecutive failures,
    /// resets after <see cref="AudioSettings.CircuitBreakerResetSec"/> seconds.
    /// </summary>
    private readonly ConcurrentDictionary<string, CircuitBreakerState> _breakerStates = new();

    /// <summary>
    /// Creates a new <see cref="MultiCloudFailover"/>.
    /// </summary>
    /// <param name="registry">Provider registry for looking up ASR providers.</param>
    /// <param name="options">Audio settings (circuit breaker thresholds).</param>
    /// <param name="logger">Logger for diagnostics.</param>
    public MultiCloudFailover(
        IProviderRegistry registry,
        IOptions<AudioSettings> options,
        ILogger<MultiCloudFailover> logger)
    {
        _registry = registry;
        _settings = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Resolves an ASR provider by trying the failover chain in order, skipping
    /// already-tried providers and providers whose circuit breaker is tripped.
    /// Privacy constraints are always respected: STRICT_LOCAL data never routes
    /// to cloud providers.
    /// </summary>
    /// <param name="context">The ASR routing context (contains privacy classification).</param>
    /// <param name="triedProviderIds">Provider IDs that have already been tried and failed.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The next available provider, or null if none remain in the chain.</returns>
    public async Task<IAsrProvider?> ResolveWithFailoverAsync(
        AsrRoutingContext context,
        List<string> triedProviderIds,
        CancellationToken ct)
    {
        var primaryProviderId = context.PreferredProviderId ?? string.Empty;
        var chain = await GetFailoverChainAsync(primaryProviderId, ct);

        var isStrictLocal = context.DataClassification == DataClassification.STRICT_LOCAL;

        _logger.LogInformation(
            "Resolving with failover: primary={Primary}, tried=[{Tried}], strictLocal={StrictLocal}, chain=[{Chain}]",
            primaryProviderId,
            string.Join(", ", triedProviderIds),
            isStrictLocal,
            string.Join(", ", chain));

        foreach (var providerId in chain)
        {
            // Skip providers that have already been tried.
            if (triedProviderIds.Contains(providerId, StringComparer.OrdinalIgnoreCase))
            {
                _logger.LogDebug("Skipping provider {ProviderId}: already tried.", providerId);
                continue;
            }

            // Skip providers whose circuit breaker is tripped.
            if (IsCircuitOpen(providerId))
            {
                _logger.LogWarning(
                    "Skipping provider {ProviderId}: circuit breaker is open.", providerId);
                continue;
            }

            var provider = await _registry.GetAsrProviderByIdAsync(providerId, ct);
            if (provider == null)
            {
                _logger.LogDebug("Skipping provider {ProviderId}: not registered.", providerId);
                continue;
            }

            // Privacy constraint: STRICT_LOCAL data never routes to cloud providers.
            if (isStrictLocal)
            {
                var descriptor = await provider.GetDescriptorAsync(ct);
                if (descriptor.SendsAudioOffDevice)
                {
                    _logger.LogWarning(
                        "Skipping provider {ProviderId}: sends audio off-device, " +
                        "incompatible with STRICT_LOCAL classification.", providerId);
                    continue;
                }

                if (descriptor.ExecutionModes.Contains(ExecutionMode.MEMORIX_CLOUD) ||
                    descriptor.ExecutionModes.Contains(ExecutionMode.THIRD_PARTY_CLOUD))
                {
                    _logger.LogWarning(
                        "Skipping provider {ProviderId}: cloud execution mode, " +
                        "incompatible with STRICT_LOCAL classification.", providerId);
                    continue;
                }
            }

            _logger.LogInformation(
                "Failover selected provider {ProviderId} for capability delegation.", providerId);
            return provider;
        }

        _logger.LogWarning(
            "Failover exhausted all providers in the chain. No suitable provider found.");
        return null;
    }

    /// <summary>
    /// Returns an ordered failover chain for the given primary provider ID.
    /// The chain is: primary → local providers → platform cloud → third-party cloud.
    /// If the primary is empty, returns all providers in priority order.
    /// </summary>
    public async Task<List<string>> GetFailoverChainAsync(
        string primaryProviderId,
        CancellationToken ct)
    {
        var allProviders = await _registry.GetAsrProvidersAsync(ct);
        var chain = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1. Primary provider first (if specified).
        if (!string.IsNullOrWhiteSpace(primaryProviderId))
        {
            chain.Add(primaryProviderId);
            seen.Add(primaryProviderId);
        }

        // Gather all descriptors for classification.
        var descriptors = new List<(string ProviderId, AsrProviderDescriptor Descriptor)>();
        foreach (var p in allProviders)
        {
            var desc = await p.GetDescriptorAsync(ct);
            descriptors.Add((desc.ProviderId, desc));
        }

        // 2. Local device providers (highest priority in failover).
        foreach (var (id, desc) in descriptors)
        {
            if (seen.Contains(id))
            {
                continue;
            }

            if (desc.ExecutionModes.Contains(ExecutionMode.LOCAL_DEVICE))
            {
                chain.Add(id);
                seen.Add(id);
            }
        }

        // 3. LAN node providers.
        foreach (var (id, desc) in descriptors)
        {
            if (seen.Contains(id))
            {
                continue;
            }

            if (desc.ExecutionModes.Contains(ExecutionMode.LOCAL_LAN_NODE))
            {
                chain.Add(id);
                seen.Add(id);
            }
        }

        // 4. Platform-managed cloud providers.
        foreach (var (id, desc) in descriptors)
        {
            if (seen.Contains(id))
            {
                continue;
            }

            if (desc.ExecutionModes.Contains(ExecutionMode.MEMORIX_CLOUD))
            {
                chain.Add(id);
                seen.Add(id);
            }
        }

        // 5. Third-party cloud providers (lowest priority).
        foreach (var (id, desc) in descriptors)
        {
            if (seen.Contains(id))
            {
                continue;
            }

            if (desc.ExecutionModes.Contains(ExecutionMode.THIRD_PARTY_CLOUD))
            {
                chain.Add(id);
                seen.Add(id);
            }
        }

        return chain;
    }

    /// <summary>
    /// Records a failure for the given provider, potentially tripping the circuit breaker.
    /// </summary>
    public void RecordFailure(string providerId)
    {
        var state = _breakerStates.GetOrAdd(providerId, _ => new CircuitBreakerState());
        lock (state)
        {
            state.ConsecutiveFailures++;
            state.LastFailureAt = DateTime.UtcNow;

            if (state.ConsecutiveFailures >= _settings.CircuitBreakerThreshold)
            {
                state.TrippedAt = DateTime.UtcNow;
                _logger.LogWarning(
                    "Circuit breaker tripped for provider {ProviderId} after {Failures} consecutive failures.",
                    providerId, state.ConsecutiveFailures);
            }
        }
    }

    /// <summary>
    /// Records a success for the given provider, resetting the circuit breaker.
    /// </summary>
    public void RecordSuccess(string providerId)
    {
        if (_breakerStates.TryRemove(providerId, out var state))
        {
            _logger.LogInformation(
                "Circuit breaker reset for provider {ProviderId} (was {Failures} failures).",
                providerId, state.ConsecutiveFailures);
        }
    }

    /// <summary>
    /// Checks whether the circuit breaker is currently open (tripped) for the given provider.
    /// Automatically resets after the configured reset period.
    /// </summary>
    public bool IsCircuitOpen(string providerId)
    {
        if (!_breakerStates.TryGetValue(providerId, out var state))
        {
            return false;
        }

        lock (state)
        {
            if (state.ConsecutiveFailures < _settings.CircuitBreakerThreshold)
            {
                return false;
            }

            // Check if the reset period has elapsed.
            var resetAt = state.TrippedAt ?? state.LastFailureAt ?? DateTime.UtcNow;
            if (DateTime.UtcNow - resetAt >= TimeSpan.FromSeconds(_settings.CircuitBreakerResetSec))
            {
                // Half-open: reset and allow a retry.
                state.ConsecutiveFailures = 0;
                state.TrippedAt = null;
                _logger.LogInformation(
                    "Circuit breaker auto-reset for provider {ProviderId} after {ResetSec}s cooldown.",
                    providerId, _settings.CircuitBreakerResetSec);
                return false;
            }

            return true;
        }
    }

    // ── Circuit breaker state ──

    private sealed class CircuitBreakerState
    {
        public int ConsecutiveFailures { get; set; }
        public DateTime? LastFailureAt { get; set; }
        public DateTime? TrippedAt { get; set; }
    }
}
