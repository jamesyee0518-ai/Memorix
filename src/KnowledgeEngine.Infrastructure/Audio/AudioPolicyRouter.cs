using KnowledgeEngine.Application.DTOs;
using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace KnowledgeEngine.Infrastructure.Audio;

/// <summary>
/// Implements the 8-step audio policy routing strategy for capability-to-provider resolution.
/// Routing order: privacy -> execution mode -> credential -> capability/limits -> language/scenario
///                -> health/cost sort -> user preference -> fallback.
/// Security constraints (steps 1-3) cannot be overridden by user preference.
/// </summary>
public class AudioPolicyRouter : IAudioPolicyRouter
{
    private readonly IProviderRegistry _registry;
    private readonly ICredentialManager _credentialManager;
    private readonly ILogger<AudioPolicyRouter> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="AudioPolicyRouter"/> class.
    /// </summary>
    /// <param name="registry">The provider registry for discovering registered ASR/TTS providers.</param>
    /// <param name="credentialManager">The credential manager for checking BYOK credential availability.</param>
    /// <param name="logger">The logger for diagnostic output.</param>
    public AudioPolicyRouter(
        IProviderRegistry registry,
        ICredentialManager credentialManager,
        ILogger<AudioPolicyRouter> logger)
    {
        _registry = registry;
        _credentialManager = credentialManager;
        _logger = logger;
    }

    // ── IAudioPolicyRouter ──

    /// <inheritdoc/>
    public async Task<IAsrProvider> ResolveAsrProviderAsync(AsrRoutingContext context, CancellationToken ct)
    {
        var (provider, _) = await ResolveAsrInternalAsync(context, ct);
        return provider ?? throw new InvalidOperationException(
            $"No ASR provider could be resolved for the given routing context. " +
            $"Fallback policy: {context.FallbackPolicy}");
    }

    /// <inheritdoc/>
    public async Task<ITtsProvider> ResolveTtsProviderAsync(TtsRoutingContext context, CancellationToken ct)
    {
        var (provider, _) = await ResolveTtsInternalAsync(context, ct);
        return provider ?? throw new InvalidOperationException(
            $"No TTS provider could be resolved for the given routing context. " +
            $"Fallback policy: {context.FallbackPolicy}");
    }

    /// <inheritdoc/>
    public async Task<RoutingDecision> ExplainAsrRoutingAsync(AsrRoutingContext context, CancellationToken ct)
    {
        var (_, decision) = await ResolveAsrInternalAsync(context, ct);
        return decision;
    }

    // ── ASR Routing (8-step strategy) ──

    /// <summary>
    /// Internal ASR routing that returns both the resolved provider and a detailed decision log.
    /// </summary>
    private async Task<(IAsrProvider? Provider, RoutingDecision Decision)> ResolveAsrInternalAsync(
        AsrRoutingContext context, CancellationToken ct)
    {
        var decision = new RoutingDecision();
        var steps = new List<string>();
        var eliminated = new List<string>();

        // ── Load all registered ASR providers and their descriptors ──

        var providers = await _registry.GetAsrProvidersAsync(ct);
        steps.Add($"Step 0 (Load): Retrieved {providers.Count} registered ASR provider(s).");

        if (providers.Count == 0)
        {
            steps.Add("Step 0: No ASR providers registered. Cannot route.");
            decision.Steps = steps;
            decision.EliminatedProviders = eliminated;
            decision.FallbackReason = "No providers registered";
            return (null, decision);
        }

        var candidates = new List<(IAsrProvider Provider, AsrProviderDescriptor Descriptor)>();
        foreach (var p in providers)
        {
            var desc = await p.GetDescriptorAsync(ct);
            candidates.Add((p, desc));
        }

        // ── Step 1: Filter by data privacy level ──
        // STRICT_LOCAL data may only be sent to providers that do not send audio off-device.
        // PRIVATE data prefers on-device but allows off-device providers to pass.

        var privacyPassed = candidates;
        if (context.DataClassification == DataClassification.STRICT_LOCAL)
        {
            privacyPassed = candidates
                .Where(x => !x.Descriptor.SendsAudioOffDevice)
                .ToList();

            foreach (var removed in candidates.Except(privacyPassed))
            {
                eliminated.Add(
                    $"{removed.Descriptor.ProviderId}: Step 1 - sends audio off-device, " +
                    "incompatible with STRICT_LOCAL classification");
            }

            steps.Add(
                $"Step 1 (Privacy): STRICT_LOCAL classification requires on-device-only providers. " +
                $"{privacyPassed.Count}/{candidates.Count} remain.");
        }
        else
        {
            steps.Add(
                $"Step 1 (Privacy): {context.DataClassification} classification allows off-device providers. " +
                $"{privacyPassed.Count}/{candidates.Count} remain.");
        }

        // If privacy filter eliminates everything, no fallback can help (security constraint).
        if (privacyPassed.Count == 0)
        {
            steps.Add("Step 1: All providers eliminated by privacy filter. Security constraint cannot be relaxed.");
            decision.Steps = steps;
            decision.EliminatedProviders = eliminated;
            decision.FallbackReason = "Privacy constraint eliminated all providers";
            return (null, decision);
        }

        // ── Step 2: Filter by execution mode preference ──

        var execPassed = privacyPassed;
        if (context.PreferredExecutionMode.HasValue)
        {
            var mode = context.PreferredExecutionMode.Value;
            execPassed = privacyPassed
                .Where(x => x.Descriptor.ExecutionModes.Contains(mode))
                .ToList();

            foreach (var removed in privacyPassed.Except(execPassed))
            {
                eliminated.Add(
                    $"{removed.Descriptor.ProviderId}: Step 2 - does not support execution mode {mode}");
            }

            steps.Add(
                $"Step 2 (Execution Mode): Preferred {mode}. " +
                $"{execPassed.Count}/{privacyPassed.Count} remain.");
        }
        else
        {
            steps.Add(
                $"Step 2 (Execution Mode): No preference set. " +
                $"{execPassed.Count}/{privacyPassed.Count} remain.");
        }

        // ── Step 3: Filter by credential mode and credential availability ──

        var credPassed = new List<(IAsrProvider Provider, AsrProviderDescriptor Descriptor)>();

        foreach (var (provider, desc) in execPassed)
        {
            if (!context.PreferredCredentialMode.HasValue)
            {
                credPassed.Add((provider, desc));
                continue;
            }

            var credMode = context.PreferredCredentialMode.Value;

            if (!desc.CredentialModes.Contains(credMode))
            {
                eliminated.Add(
                    $"{desc.ProviderId}: Step 3 - does not support credential mode {credMode}");
                continue;
            }

            // For BYOK modes, verify an active credential exists.
            if (credMode == CredentialMode.USER_BYOK && context.UserId.HasValue)
            {
                var cred = await _credentialManager.FindActiveAsync(
                    desc.ProviderId, CredentialOwnerTypes.User, context.UserId.Value, ct);
                if (cred == null)
                {
                    eliminated.Add(
                        $"{desc.ProviderId}: Step 3 - no active USER_BYOK credential for user {context.UserId}");
                    continue;
                }
            }
            else if (credMode == CredentialMode.TENANT_BYOK && context.TenantId.HasValue)
            {
                var cred = await _credentialManager.FindActiveAsync(
                    desc.ProviderId, CredentialOwnerTypes.Tenant, context.TenantId.Value, ct);
                if (cred == null)
                {
                    eliminated.Add(
                        $"{desc.ProviderId}: Step 3 - no active TENANT_BYOK credential for tenant {context.TenantId}");
                    continue;
                }
            }

            credPassed.Add((provider, desc));
        }

        steps.Add(
            $"Step 3 (Credentials): {credPassed.Count}/{execPassed.Count} remain after credential mode " +
            $"and availability check.");

        // ── Step 4: Filter by provider capability and file limits ──

        var capPassed = credPassed.Where(x =>
        {
            if (x.Descriptor.MaxFileBytes.HasValue &&
                context.FileSizeBytes > x.Descriptor.MaxFileBytes.Value)
            {
                eliminated.Add(
                    $"{x.Descriptor.ProviderId}: Step 4 - file size {context.FileSizeBytes}B " +
                    $"exceeds max {x.Descriptor.MaxFileBytes.Value}B");
                return false;
            }

            if (x.Descriptor.MaxAudioDurationMs.HasValue &&
                context.DurationMs > x.Descriptor.MaxAudioDurationMs.Value)
            {
                eliminated.Add(
                    $"{x.Descriptor.ProviderId}: Step 4 - duration {context.DurationMs}ms " +
                    $"exceeds max {x.Descriptor.MaxAudioDurationMs.Value}ms");
                return false;
            }

            if (x.Descriptor.AcceptedMimeTypes.Count > 0 &&
                !x.Descriptor.AcceptedMimeTypes.Contains(context.MimeType))
            {
                eliminated.Add(
                    $"{x.Descriptor.ProviderId}: Step 4 - MIME type '{context.MimeType}' not accepted");
                return false;
            }

            return true;
        }).ToList();

        steps.Add(
            $"Step 4 (Capability/Limits): {capPassed.Count}/{credPassed.Count} remain after " +
            "file size, duration, and MIME type checks.");

        // ── Step 5: Filter by language and scenario requirements ──

        var langPassed = capPassed.Where(x =>
        {
            if (!string.IsNullOrEmpty(context.Language) &&
                x.Descriptor.SupportedLanguages.Count > 0 &&
                !x.Descriptor.SupportedLanguages.Contains(context.Language) &&
                !x.Descriptor.SupportedLanguages.Contains("*"))
            {
                eliminated.Add(
                    $"{x.Descriptor.ProviderId}: Step 5 - does not support language '{context.Language}'");
                return false;
            }

            if (context.EnableSpeakerDiarization && !x.Descriptor.SupportsDiarization)
            {
                eliminated.Add($"{x.Descriptor.ProviderId}: Step 5 - does not support diarization");
                return false;
            }

            if (context.EnableHotwords && !x.Descriptor.SupportsHotwords)
            {
                eliminated.Add($"{x.Descriptor.ProviderId}: Step 5 - does not support hotwords");
                return false;
            }

            if (context.EnableVad && !x.Descriptor.SupportsVad)
            {
                eliminated.Add($"{x.Descriptor.ProviderId}: Step 5 - does not support VAD");
                return false;
            }

            if (context.EnablePunctuation && !x.Descriptor.SupportsPunctuation)
            {
                eliminated.Add($"{x.Descriptor.ProviderId}: Step 5 - does not support punctuation");
                return false;
            }

            if (context.EnableWordTimestamp && !x.Descriptor.SupportsWordTimestamp)
            {
                eliminated.Add($"{x.Descriptor.ProviderId}: Step 5 - does not support word-level timestamps");
                return false;
            }

            return true;
        }).ToList();

        steps.Add(
            $"Step 5 (Language/Scenario): {langPassed.Count}/{capPassed.Count} remain after " +
            "language, diarization, hotwords, VAD, punctuation, and timestamp checks.");

        // ── Step 6: Sort by health status, latency, and cost ──

        if (langPassed.Count == 0)
        {
            steps.Add("Step 6 (Health/Cost): Skipped - no providers remain after Step 5.");
        }
        else
        {
            var scored = new List<ProviderScore>();
            foreach (var (provider, desc) in langPassed)
            {
                var health = await provider.HealthCheckAsync(ct);
                var estimateTask = provider.EstimateCostAsync(
                    new AsrTranscriptionRequest
                    {
                        DurationMs = context.DurationMs,
                        FileSizeBytes = context.FileSizeBytes,
                        MimeType = context.MimeType,
                    }, ct);
                var costEstimate = estimateTask is not null ? await estimateTask : null;

                scored.Add(new ProviderScore
                {
                    Provider = provider,
                    Descriptor = desc,
                    Health = health,
                    EstimatedCost = costEstimate?.EstimatedCost ?? decimal.MaxValue,
                });
            }

            // Sort: healthy first, then lowest latency, then lowest cost.
            var sorted = scored
                .OrderByDescending(s => s.Health.IsHealthy)
                .ThenBy(s => s.Health.LatencyMs)
                .ThenBy(s => s.EstimatedCost)
                .ToList();

            var best = sorted.First();
            steps.Add(
                $"Step 6 (Health/Cost): Sorted {sorted.Count} provider(s). " +
                $"Best: {best.Descriptor.ProviderId} " +
                $"(healthy={best.Health.IsHealthy}, latency={best.Health.LatencyMs}ms, " +
                $"estCost={best.EstimatedCost}).");

            // ── Step 7: Apply user explicit preference ──
            // User preference can only select from providers that passed security (steps 1-3).
            // It cannot override security constraints.

            IAsrProvider? selected;
            AsrProviderDescriptor? selectedDesc;

            if (!string.IsNullOrEmpty(context.PreferredProviderId))
            {
                var preferred = sorted.FirstOrDefault(s =>
                    s.Descriptor.ProviderId == context.PreferredProviderId);

                if (preferred != null)
                {
                    selected = preferred.Provider;
                    selectedDesc = preferred.Descriptor;
                    steps.Add(
                        $"Step 7 (User Preference): Preferred provider '{context.PreferredProviderId}' " +
                        "is available and passed all security constraints. Selected.");
                }
                else
                {
                    selected = best.Provider;
                    selectedDesc = best.Descriptor;
                    steps.Add(
                        $"Step 7 (User Preference): Preferred provider '{context.PreferredProviderId}' " +
                        "did not pass security constraints (steps 1-3) or was eliminated. " +
                        $"Cannot override. Using best-ranked: {selectedDesc.ProviderId}.");
                }
            }
            else
            {
                selected = best.Provider;
                selectedDesc = best.Descriptor;
                steps.Add(
                    $"Step 7 (User Preference): No explicit preference. " +
                    $"Using best-ranked provider: {selectedDesc.ProviderId}.");
            }

            // ── Step 8: Return best provider ──

            decision.SelectedProviderId = selectedDesc.ProviderId;
            decision.SelectedModelId = selectedDesc.ModelId;
            decision.ExecutionMode = context.PreferredExecutionMode?.ToString()
                ?? selectedDesc.ExecutionModes.FirstOrDefault().ToString() ?? string.Empty;
            decision.CredentialMode = context.PreferredCredentialMode?.ToString()
                ?? selectedDesc.CredentialModes.FirstOrDefault().ToString() ?? string.Empty;

            steps.Add(
                $"Step 8 (Final): Selected provider '{decision.SelectedProviderId}' " +
                $"(model: {decision.SelectedModelId}, execution: {decision.ExecutionMode}, " +
                $"credential: {decision.CredentialMode}).");

            decision.Steps = steps;
            decision.EliminatedProviders = eliminated;

            return (selected, decision);
        }

        // ── Fallback: No provider survived all 8 steps ──

        var fallbackProvider = await ApplyAsrFallbackAsync(
            context, privacyPassed, steps, eliminated, ct);

        if (fallbackProvider != null)
        {
            var desc = await fallbackProvider.GetDescriptorAsync(ct);
            decision.SelectedProviderId = desc.ProviderId;
            decision.SelectedModelId = desc.ModelId;
            decision.ExecutionMode = desc.ExecutionModes.FirstOrDefault().ToString() ?? string.Empty;
            decision.CredentialMode = desc.CredentialModes.FirstOrDefault().ToString() ?? string.Empty;
            decision.FallbackReason = $"Fallback policy '{context.FallbackPolicy}' applied";
        }
        else
        {
            decision.FallbackReason = $"Fallback policy '{context.FallbackPolicy}' failed to find a provider";
        }

        decision.Steps = steps;
        decision.EliminatedProviders = eliminated;

        return (fallbackProvider, decision);
    }

    /// <summary>
    /// Applies the fallback policy when the main 8-step routing fails to find a provider.
    /// Privacy constraints (step 1) are always respected; other constraints are relaxed.
    /// </summary>
    private async Task<IAsrProvider?> ApplyAsrFallbackAsync(
        AsrRoutingContext context,
        List<(IAsrProvider Provider, AsrProviderDescriptor Descriptor)> privacyPassed,
        List<string> steps,
        List<string> eliminated,
        CancellationToken ct)
    {
        if (privacyPassed.Count == 0)
        {
            steps.Add("Fallback: No providers passed privacy filter. Cannot apply fallback.");
            return null;
        }

        var policy = context.FallbackPolicy;

        if (policy == FallbackPolicies.Stop)
        {
            steps.Add("Fallback: Policy is STOP. No fallback will be attempted.");
            _logger.LogWarning(
                "ASR routing failed with STOP policy. {EliminatedCount} providers eliminated.",
                eliminated.Count);
            return null;
        }

        if (policy == FallbackPolicies.LocalFallback)
        {
            var localProviders = privacyPassed
                .Where(x => x.Descriptor.ExecutionModes.Contains(ExecutionMode.LOCAL_DEVICE) ||
                            x.Descriptor.ExecutionModes.Contains(ExecutionMode.LOCAL_LAN_NODE))
                .ToList();

            steps.Add(
                $"Fallback (LOCAL_FALLBACK): Found {localProviders.Count} local provider(s) " +
                "from privacy-passed candidates.");

            if (localProviders.Count > 0)
            {
                // Pick the first healthy local provider, or just the first.
                foreach (var (provider, _) in localProviders)
                {
                    var health = await provider.HealthCheckAsync(ct);
                    if (health.IsHealthy)
                    {
                        steps.Add($"Fallback: Selected healthy local provider.");
                        return provider;
                    }
                }

                steps.Add("Fallback: No healthy local provider found. Using first available.");
                return localProviders.First().Provider;
            }

            steps.Add("Fallback: No local providers available.");
            return null;
        }

        if (policy == FallbackPolicies.PlatformFallback)
        {
            var platformProviders = privacyPassed
                .Where(x => x.Descriptor.ExecutionModes.Contains(ExecutionMode.MEMORIX_CLOUD))
                .ToList();

            steps.Add(
                $"Fallback (PLATFORM_FALLBACK): Found {platformProviders.Count} platform provider(s) " +
                "from privacy-passed candidates.");

            if (platformProviders.Count > 0)
            {
                foreach (var (provider, _) in platformProviders)
                {
                    var health = await provider.HealthCheckAsync(ct);
                    if (health.IsHealthy)
                    {
                        steps.Add("Fallback: Selected healthy platform provider.");
                        return provider;
                    }
                }

                steps.Add("Fallback: No healthy platform provider found. Using first available.");
                return platformProviders.First().Provider;
            }

            steps.Add("Fallback: No platform providers available.");
            return null;
        }

        steps.Add($"Fallback: Unknown policy '{policy}'. No fallback applied.");
        return null;
    }

    // ── TTS Routing (simplified 8-step strategy) ──

    /// <summary>
    /// Internal TTS routing that returns both the resolved provider and a detailed decision log.
    /// </summary>
    private async Task<(ITtsProvider? Provider, RoutingDecision Decision)> ResolveTtsInternalAsync(
        TtsRoutingContext context, CancellationToken ct)
    {
        var decision = new RoutingDecision();
        var steps = new List<string>();
        var eliminated = new List<string>();

        var providers = await _registry.GetTtsProvidersAsync(ct);
        steps.Add($"Step 0 (Load): Retrieved {providers.Count} registered TTS provider(s).");

        if (providers.Count == 0)
        {
            steps.Add("Step 0: No TTS providers registered. Cannot route.");
            decision.Steps = steps;
            decision.EliminatedProviders = eliminated;
            decision.FallbackReason = "No providers registered";
            return (null, decision);
        }

        var candidates = new List<(ITtsProvider Provider, TtsProviderDescriptor Descriptor)>();
        foreach (var p in providers)
        {
            var desc = await p.GetDescriptorAsync(ct);
            candidates.Add((p, desc));
        }

        // ── Step 1: Filter by data privacy level ──

        var privacyPassed = candidates;
        if (context.DataClassification == DataClassification.STRICT_LOCAL)
        {
            privacyPassed = candidates
                .Where(x => !x.Descriptor.SendsAudioOffDevice)
                .ToList();

            foreach (var removed in candidates.Except(privacyPassed))
            {
                eliminated.Add(
                    $"{removed.Descriptor.ProviderId}: Step 1 - sends audio off-device, " +
                    "incompatible with STRICT_LOCAL classification");
            }

            steps.Add(
                $"Step 1 (Privacy): STRICT_LOCAL classification requires on-device-only providers. " +
                $"{privacyPassed.Count}/{candidates.Count} remain.");
        }
        else
        {
            steps.Add(
                $"Step 1 (Privacy): {context.DataClassification} classification allows off-device providers. " +
                $"{privacyPassed.Count}/{candidates.Count} remain.");
        }

        if (privacyPassed.Count == 0)
        {
            steps.Add("Step 1: All providers eliminated by privacy filter.");
            decision.Steps = steps;
            decision.EliminatedProviders = eliminated;
            decision.FallbackReason = "Privacy constraint eliminated all providers";
            return (null, decision);
        }

        // ── Step 2: Filter by execution mode preference ──

        var execPassed = privacyPassed;
        if (context.PreferredExecutionMode.HasValue)
        {
            var mode = context.PreferredExecutionMode.Value;
            execPassed = privacyPassed
                .Where(x => x.Descriptor.ExecutionModes.Contains(mode))
                .ToList();

            foreach (var removed in privacyPassed.Except(execPassed))
            {
                eliminated.Add(
                    $"{removed.Descriptor.ProviderId}: Step 2 - does not support execution mode {mode}");
            }

            steps.Add(
                $"Step 2 (Execution Mode): Preferred {mode}. " +
                $"{execPassed.Count}/{privacyPassed.Count} remain.");
        }
        else
        {
            steps.Add(
                $"Step 2 (Execution Mode): No preference set. " +
                $"{execPassed.Count}/{privacyPassed.Count} remain.");
        }

        // ── Step 3: Filter by credential mode and credential availability ──

        var credPassed = new List<(ITtsProvider Provider, TtsProviderDescriptor Descriptor)>();

        foreach (var (provider, desc) in execPassed)
        {
            if (!context.PreferredCredentialMode.HasValue)
            {
                credPassed.Add((provider, desc));
                continue;
            }

            var credMode = context.PreferredCredentialMode.Value;

            if (!desc.CredentialModes.Contains(credMode))
            {
                eliminated.Add(
                    $"{desc.ProviderId}: Step 3 - does not support credential mode {credMode}");
                continue;
            }

            if (credMode == CredentialMode.USER_BYOK && context.UserId.HasValue)
            {
                var cred = await _credentialManager.FindActiveAsync(
                    desc.ProviderId, CredentialOwnerTypes.User, context.UserId.Value, ct);
                if (cred == null)
                {
                    eliminated.Add(
                        $"{desc.ProviderId}: Step 3 - no active USER_BYOK credential for user {context.UserId}");
                    continue;
                }
            }
            else if (credMode == CredentialMode.TENANT_BYOK && context.TenantId.HasValue)
            {
                var cred = await _credentialManager.FindActiveAsync(
                    desc.ProviderId, CredentialOwnerTypes.Tenant, context.TenantId.Value, ct);
                if (cred == null)
                {
                    eliminated.Add(
                        $"{desc.ProviderId}: Step 3 - no active TENANT_BYOK credential for tenant {context.TenantId}");
                    continue;
                }
            }

            credPassed.Add((provider, desc));
        }

        steps.Add(
            $"Step 3 (Credentials): {credPassed.Count}/{execPassed.Count} remain after credential check.");

        // ── Step 4: Filter by output format and sample rate ──

        var capPassed = credPassed.Where(x =>
        {
            if (x.Descriptor.OutputFormats.Count > 0 &&
                !x.Descriptor.OutputFormats.Contains(context.OutputFormat))
            {
                eliminated.Add(
                    $"{x.Descriptor.ProviderId}: Step 4 - does not support output format '{context.OutputFormat}'");
                return false;
            }

            return true;
        }).ToList();

        steps.Add(
            $"Step 4 (Capability/Limits): {capPassed.Count}/{credPassed.Count} remain after " +
            "output format check.");

        // ── Step 5: Filter by language ──

        var langPassed = capPassed.Where(x =>
        {
            if (!string.IsNullOrEmpty(context.Language) &&
                x.Descriptor.SupportedLanguages.Count > 0 &&
                !x.Descriptor.SupportedLanguages.Contains(context.Language) &&
                !x.Descriptor.SupportedLanguages.Contains("*"))
            {
                eliminated.Add(
                    $"{x.Descriptor.ProviderId}: Step 5 - does not support language '{context.Language}'");
                return false;
            }

            return true;
        }).ToList();

        steps.Add(
            $"Step 5 (Language): {langPassed.Count}/{capPassed.Count} remain after language check.");

        // ── Step 6: Sort by health and cost ──

        if (langPassed.Count == 0)
        {
            steps.Add("Step 6 (Health/Cost): Skipped - no providers remain after Step 5.");
        }
        else
        {
            var scored = new List<TtsProviderScore>();
            foreach (var (provider, desc) in langPassed)
            {
                var health = await provider.HealthCheckAsync(ct);
                var estimateTask = provider.EstimateCostAsync(
                    new TtsRequest
                    {
                        Language = context.Language,
                        OutputFormat = context.OutputFormat,
                    }, ct);
                var costEstimate = estimateTask is not null ? await estimateTask : null;

                scored.Add(new TtsProviderScore
                {
                    Provider = provider,
                    Descriptor = desc,
                    Health = health,
                    EstimatedCost = costEstimate?.EstimatedCost ?? decimal.MaxValue,
                });
            }

            var sorted = scored
                .OrderByDescending(s => s.Health.IsHealthy)
                .ThenBy(s => s.Health.LatencyMs)
                .ThenBy(s => s.EstimatedCost)
                .ToList();

            var best = sorted.First();
            steps.Add(
                $"Step 6 (Health/Cost): Sorted {sorted.Count} provider(s). " +
                $"Best: {best.Descriptor.ProviderId} " +
                $"(healthy={best.Health.IsHealthy}, latency={best.Health.LatencyMs}ms).");

            // ── Step 7: Apply user explicit preference ──

            ITtsProvider? selected;
            TtsProviderDescriptor? selectedDesc;

            if (!string.IsNullOrEmpty(context.PreferredProviderId))
            {
                var preferred = sorted.FirstOrDefault(s =>
                    s.Descriptor.ProviderId == context.PreferredProviderId);

                if (preferred != null)
                {
                    selected = preferred.Provider;
                    selectedDesc = preferred.Descriptor;
                    steps.Add(
                        $"Step 7 (User Preference): Preferred provider '{context.PreferredProviderId}' " +
                        "is available and passed all security constraints. Selected.");
                }
                else
                {
                    selected = best.Provider;
                    selectedDesc = best.Descriptor;
                    steps.Add(
                        $"Step 7 (User Preference): Preferred provider '{context.PreferredProviderId}' " +
                        $"did not pass security constraints. Using best-ranked: {selectedDesc.ProviderId}.");
                }
            }
            else
            {
                selected = best.Provider;
                selectedDesc = best.Descriptor;
                steps.Add(
                    $"Step 7 (User Preference): No explicit preference. " +
                    $"Using best-ranked provider: {selectedDesc.ProviderId}.");
            }

            // ── Step 8: Return best provider ──

            decision.SelectedProviderId = selectedDesc.ProviderId;
            decision.SelectedModelId = selectedDesc.ModelId;
            decision.ExecutionMode = context.PreferredExecutionMode?.ToString()
                ?? selectedDesc.ExecutionModes.FirstOrDefault().ToString() ?? string.Empty;
            decision.CredentialMode = context.PreferredCredentialMode?.ToString()
                ?? selectedDesc.CredentialModes.FirstOrDefault().ToString() ?? string.Empty;

            steps.Add(
                $"Step 8 (Final): Selected provider '{decision.SelectedProviderId}' " +
                $"(model: {decision.SelectedModelId}).");

            decision.Steps = steps;
            decision.EliminatedProviders = eliminated;

            return (selected, decision);
        }

        // ── Fallback for TTS ──

        var fallbackProvider = await ApplyTtsFallbackAsync(
            context, privacyPassed, steps, eliminated, ct);

        if (fallbackProvider != null)
        {
            var desc = await fallbackProvider.GetDescriptorAsync(ct);
            decision.SelectedProviderId = desc.ProviderId;
            decision.SelectedModelId = desc.ModelId;
            decision.FallbackReason = $"Fallback policy '{context.FallbackPolicy}' applied";
        }
        else
        {
            decision.FallbackReason = $"Fallback policy '{context.FallbackPolicy}' failed to find a provider";
        }

        decision.Steps = steps;
        decision.EliminatedProviders = eliminated;

        return (fallbackProvider, decision);
    }

    /// <summary>
    /// Applies the fallback policy for TTS routing.
    /// </summary>
    private async Task<ITtsProvider?> ApplyTtsFallbackAsync(
        TtsRoutingContext context,
        List<(ITtsProvider Provider, TtsProviderDescriptor Descriptor)> privacyPassed,
        List<string> steps,
        List<string> eliminated,
        CancellationToken ct)
    {
        if (privacyPassed.Count == 0)
        {
            steps.Add("Fallback: No providers passed privacy filter. Cannot apply fallback.");
            return null;
        }

        var policy = context.FallbackPolicy;

        if (policy == FallbackPolicies.Stop)
        {
            steps.Add("Fallback: Policy is STOP. No fallback will be attempted.");
            _logger.LogWarning(
                "TTS routing failed with STOP policy. {EliminatedCount} providers eliminated.",
                eliminated.Count);
            return null;
        }

        if (policy == FallbackPolicies.LocalFallback)
        {
            var localProviders = privacyPassed
                .Where(x => x.Descriptor.ExecutionModes.Contains(ExecutionMode.LOCAL_DEVICE) ||
                            x.Descriptor.ExecutionModes.Contains(ExecutionMode.LOCAL_LAN_NODE))
                .ToList();

            steps.Add(
                $"Fallback (LOCAL_FALLBACK): Found {localProviders.Count} local provider(s).");

            if (localProviders.Count > 0)
            {
                foreach (var (provider, _) in localProviders)
                {
                    var health = await provider.HealthCheckAsync(ct);
                    if (health.IsHealthy)
                    {
                        return provider;
                    }
                }

                return localProviders.First().Provider;
            }

            return null;
        }

        if (policy == FallbackPolicies.PlatformFallback)
        {
            var platformProviders = privacyPassed
                .Where(x => x.Descriptor.ExecutionModes.Contains(ExecutionMode.MEMORIX_CLOUD))
                .ToList();

            steps.Add(
                $"Fallback (PLATFORM_FALLBACK): Found {platformProviders.Count} platform provider(s).");

            if (platformProviders.Count > 0)
            {
                foreach (var (provider, _) in platformProviders)
                {
                    var health = await provider.HealthCheckAsync(ct);
                    if (health.IsHealthy)
                    {
                        return provider;
                    }
                }

                return platformProviders.First().Provider;
            }

            return null;
        }

        return null;
    }

    // ── Internal score records ──

    private sealed record ProviderScore
    {
        public required IAsrProvider Provider { get; init; }
        public required AsrProviderDescriptor Descriptor { get; init; }
        public required ProviderHealth Health { get; init; }
        public required decimal EstimatedCost { get; init; }
    }

    private sealed record TtsProviderScore
    {
        public required ITtsProvider Provider { get; init; }
        public required TtsProviderDescriptor Descriptor { get; init; }
        public required ProviderHealth Health { get; init; }
        public required decimal EstimatedCost { get; init; }
    }
}
