using System.Collections.Concurrent;
using KnowledgeEngine.Application.DTOs;
using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Domain.Enums;

namespace KnowledgeEngine.Infrastructure.Audio;

/// <summary>
/// Thread-safe provider registry for ASR and TTS capability providers.
/// Providers self-register during DI initialization via <see cref="RegisterAsync"/>
/// and are discovered at runtime through filter-based queries.
/// Descriptor lists are cached and invalidated on each registration.
/// </summary>
public class ProviderRegistry : IProviderRegistry
{
    private readonly ConcurrentDictionary<string, IAsrProvider> _asrProviders = new();
    private readonly ConcurrentDictionary<string, ITtsProvider> _ttsProviders = new();

    // Descriptor caches — invalidated (set to null) whenever a provider is registered.
    private List<AsrProviderDescriptor>? _asrDescriptorCache;
    private List<TtsProviderDescriptor>? _ttsDescriptorCache;
    private readonly SemaphoreSlim _asrCacheLock = new(1, 1);
    private readonly SemaphoreSlim _ttsCacheLock = new(1, 1);

    /// <summary>
    /// Creates a new <see cref="ProviderRegistry"/>. Takes no parameters —
    /// providers self-register via <see cref="RegisterAsync"/>.
    /// </summary>
    public ProviderRegistry()
    {
    }

    // ── Registration ──

    /// <inheritdoc/>
    public async Task RegisterAsync(IAsrProvider provider, CancellationToken ct)
    {
        var descriptor = await provider.GetDescriptorAsync(ct);
        _asrProviders[descriptor.ProviderId] = provider;

        // Invalidate the descriptor cache.
        await _asrCacheLock.WaitAsync(ct);
        try
        {
            _asrDescriptorCache = null;
        }
        finally
        {
            _asrCacheLock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task RegisterAsync(ITtsProvider provider, CancellationToken ct)
    {
        var descriptor = await provider.GetDescriptorAsync(ct);
        _ttsProviders[descriptor.ProviderId] = provider;

        // Invalidate the descriptor cache.
        await _ttsCacheLock.WaitAsync(ct);
        try
        {
            _ttsDescriptorCache = null;
        }
        finally
        {
            _ttsCacheLock.Release();
        }
    }

    // ── Bulk retrieval ──

    /// <inheritdoc/>
    public Task<List<IAsrProvider>> GetAsrProvidersAsync(CancellationToken ct)
    {
        return Task.FromResult(_asrProviders.Values.ToList());
    }

    /// <inheritdoc/>
    public Task<List<ITtsProvider>> GetTtsProvidersAsync(CancellationToken ct)
    {
        return Task.FromResult(_ttsProviders.Values.ToList());
    }

    // ── Filtered discovery ──

    /// <inheritdoc/>
    public async Task<List<IAsrProvider>> FindAsrProvidersAsync(ProviderFilter filter, CancellationToken ct)
    {
        var descriptors = await GetAsrDescriptorsAsync(ct);
        var result = new List<IAsrProvider>();

        foreach (var desc in descriptors)
        {
            if (!MatchesAsrFilter(desc, filter))
                continue;

            if (_asrProviders.TryGetValue(desc.ProviderId, out var provider))
            {
                result.Add(provider);
            }
        }

        return result;
    }

    /// <inheritdoc/>
    public async Task<List<ITtsProvider>> FindTtsProvidersAsync(ProviderFilter filter, CancellationToken ct)
    {
        var descriptors = await GetTtsDescriptorsAsync(ct);
        var result = new List<ITtsProvider>();

        foreach (var desc in descriptors)
        {
            if (!MatchesTtsFilter(desc, filter))
                continue;

            if (_ttsProviders.TryGetValue(desc.ProviderId, out var provider))
            {
                result.Add(provider);
            }
        }

        return result;
    }

    // ── Direct lookup ──

    /// <inheritdoc/>
    public Task<IAsrProvider?> GetAsrProviderByIdAsync(string providerId, CancellationToken ct)
    {
        _asrProviders.TryGetValue(providerId, out var provider);
        return Task.FromResult(provider);
    }

    /// <inheritdoc/>
    public Task<ITtsProvider?> GetTtsProviderByIdAsync(string providerId, CancellationToken ct)
    {
        _ttsProviders.TryGetValue(providerId, out var provider);
        return Task.FromResult(provider);
    }

    // ── Descriptor cache ──

    /// <inheritdoc/>
    public async Task<List<AsrProviderDescriptor>> GetAsrDescriptorsAsync(CancellationToken ct)
    {
        if (_asrDescriptorCache != null)
            return _asrDescriptorCache;

        await _asrCacheLock.WaitAsync(ct);
        try
        {
            // Double-check after acquiring the lock.
            if (_asrDescriptorCache != null)
                return _asrDescriptorCache;

            var tasks = _asrProviders.Values.Select(p => p.GetDescriptorAsync(ct));
            var descriptors = await Task.WhenAll(tasks);
            _asrDescriptorCache = descriptors.ToList();
            return _asrDescriptorCache;
        }
        finally
        {
            _asrCacheLock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task<List<TtsProviderDescriptor>> GetTtsDescriptorsAsync(CancellationToken ct)
    {
        if (_ttsDescriptorCache != null)
            return _ttsDescriptorCache;

        await _ttsCacheLock.WaitAsync(ct);
        try
        {
            // Double-check after acquiring the lock.
            if (_ttsDescriptorCache != null)
                return _ttsDescriptorCache;

            var tasks = _ttsProviders.Values.Select(p => p.GetDescriptorAsync(ct));
            var descriptors = await Task.WhenAll(tasks);
            _ttsDescriptorCache = descriptors.ToList();
            return _ttsDescriptorCache;
        }
        finally
        {
            _ttsCacheLock.Release();
        }
    }

    // ── Private: ASR filter matching ──

    /// <summary>
    /// Determines whether an ASR provider descriptor matches the given filter criteria.
    /// </summary>
    private static bool MatchesAsrFilter(AsrProviderDescriptor desc, ProviderFilter filter)
    {
        // ProviderId — exact match (case-insensitive).
        if (!string.IsNullOrWhiteSpace(filter.ProviderId) &&
            !string.Equals(desc.ProviderId, filter.ProviderId, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // ExecutionModes — provider must support at least one of the requested modes.
        if (filter.ExecutionModes is { Count: > 0 })
        {
            if (!desc.ExecutionModes.Any(filter.ExecutionModes.Contains))
                return false;
        }

        // CredentialModes — provider must support at least one of the requested modes.
        if (filter.CredentialModes is { Count: > 0 })
        {
            if (!desc.CredentialModes.Any(filter.CredentialModes.Contains))
                return false;
        }

        // Language — provider must support the requested language.
        // An empty SupportedLanguages list means the provider supports all languages.
        if (!string.IsNullOrWhiteSpace(filter.Language))
        {
            if (desc.SupportedLanguages.Count > 0 &&
                !desc.SupportedLanguages.Contains(filter.Language, StringComparer.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        // Capability — map capability string to descriptor flags.
        if (!string.IsNullOrWhiteSpace(filter.Capability) &&
            !MatchesAsrCapability(desc, filter.Capability))
        {
            return false;
        }

        // Boolean capability filters — exact match when specified.
        if (filter.SupportsStreaming.HasValue && desc.SupportsStreaming != filter.SupportsStreaming.Value)
            return false;
        if (filter.SupportsVad.HasValue && desc.SupportsVad != filter.SupportsVad.Value)
            return false;
        if (filter.SupportsPunctuation.HasValue && desc.SupportsPunctuation != filter.SupportsPunctuation.Value)
            return false;
        if (filter.SupportsDiarization.HasValue && desc.SupportsDiarization != filter.SupportsDiarization.Value)
            return false;
        if (filter.SupportsHotwords.HasValue && desc.SupportsHotwords != filter.SupportsHotwords.Value)
            return false;
        if (filter.SendsAudioOffDevice.HasValue && desc.SendsAudioOffDevice != filter.SendsAudioOffDevice.Value)
            return false;

        return true;
    }

    /// <summary>
    /// Maps a capability string to the corresponding ASR descriptor flag.
    /// </summary>
    private static bool MatchesAsrCapability(AsrProviderDescriptor desc, string capability)
    {
        return capability switch
        {
            AudioCapabilities.Vad => desc.SupportsVad,
            AudioCapabilities.Transcription => desc.SupportsBatch,
            AudioCapabilities.Punctuation => desc.SupportsPunctuation,
            AudioCapabilities.Diarization => desc.SupportsDiarization,
            _ => true // Unknown capability — don't filter.
        };
    }

    // ── Private: TTS filter matching ──

    /// <summary>
    /// Determines whether a TTS provider descriptor matches the given filter criteria.
    /// </summary>
    private static bool MatchesTtsFilter(TtsProviderDescriptor desc, ProviderFilter filter)
    {
        // ProviderId — exact match (case-insensitive).
        if (!string.IsNullOrWhiteSpace(filter.ProviderId) &&
            !string.Equals(desc.ProviderId, filter.ProviderId, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // ExecutionModes — provider must support at least one of the requested modes.
        if (filter.ExecutionModes is { Count: > 0 })
        {
            if (!desc.ExecutionModes.Any(filter.ExecutionModes.Contains))
                return false;
        }

        // CredentialModes — provider must support at least one of the requested modes.
        if (filter.CredentialModes is { Count: > 0 })
        {
            if (!desc.CredentialModes.Any(filter.CredentialModes.Contains))
                return false;
        }

        // Language — provider must support the requested language.
        if (!string.IsNullOrWhiteSpace(filter.Language))
        {
            if (desc.SupportedLanguages.Count > 0 &&
                !desc.SupportedLanguages.Contains(filter.Language, StringComparer.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        // Capability — map capability string to descriptor flags.
        if (!string.IsNullOrWhiteSpace(filter.Capability) &&
            !MatchesTtsCapability(desc, filter.Capability))
        {
            return false;
        }

        // Boolean capability filters — exact match when specified.
        if (filter.SupportsStreaming.HasValue && desc.SupportsStreaming != filter.SupportsStreaming.Value)
            return false;
        if (filter.SendsAudioOffDevice.HasValue && desc.SendsAudioOffDevice != filter.SendsAudioOffDevice.Value)
            return false;

        return true;
    }

    /// <summary>
    /// Maps a capability string to the corresponding TTS descriptor flag.
    /// </summary>
    private static bool MatchesTtsCapability(TtsProviderDescriptor desc, string capability)
    {
        return capability switch
        {
            AudioCapabilities.Synthesis => true, // All TTS providers support synthesis.
            _ => true // Unknown capability — don't filter.
        };
    }
}
