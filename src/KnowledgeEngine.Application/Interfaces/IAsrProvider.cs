using KnowledgeEngine.Application.DTOs;

namespace KnowledgeEngine.Application.Interfaces;

/// <summary>
/// ASR (Automatic Speech Recognition) capability contract.
/// Business code depends on this interface, never on specific model implementations.
/// </summary>
public interface IAsrProvider
{
    /// <summary>
    /// Returns the provider's capability descriptor declaring execution modes,
    /// credential modes, supported languages, privacy attributes, and limits.
    /// </summary>
    Task<AsrProviderDescriptor> GetDescriptorAsync(CancellationToken ct);

    /// <summary>
    /// Validates whether the provider can handle the given request before execution.
    /// </summary>
    Task<ValidationResult> ValidateRequestAsync(AsrTranscriptionRequest request, CancellationToken ct);

    /// <summary>
    /// Performs batch transcription on a complete audio file.
    /// </summary>
    Task<AsrTranscriptionResult> TranscribeAsync(AsrTranscriptionRequest request, CancellationToken ct);

    /// <summary>
    /// Optional streaming transcription for real-time use cases.
    /// Returns null if the provider does not support streaming.
    /// </summary>
    IAsyncEnumerable<AsrPartialResult>? TranscribeStream(AsrStreamingRequest request, CancellationToken ct);

    /// <summary>
    /// Optional cost estimate before execution.
    /// </summary>
    Task<CostEstimate>? EstimateCostAsync(AsrTranscriptionRequest request, CancellationToken ct);

    /// <summary>
    /// Cancels an in-progress transcription task on the provider side.
    /// </summary>
    Task CancelAsync(string providerTaskId, CancellationToken ct);

    /// <summary>
    /// Checks provider health and measures latency.
    /// </summary>
    Task<ProviderHealth> HealthCheckAsync(CancellationToken ct);
}
