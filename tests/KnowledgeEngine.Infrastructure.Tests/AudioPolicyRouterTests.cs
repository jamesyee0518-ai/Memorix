using KnowledgeEngine.Application.DTOs;
using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Domain.Entities;
using KnowledgeEngine.Domain.Enums;
using KnowledgeEngine.Infrastructure.Audio;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace KnowledgeEngine.Infrastructure.Tests;

public sealed class AudioPolicyRouterTests
{
    // ── Descriptor builder with sensible defaults that pass all steps ──

    private static AsrProviderDescriptor Descriptor(
        string providerId,
        string modelId = "test-model",
        bool sendsAudioOffDevice = false,
        List<ExecutionMode>? executionModes = null,
        List<CredentialMode>? credentialModes = null,
        List<string>? supportedLanguages = null,
        long? maxFileBytes = null,
        long? maxAudioDurationMs = null,
        List<string>? acceptedMimeTypes = null) => new()
    {
        ProviderId = providerId,
        ModelId = modelId,
        SendsAudioOffDevice = sendsAudioOffDevice,
        ExecutionModes = executionModes ?? new() { ExecutionMode.LOCAL_DEVICE },
        CredentialModes = credentialModes ?? new() { CredentialMode.NO_CREDENTIAL },
        SupportedLanguages = supportedLanguages ?? new(),
        MaxFileBytes = maxFileBytes,
        MaxAudioDurationMs = maxAudioDurationMs,
        AcceptedMimeTypes = acceptedMimeTypes ?? new(),
        SupportsBatch = true,
        SupportsVad = true,
        SupportsPunctuation = true,
        SupportsDiarization = true,
        SupportsHotwords = true,
        SupportsWordTimestamp = true,
    };

    private static AudioPolicyRouter CreateRouter(
        List<IAsrProvider> providers,
        ProviderCredential? credential = null)
    {
        var registry = new MockProviderRegistry(providers);
        var credManager = new MockCredentialManager(credential);
        return new AudioPolicyRouter(registry, credManager, NullLogger<AudioPolicyRouter>.Instance);
    }

    // ── Tests ──

    [Fact]
    public async Task ResolveAsr_NoProvidersRegistered_Throws()
    {
        var router = CreateRouter(new List<IAsrProvider>());
        var context = new AsrRoutingContext
        {
            FallbackPolicy = FallbackPolicies.Stop,
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            router.ResolveAsrProviderAsync(context, CancellationToken.None));
    }

    [Fact]
    public async Task ResolveAsr_StrictLocalEliminatesOffDeviceProviders()
    {
        // Only an off-device provider — STRICT_LOCAL privacy filter eliminates it.
        var provider = new MockAsrProvider(
            Descriptor("cloud-asr", sendsAudioOffDevice: true));
        var router = CreateRouter(new List<IAsrProvider> { provider });

        var context = new AsrRoutingContext
        {
            DataClassification = DataClassification.STRICT_LOCAL,
            FallbackPolicy = FallbackPolicies.Stop,
        };

        var decision = await router.ExplainAsrRoutingAsync(context, CancellationToken.None);

        Assert.Equal(string.Empty, decision.SelectedProviderId);
        Assert.Contains(decision.EliminatedProviders,
            e => e.Contains("cloud-asr") && e.Contains("STRICT_LOCAL"));
    }

    [Fact]
    public async Task ResolveAsr_StrictLocalKeepsOnDeviceProviders()
    {
        var provider = new MockAsrProvider(
            Descriptor("local-asr", sendsAudioOffDevice: false));
        var router = CreateRouter(new List<IAsrProvider> { provider });

        var context = new AsrRoutingContext
        {
            DataClassification = DataClassification.STRICT_LOCAL,
            FallbackPolicy = FallbackPolicies.Stop,
        };

        var resolved = await router.ResolveAsrProviderAsync(context, CancellationToken.None);
        var desc = await resolved.GetDescriptorAsync(CancellationToken.None);

        Assert.Equal("local-asr", desc.ProviderId);
    }

    [Fact]
    public async Task ResolveAsr_ExecutionModePreferenceFilters()
    {
        // Only a cloud provider, but user prefers LOCAL_DEVICE.
        var provider = new MockAsrProvider(
            Descriptor("cloud-asr",
                sendsAudioOffDevice: false,
                executionModes: new() { ExecutionMode.MEMORIX_CLOUD }));
        var router = CreateRouter(new List<IAsrProvider> { provider });

        var context = new AsrRoutingContext
        {
            DataClassification = DataClassification.PUBLIC,
            PreferredExecutionMode = ExecutionMode.LOCAL_DEVICE,
            FallbackPolicy = FallbackPolicies.Stop,
        };

        var decision = await router.ExplainAsrRoutingAsync(context, CancellationToken.None);

        Assert.Equal(string.Empty, decision.SelectedProviderId);
        Assert.Contains(decision.EliminatedProviders,
            e => e.Contains("cloud-asr") && e.Contains("execution mode"));
    }

    [Fact]
    public async Task ResolveAsr_FileSizeExceedsMax_EliminatesProvider()
    {
        var provider = new MockAsrProvider(
            Descriptor("local-asr", maxFileBytes: 1000));
        var router = CreateRouter(new List<IAsrProvider> { provider });

        var context = new AsrRoutingContext
        {
            DataClassification = DataClassification.PUBLIC,
            FileSizeBytes = 5000,
            FallbackPolicy = FallbackPolicies.Stop,
        };

        var decision = await router.ExplainAsrRoutingAsync(context, CancellationToken.None);

        Assert.Equal(string.Empty, decision.SelectedProviderId);
        Assert.Contains(decision.EliminatedProviders,
            e => e.Contains("local-asr") && e.Contains("file size"));
    }

    [Fact]
    public async Task ResolveAsr_LanguageNotSupported_EliminatesProvider()
    {
        var provider = new MockAsrProvider(
            Descriptor("local-asr", supportedLanguages: new() { "en" }));
        var router = CreateRouter(new List<IAsrProvider> { provider });

        var context = new AsrRoutingContext
        {
            DataClassification = DataClassification.PUBLIC,
            Language = "zh",
            FallbackPolicy = FallbackPolicies.Stop,
        };

        var decision = await router.ExplainAsrRoutingAsync(context, CancellationToken.None);

        Assert.Equal(string.Empty, decision.SelectedProviderId);
        Assert.Contains(decision.EliminatedProviders,
            e => e.Contains("local-asr") && e.Contains("language"));
    }

    [Fact]
    public async Task ResolveAsr_UserPreferenceSelected_WhenAvailable()
    {
        var providerA = new MockAsrProvider(
            Descriptor("provider-a"),
            health: new ProviderHealth
            {
                ProviderId = "provider-a",
                IsHealthy = true,
                LatencyMs = 50,
            });
        var providerB = new MockAsrProvider(
            Descriptor("provider-b"),
            health: new ProviderHealth
            {
                ProviderId = "provider-b",
                IsHealthy = true,
                LatencyMs = 100,
            });
        var router = CreateRouter(new List<IAsrProvider> { providerA, providerB });

        var context = new AsrRoutingContext
        {
            DataClassification = DataClassification.PUBLIC,
            PreferredProviderId = "provider-b",
            FallbackPolicy = FallbackPolicies.Stop,
        };

        var resolved = await router.ResolveAsrProviderAsync(context, CancellationToken.None);
        var desc = await resolved.GetDescriptorAsync(CancellationToken.None);

        Assert.Equal("provider-b", desc.ProviderId);
    }

    [Fact]
    public async Task ResolveAsr_UserPreferenceNotSelected_WhenEliminatedByPrivacy()
    {
        var onDevice = new MockAsrProvider(
            Descriptor("on-device", sendsAudioOffDevice: false));
        var offDevice = new MockAsrProvider(
            Descriptor("off-device", sendsAudioOffDevice: true));
        var router = CreateRouter(new List<IAsrProvider> { onDevice, offDevice });

        var context = new AsrRoutingContext
        {
            DataClassification = DataClassification.STRICT_LOCAL,
            PreferredProviderId = "off-device",
            FallbackPolicy = FallbackPolicies.Stop,
        };

        var decision = await router.ExplainAsrRoutingAsync(context, CancellationToken.None);

        // The on-device provider should be selected; the off-device preferred
        // provider was eliminated by the privacy filter and cannot override.
        Assert.Equal("on-device", decision.SelectedProviderId);
        Assert.Contains(decision.EliminatedProviders, e => e.Contains("off-device"));
    }

    [Fact]
    public async Task ExplainAsr_StopFallback_ReturnsNull()
    {
        // Provider fails at Step 4 (file size exceeds max); STOP fallback yields nothing.
        var provider = new MockAsrProvider(
            Descriptor("local-asr", maxFileBytes: 1000));
        var router = CreateRouter(new List<IAsrProvider> { provider });

        var context = new AsrRoutingContext
        {
            DataClassification = DataClassification.PUBLIC,
            FileSizeBytes = 5000,
            FallbackPolicy = FallbackPolicies.Stop,
        };

        var decision = await router.ExplainAsrRoutingAsync(context, CancellationToken.None);

        Assert.Equal(string.Empty, decision.SelectedProviderId);
        Assert.NotNull(decision.FallbackReason);
        Assert.Contains("STOP", decision.FallbackReason);
    }

    [Fact]
    public async Task ExplainAsr_LocalFallback_SelectsLocalProvider()
    {
        // Provider supports LOCAL_LAN_NODE but user prefers LOCAL_DEVICE.
        // Eliminated at Step 2, but LOCAL_FALLBACK can pick it up.
        var provider = new MockAsrProvider(
            Descriptor("lan-asr",
                executionModes: new() { ExecutionMode.LOCAL_LAN_NODE }));
        var router = CreateRouter(new List<IAsrProvider> { provider });

        var context = new AsrRoutingContext
        {
            DataClassification = DataClassification.PUBLIC,
            PreferredExecutionMode = ExecutionMode.LOCAL_DEVICE,
            FallbackPolicy = FallbackPolicies.LocalFallback,
        };

        var decision = await router.ExplainAsrRoutingAsync(context, CancellationToken.None);

        Assert.Equal("lan-asr", decision.SelectedProviderId);
        Assert.NotNull(decision.FallbackReason);
        Assert.Contains("LOCAL_FALLBACK", decision.FallbackReason);
    }

    [Fact]
    public async Task ExplainAsr_PlatformFallback_SelectsPlatformProvider()
    {
        // Provider supports MEMORIX_CLOUD but user prefers LOCAL_DEVICE.
        // Eliminated at Step 2, but PLATFORM_FALLBACK can pick it up.
        var provider = new MockAsrProvider(
            Descriptor("cloud-asr",
                executionModes: new() { ExecutionMode.MEMORIX_CLOUD }));
        var router = CreateRouter(new List<IAsrProvider> { provider });

        var context = new AsrRoutingContext
        {
            DataClassification = DataClassification.PUBLIC,
            PreferredExecutionMode = ExecutionMode.LOCAL_DEVICE,
            FallbackPolicy = FallbackPolicies.PlatformFallback,
        };

        var decision = await router.ExplainAsrRoutingAsync(context, CancellationToken.None);

        Assert.Equal("cloud-asr", decision.SelectedProviderId);
        Assert.NotNull(decision.FallbackReason);
        Assert.Contains("PLATFORM_FALLBACK", decision.FallbackReason);
    }

    // ── Mock Implementations ──

    private sealed class MockAsrProvider : IAsrProvider
    {
        private readonly AsrProviderDescriptor _descriptor;
        private readonly ProviderHealth _health;
        private readonly CostEstimate _costEstimate;

        public MockAsrProvider(
            AsrProviderDescriptor descriptor,
            ProviderHealth? health = null,
            CostEstimate? costEstimate = null)
        {
            _descriptor = descriptor;
            _health = health ?? new ProviderHealth
            {
                ProviderId = descriptor.ProviderId,
                IsHealthy = true,
                LatencyMs = 100,
            };
            _costEstimate = costEstimate ?? new CostEstimate
            {
                ProviderId = descriptor.ProviderId,
                ModelId = descriptor.ModelId,
                EstimatedCost = 0.01m,
            };
        }

        public Task<AsrProviderDescriptor> GetDescriptorAsync(CancellationToken ct) =>
            Task.FromResult(_descriptor);

        public Task<ValidationResult> ValidateRequestAsync(
            AsrTranscriptionRequest request, CancellationToken ct) =>
            Task.FromResult(ValidationResult.Ok());

        public Task<AsrTranscriptionResult> TranscribeAsync(
            AsrTranscriptionRequest request, CancellationToken ct) =>
            throw new NotImplementedException();

        public IAsyncEnumerable<AsrPartialResult>? TranscribeStream(
            AsrStreamingRequest request, CancellationToken ct) =>
            null;

        public Task<CostEstimate>? EstimateCostAsync(
            AsrTranscriptionRequest request, CancellationToken ct) =>
            Task.FromResult(_costEstimate);

        public Task CancelAsync(string providerTaskId, CancellationToken ct) =>
            Task.CompletedTask;

        public Task<ProviderHealth> HealthCheckAsync(CancellationToken ct) =>
            Task.FromResult(_health);
    }

    private sealed class MockProviderRegistry : IProviderRegistry
    {
        private readonly List<IAsrProvider> _asrProviders;
        private readonly List<ITtsProvider> _ttsProviders;

        public MockProviderRegistry(
            List<IAsrProvider> asrProviders,
            List<ITtsProvider>? ttsProviders = null)
        {
            _asrProviders = asrProviders;
            _ttsProviders = ttsProviders ?? new();
        }

        public Task RegisterAsync(IAsrProvider provider, CancellationToken ct)
        {
            _asrProviders.Add(provider);
            return Task.CompletedTask;
        }

        public Task RegisterAsync(ITtsProvider provider, CancellationToken ct)
        {
            _ttsProviders.Add(provider);
            return Task.CompletedTask;
        }

        public Task<List<IAsrProvider>> GetAsrProvidersAsync(CancellationToken ct) =>
            Task.FromResult(_asrProviders);

        public Task<List<ITtsProvider>> GetTtsProvidersAsync(CancellationToken ct) =>
            Task.FromResult(_ttsProviders);

        public Task<List<IAsrProvider>> FindAsrProvidersAsync(
            ProviderFilter filter, CancellationToken ct) =>
            Task.FromResult(_asrProviders);

        public Task<List<ITtsProvider>> FindTtsProvidersAsync(
            ProviderFilter filter, CancellationToken ct) =>
            Task.FromResult(_ttsProviders);

        public async Task<IAsrProvider?> GetAsrProviderByIdAsync(
            string providerId, CancellationToken ct)
        {
            foreach (var p in _asrProviders)
            {
                var desc = await p.GetDescriptorAsync(ct);
                if (desc.ProviderId == providerId) return p;
            }
            return null;
        }

        public Task<ITtsProvider?> GetTtsProviderByIdAsync(
            string providerId, CancellationToken ct) =>
            Task.FromResult(_ttsProviders.FirstOrDefault());

        public async Task<List<AsrProviderDescriptor>> GetAsrDescriptorsAsync(CancellationToken ct)
        {
            var result = new List<AsrProviderDescriptor>();
            foreach (var p in _asrProviders)
                result.Add(await p.GetDescriptorAsync(ct));
            return result;
        }

        public async Task<List<TtsProviderDescriptor>> GetTtsDescriptorsAsync(CancellationToken ct)
        {
            var result = new List<TtsProviderDescriptor>();
            foreach (var p in _ttsProviders)
                result.Add(await p.GetDescriptorAsync(ct));
            return result;
        }
    }

    private sealed class MockCredentialManager : ICredentialManager
    {
        private readonly ProviderCredential? _credential;

        public MockCredentialManager(ProviderCredential? credential = null)
        {
            _credential = credential;
        }

        public Task<ProviderCredential> StoreAsync(
            StoreCredentialRequest request, CancellationToken ct) =>
            throw new NotImplementedException();

        public Task<string?> GetSecretAsync(Guid credentialId, CancellationToken ct) =>
            throw new NotImplementedException();

        public Task<bool> VerifyAsync(Guid credentialId, CancellationToken ct) =>
            throw new NotImplementedException();

        public Task DisableAsync(Guid credentialId, CancellationToken ct) =>
            throw new NotImplementedException();

        public Task RotateAsync(Guid credentialId, CancellationToken ct) =>
            throw new NotImplementedException();

        public Task<List<CredentialDto>> ListByOwnerAsync(
            string ownerType, Guid ownerId, CancellationToken ct) =>
            throw new NotImplementedException();

        public Task<ProviderCredential?> FindActiveAsync(
            string providerId, string ownerType, Guid ownerId, CancellationToken ct) =>
            Task.FromResult(_credential);
    }
}
