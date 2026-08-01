using KnowledgeEngine.Application.DTOs;
using KnowledgeEngine.Domain.Enums;

namespace KnowledgeEngine.Application.Interfaces;

/// <summary>
/// Provider registration and discovery mechanism.
/// Providers self-register during DI initialization and are discovered at runtime
/// via filter-based queries.
/// </summary>
public interface IProviderRegistry
{
    // ── Registration ──

    Task RegisterAsync(IAsrProvider provider, CancellationToken ct);
    Task RegisterAsync(ITtsProvider provider, CancellationToken ct);

    // ── Bulk retrieval ──

    Task<List<IAsrProvider>> GetAsrProvidersAsync(CancellationToken ct);
    Task<List<ITtsProvider>> GetTtsProvidersAsync(CancellationToken ct);

    // ── Filtered discovery ──

    Task<List<IAsrProvider>> FindAsrProvidersAsync(ProviderFilter filter, CancellationToken ct);
    Task<List<ITtsProvider>> FindTtsProvidersAsync(ProviderFilter filter, CancellationToken ct);

    // ── Direct lookup ──

    Task<IAsrProvider?> GetAsrProviderByIdAsync(string providerId, CancellationToken ct);
    Task<ITtsProvider?> GetTtsProviderByIdAsync(string providerId, CancellationToken ct);

    // ── Descriptor cache ──

    Task<List<AsrProviderDescriptor>> GetAsrDescriptorsAsync(CancellationToken ct);
    Task<List<TtsProviderDescriptor>> GetTtsDescriptorsAsync(CancellationToken ct);
}
