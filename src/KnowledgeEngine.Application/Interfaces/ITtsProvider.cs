using KnowledgeEngine.Application.DTOs;

namespace KnowledgeEngine.Application.Interfaces;

/// <summary>
/// TTS (Text-to-Speech) capability contract.
/// Business code depends on this interface, never on specific model implementations.
/// </summary>
public interface ITtsProvider
{
    /// <summary>
    /// Returns the provider's capability descriptor declaring execution modes,
    /// credential modes, supported languages, voice cloning, and output formats.
    /// </summary>
    Task<TtsProviderDescriptor> GetDescriptorAsync(CancellationToken ct);

    /// <summary>
    /// Validates whether the provider can handle the given request before execution.
    /// </summary>
    Task<ValidationResult> ValidateRequestAsync(TtsRequest request, CancellationToken ct);

    /// <summary>
    /// Synthesizes text to a complete audio file.
    /// </summary>
    Task<TtsResult> SynthesizeAsync(TtsRequest request, CancellationToken ct);

    /// <summary>
    /// Optional streaming synthesis for low-latency playback.
    /// Returns null if the provider does not support streaming.
    /// </summary>
    IAsyncEnumerable<AudioChunk>? SynthesizeStream(TtsStreamRequest request, CancellationToken ct);

    /// <summary>
    /// Lists available voice profiles for this provider.
    /// </summary>
    Task<List<VoiceProfile>> ListVoicesAsync(CancellationToken ct);

    /// <summary>
    /// Optional cost estimate before execution.
    /// </summary>
    Task<CostEstimate>? EstimateCostAsync(TtsRequest request, CancellationToken ct);

    /// <summary>
    /// Checks provider health and measures latency.
    /// </summary>
    Task<ProviderHealth> HealthCheckAsync(CancellationToken ct);
}
