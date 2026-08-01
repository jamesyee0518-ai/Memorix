using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Domain.Entities;
using KnowledgeEngine.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace KnowledgeEngine.Infrastructure.Audio;

/// <summary>
/// Startup seeder that populates the <c>model_registries</c> and
/// <c>benchmark_results</c> tables with sample benchmark data for the known
/// ASR models referenced in the V2.0 development plan.
/// </summary>
/// <remarks>
/// The seeder implements <see cref="IHostedService"/> so it runs exactly once
/// during application startup. It is fully idempotent:
/// <list type="bullet">
/// <item>Model registry entries are matched by ProviderId + ModelId + Capability.</item>
/// <item>Benchmark results are matched by ModelRegistryId + BenchmarkName + DatasetName.</item>
/// </list>
/// Existing entries are left untouched and only missing entries are inserted.
/// </remarks>
public sealed class BenchmarkSeeder : IHostedService
{
    private const string SeedBenchmarkName = "aishell-1";
    private const string SeedDatasetName = "aishell-1";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<BenchmarkSeeder> _logger;

    public BenchmarkSeeder(
        IServiceScopeFactory scopeFactory,
        ILogger<BenchmarkSeeder> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await SeedAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Seeding failures must not crash application startup.
            _logger.LogError(ex, "Failed to seed benchmark evaluation data");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task SeedAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();

        var seedModels = BuildSeedModels();

        // ── Step 1: Ensure model registry entries exist ──

        var providerModelPairs = seedModels
            .Select(m => m.ProviderId)
            .Distinct()
            .ToHashSet();

        var existingModels = await db.ModelRegistries
            .Where(m => providerModelPairs.Contains(m.ProviderId)
                        && m.Capability == AudioCapabilities.Transcription)
            .ToListAsync(ct);

        var existingModelKeys = existingModels
            .Select(m => (m.ProviderId, m.ModelId, m.Capability))
            .ToHashSet();

        var modelsToAdd = seedModels
            .Where(m => !existingModelKeys.Contains((m.ProviderId, m.ModelId, m.Capability)))
            .ToList();

        foreach (var model in modelsToAdd)
        {
            db.ModelRegistries.Add(model);
        }

        if (modelsToAdd.Count > 0)
        {
            await db.SaveChangesAsync(ct);
            _logger.LogInformation(
                "Benchmark seeder: inserted {Count} model registry entries",
                modelsToAdd.Count);
        }

        // Refresh the full list (existing + newly added) keyed by provider for lookup.
        var allModels = existingModels.Concat(modelsToAdd).ToList();

        // ── Step 2: Ensure benchmark results exist ──

        var modelIds = allModels.Select(m => m.Id).ToHashSet();

        var existingResults = await db.BenchmarkResults
            .Where(r => modelIds.Contains(r.ModelRegistryId)
                        && r.BenchmarkName == SeedBenchmarkName
                        && r.DatasetName == SeedDatasetName)
            .Select(r => r.ModelRegistryId)
            .ToListAsync(ct);

        var existingResultModelIds = existingResults.ToHashSet();

        var resultsToAdd = new List<BenchmarkResult>();
        foreach (var model in allModels)
        {
            if (existingResultModelIds.Contains(model.Id))
            {
                continue;
            }

            var spec = GetBenchmarkSpec(model.ProviderId, model.ModelId);
            if (spec == null)
            {
                continue;
            }

            resultsToAdd.Add(BuildBenchmarkResult(model.Id, spec));
        }

        if (resultsToAdd.Count == 0)
        {
            _logger.LogInformation(
                "Benchmark seeder: all benchmark results already exist, nothing to insert");
            return;
        }

        foreach (var result in resultsToAdd)
        {
            db.BenchmarkResults.Add(result);
        }

        await db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Benchmark seeder: inserted {Count} benchmark result entries",
            resultsToAdd.Count);
    }

    /// <summary>
    /// Builds the list of seed <see cref="ModelRegistry"/> entries for the known
    /// ASR models. These represent the baseline model registrations that the
    /// benchmark results reference.
    /// </summary>
    private static List<ModelRegistry> BuildSeedModels()
    {
        var now = DateTime.UtcNow;

        return
        [
            new ModelRegistry
            {
                Id = Guid.NewGuid(),
                ProviderId = "whispercpp",
                ModelId = "whisper-large-v3",
                DisplayName = "Whisper Large v3 (whisper.cpp)",
                Capability = AudioCapabilities.Transcription,
                ExecutionModes = "LOCAL_DEVICE,LOCAL_LAN_NODE",
                CredentialModes = "NO_CREDENTIAL",
                SupportedLanguages = "zh,en,ja",
                AcceptedMimeTypes = "audio/wav,audio/mp3,audio/m4a,audio/flac",
                SupportsStreaming = false,
                SupportsBatch = true,
                SupportsVad = false,
                SupportsPunctuation = false,
                SupportsDiarization = false,
                SupportsHotwords = false,
                SupportsWordTimestamp = true,
                SupportsSegmentTimestamp = true,
                SendsAudioOffDevice = false,
                StoresProviderData = false,
                PricingUnit = null,
                DataRegion = null,
                IsEnabled = true,
                HealthStatus = ModelRegistryStatuses.Unknown,
                CreatedAt = now,
                UpdatedAt = now,
            },
            new ModelRegistry
            {
                Id = Guid.NewGuid(),
                ProviderId = "funasr",
                ModelId = "paraformer-zh",
                DisplayName = "Paraformer Chinese (FunASR)",
                Capability = AudioCapabilities.Transcription,
                ExecutionModes = "LOCAL_DEVICE,LOCAL_LAN_NODE",
                CredentialModes = "NO_CREDENTIAL",
                SupportedLanguages = "zh",
                AcceptedMimeTypes = "audio/wav,audio/mp3,audio/m4a,audio/flac",
                SupportsStreaming = true,
                SupportsBatch = true,
                SupportsVad = true,
                SupportsPunctuation = true,
                SupportsDiarization = false,
                SupportsHotwords = true,
                SupportsWordTimestamp = true,
                SupportsSegmentTimestamp = true,
                SendsAudioOffDevice = false,
                StoresProviderData = false,
                PricingUnit = null,
                DataRegion = null,
                IsEnabled = true,
                HealthStatus = ModelRegistryStatuses.Unknown,
                CreatedAt = now,
                UpdatedAt = now,
            },
            new ModelRegistry
            {
                Id = Guid.NewGuid(),
                ProviderId = "zhipu",
                ModelId = "glm-asr-2512",
                DisplayName = "GLM-ASR-2512 (Zhipu)",
                Capability = AudioCapabilities.Transcription,
                ExecutionModes = "THIRD_PARTY_CLOUD,MEMORIX_CLOUD",
                CredentialModes = "USER_BYOK,TENANT_BYOK,PLATFORM_MANAGED",
                SupportedLanguages = "zh,en",
                AcceptedMimeTypes = "audio/wav,audio/mp3,audio/m4a,audio/flac",
                SupportsStreaming = true,
                SupportsBatch = true,
                SupportsVad = true,
                SupportsPunctuation = true,
                SupportsDiarization = true,
                SupportsHotwords = false,
                SupportsWordTimestamp = true,
                SupportsSegmentTimestamp = true,
                SendsAudioOffDevice = true,
                StoresProviderData = false,
                PricingUnit = "SECOND",
                DataRegion = "CN",
                IsEnabled = true,
                HealthStatus = ModelRegistryStatuses.Unknown,
                CreatedAt = now,
                UpdatedAt = now,
            },
        ];
    }

    /// <summary>
    /// Returns the benchmark specification (CER, WER, RTF, and supplementary metrics)
    /// for a given provider/model pair, or <c>null</c> if no seed data is defined.
    /// </summary>
    private static BenchmarkSpec? GetBenchmarkSpec(string providerId, string modelId)
    {
        return (providerId, modelId) switch
        {
            ("whispercpp", "whisper-large-v3") => new BenchmarkSpec(
                Cer: 0.085m,
                Wer: 0.12m,
                Rtf: 0.3m,
                GpuMemoryMb: 4200,
                CpuMemoryMb: null,
                Ttfb: 800,
                Throughput: 3.3m,
                ProperNounAccuracy: 0.82m,
                TimestampDeviationMs: 220m,
                SpeakerAccuracy: null,
                UserModificationRate: 0.15m,
                UnitCost: 0m,
                Notes: "Baseline evaluation on AISHELL-1 test set. " +
                       "Strong multilingual support; higher RTF on CPU-only deployments."),

            ("funasr", "paraformer-zh") => new BenchmarkSpec(
                Cer: 0.055m,
                Wer: 0.09m,
                Rtf: 0.15m,
                GpuMemoryMb: 2200,
                CpuMemoryMb: null,
                Ttfb: 350,
                Throughput: 6.7m,
                ProperNounAccuracy: 0.88m,
                TimestampDeviationMs: 150m,
                SpeakerAccuracy: null,
                UserModificationRate: 0.10m,
                UnitCost: 0m,
                Notes: "Best Chinese-specific accuracy on AISHELL-1. " +
                       "Streaming support and hotword integration; low RTF on GPU."),

            ("zhipu", "glm-asr-2512") => new BenchmarkSpec(
                Cer: 0.04m,
                Wer: 0.07m,
                Rtf: 0.1m,
                GpuMemoryMb: null,
                CpuMemoryMb: null,
                Ttfb: 200,
                Throughput: 10.0m,
                ProperNounAccuracy: 0.92m,
                TimestampDeviationMs: 120m,
                SpeakerAccuracy: 0.90m,
                UserModificationRate: 0.07m,
                UnitCost: 0.01m,
                Notes: "Cloud-based ASR with diarization support. " +
                       "Lowest CER/WER and RTF; requires network connectivity."),

            _ => null,
        };
    }

    /// <summary>
    /// Builds a <see cref="BenchmarkResult"/> from a model ID and benchmark specification.
    /// </summary>
    private static BenchmarkResult BuildBenchmarkResult(Guid modelRegistryId, BenchmarkSpec spec)
    {
        var now = DateTime.UtcNow;

        return new BenchmarkResult
        {
            Id = Guid.NewGuid(),
            ModelRegistryId = modelRegistryId,
            BenchmarkName = SeedBenchmarkName,
            Cer = spec.Cer,
            Wer = spec.Wer,
            Rtf = spec.Rtf,
            GpuMemoryMb = spec.GpuMemoryMb,
            CpuMemoryMb = spec.CpuMemoryMb,
            Ttfb = spec.Ttfb,
            Throughput = spec.Throughput,
            ProperNounAccuracy = spec.ProperNounAccuracy,
            TimestampDeviationMs = spec.TimestampDeviationMs,
            SpeakerAccuracy = spec.SpeakerAccuracy,
            UserModificationRate = spec.UserModificationRate,
            UnitCost = spec.UnitCost,
            EvaluatedAt = now,
            DatasetName = SeedDatasetName,
            Notes = spec.Notes,
            CreatedAt = now,
        };
    }

    /// <summary>
    /// Internal record holding the benchmark metrics for a single model.
    /// </summary>
    private sealed record BenchmarkSpec(
        decimal Cer,
        decimal Wer,
        decimal Rtf,
        int? GpuMemoryMb,
        int? CpuMemoryMb,
        long Ttfb,
        decimal Throughput,
        decimal? ProperNounAccuracy,
        decimal? TimestampDeviationMs,
        decimal? SpeakerAccuracy,
        decimal? UserModificationRate,
        decimal UnitCost,
        string Notes);
}
