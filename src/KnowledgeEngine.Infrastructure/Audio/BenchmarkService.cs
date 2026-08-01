using KnowledgeEngine.Application.DTOs;
using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Domain.Entities;
using KnowledgeEngine.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KnowledgeEngine.Infrastructure.Audio;

/// <summary>
/// EF Core-backed implementation of <see cref="IBenchmarkService"/>.
/// Runs health-check-driven benchmarks on registered models, stores results,
/// and produces ranked leaderboards by category.
/// </summary>
public class BenchmarkService : IBenchmarkService
{
    private readonly IAppDbContext _db;
    private readonly IProviderRegistry _providerRegistry;
    private readonly ILogger<BenchmarkService> _logger;

    public BenchmarkService(
        IAppDbContext db,
        IProviderRegistry providerRegistry,
        ILogger<BenchmarkService> logger)
    {
        _db = db;
        _providerRegistry = providerRegistry;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<BenchmarkResult> RunBenchmarkAsync(
        Guid modelRegistryId, string datasetName, CancellationToken ct)
    {
        // ── Load the model registry entry ──

        var model = await _db.ModelRegistries
            .FirstOrDefaultAsync(m => m.Id == modelRegistryId, ct);

        if (model == null)
        {
            throw new KeyNotFoundException(
                $"Model registry entry with id {modelRegistryId} not found.");
        }

        // ── Run a health check on the model's provider ──

        ProviderHealth? health = null;
        CostEstimate? costEstimate = null;

        if (IsTtsCapability(model.Capability))
        {
            var provider = await _providerRegistry.GetTtsProviderByIdAsync(model.ProviderId, ct);
            if (provider != null)
            {
                health = await provider.HealthCheckAsync(ct);
                var estimateTask = provider.EstimateCostAsync(
                    new TtsRequest { Language = "zh" }, ct);
                costEstimate = estimateTask is not null ? await estimateTask : null;
            }
        }
        else
        {
            var provider = await _providerRegistry.GetAsrProviderByIdAsync(model.ProviderId, ct);
            if (provider != null)
            {
                health = await provider.HealthCheckAsync(ct);
                var estimateTask = provider.EstimateCostAsync(
                    new AsrTranscriptionRequest
                    {
                        DurationMs = 60_000,
                        FileSizeBytes = 1_920_000,
                        MimeType = "audio/wav",
                    }, ct);
                costEstimate = estimateTask is not null ? await estimateTask : null;
            }
        }

        // ── Record throughput / RTF / TTFB from the health check ──

        var latencyMs = health?.LatencyMs ?? 0;
        var isHealthy = health?.IsHealthy ?? false;

        // TTFB approximated from provider latency.
        var ttfb = latencyMs > 0 ? latencyMs : 0;

        // Throughput: estimated segments per second from latency.
        // A lower latency implies higher throughput. We use a simple inverse model.
        var throughput = latencyMs > 0
            ? Math.Round(1000m / latencyMs, 4)
            : 0m;

        // RTF: real-time factor approximated from latency relative to a 1-second reference.
        var rtf = latencyMs > 0
            ? Math.Round(latencyMs / 1000m, 4)
            : 0m;

        // ── Build and store the benchmark result ──

        var result = new BenchmarkResult
        {
            Id = Guid.NewGuid(),
            ModelRegistryId = modelRegistryId,
            BenchmarkName = string.IsNullOrWhiteSpace(datasetName)
                ? $"benchmark-{model.ProviderId}-{model.ModelId}"
                : datasetName,
            Cer = 0m,           // Requires ground-truth dataset comparison (not available in health-check mode)
            Wer = 0m,            // Requires ground-truth dataset comparison
            Rtf = rtf,
            Ttfb = ttfb,
            Throughput = throughput,
            UnitCost = costEstimate?.EstimatedCost ?? 0m,
            EvaluatedAt = DateTime.UtcNow,
            DatasetName = string.IsNullOrWhiteSpace(datasetName) ? null : datasetName,
            Notes = isHealthy
                ? $"Health check passed (latency={latencyMs}ms)"
                : $"Health check failed (latency={latencyMs}ms)",
            CreatedAt = DateTime.UtcNow,
        };

        _db.BenchmarkResults.Add(result);
        await _db.SaveChangesAsync(ct);

        // ── Update the model's health status ──

        var healthStatus = isHealthy
            ? ModelRegistryStatuses.Healthy
            : ModelRegistryStatuses.Unhealthy;

        model.HealthStatus = healthStatus;
        model.LastHealthCheckAt = DateTime.UtcNow;
        model.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Benchmark completed for model {ProviderId}/{ModelId}: " +
            "TTFB={Ttfb}ms, throughput={Throughput} seg/s, RTF={Rtf}, unitCost={UnitCost}",
            model.ProviderId, model.ModelId, result.Ttfb, result.Throughput, result.Rtf, result.UnitCost);

        return result;
    }

    /// <inheritdoc/>
    public async Task<List<BenchmarkResult>> GetResultsAsync(
        Guid? modelRegistryId, string? benchmarkName, CancellationToken ct)
    {
        var query = _db.BenchmarkResults.AsNoTracking().AsQueryable();

        if (modelRegistryId.HasValue)
        {
            query = query.Where(r => r.ModelRegistryId == modelRegistryId.Value);
        }

        if (!string.IsNullOrWhiteSpace(benchmarkName))
        {
            query = query.Where(r => r.BenchmarkName == benchmarkName);
        }

        return await query
            .OrderByDescending(r => r.EvaluatedAt)
            .ToListAsync(ct);
    }

    /// <inheritdoc/>
    public async Task<List<RankingEntry>> GetRankingsAsync(string category, CancellationToken ct)
    {
        // ── Load all benchmark results joined with their model registry entries ──

        var joined = await _db.BenchmarkResults
            .Join(
                _db.ModelRegistries,
                br => br.ModelRegistryId,
                mr => mr.Id,
                (br, mr) => new { br, mr })
            .ToListAsync(ct);

        // ── Get the latest result per model to avoid duplicate entries ──

        var latestPerModel = joined
            .GroupBy(x => x.mr.Id)
            .Select(g => g.OrderByDescending(x => x.br.EvaluatedAt).First())
            .ToList();

        // ── Apply category-specific filtering and sorting ──

        IEnumerable<(Guid ModelRegistryId, string ProviderId, string ModelId, string DisplayName, decimal Score, string Metric)> ranked;

        switch (category)
        {
            case BenchmarkRankings.Fastest:
                // By throughput descending.
                ranked = latestPerModel
                    .OrderByDescending(x => x.br.Throughput)
                    .Select(x => (x.mr.Id, x.mr.ProviderId, x.mr.ModelId, x.mr.DisplayName,
                        x.br.Throughput, "throughput"));
                break;

            case BenchmarkRankings.MostAccurate:
                // By CER ascending (lower is better).
                ranked = latestPerModel
                    .OrderBy(x => x.br.Cer)
                    .Select(x => (x.mr.Id, x.mr.ProviderId, x.mr.ModelId, x.mr.DisplayName,
                        x.br.Cer, "cer"));
                break;

            case BenchmarkRankings.LowestCost:
                // By unit cost ascending.
                ranked = latestPerModel
                    .OrderBy(x => x.br.UnitCost)
                    .Select(x => (x.mr.Id, x.mr.ProviderId, x.mr.ModelId, x.mr.DisplayName,
                        x.br.UnitCost, "unit_cost"));
                break;

            case BenchmarkRankings.BestChinese:
                // By CER ascending on datasets containing 'chinese'.
                ranked = latestPerModel
                    .Where(x => x.br.DatasetName != null &&
                                x.br.DatasetName.Contains("chinese", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(x => x.br.Cer)
                    .Select(x => (x.mr.Id, x.mr.ProviderId, x.mr.ModelId, x.mr.DisplayName,
                        x.br.Cer, "cer"));
                break;

            case BenchmarkRankings.BestMobile:
                // By TTFB ascending (lower is better for mobile/realtime).
                ranked = latestPerModel
                    .OrderBy(x => x.br.Ttfb)
                    .Select(x => (x.mr.Id, x.mr.ProviderId, x.mr.ModelId, x.mr.DisplayName,
                        (decimal)x.br.Ttfb, "ttfb"));
                break;

            case BenchmarkRankings.BestMeeting:
                // By speaker accuracy descending (higher is better for meeting scenarios).
                ranked = latestPerModel
                    .Where(x => x.br.SpeakerAccuracy.HasValue)
                    .OrderByDescending(x => x.br.SpeakerAccuracy!.Value)
                    .Select(x => (x.mr.Id, x.mr.ProviderId, x.mr.ModelId, x.mr.DisplayName,
                        x.br.SpeakerAccuracy!.Value, "speaker_accuracy"));
                break;

            default:
                _logger.LogWarning("Unknown ranking category '{Category}'. Returning empty list.", category);
                return new List<RankingEntry>();
        }

        // ── Assign 1-based ranks ──

        var entries = ranked
            .Select((x, index) => new RankingEntry
            {
                ModelRegistryId = x.ModelRegistryId,
                ProviderId = x.ProviderId,
                ModelId = x.ModelId,
                DisplayName = x.DisplayName,
                Score = x.Score,
                Metric = x.Metric,
                Rank = index + 1,
            })
            .ToList();

        return entries;
    }

    /// <summary>
    /// Determines whether the capability string refers to a TTS (synthesis) capability.
    /// </summary>
    private static bool IsTtsCapability(string capability)
    {
        return string.Equals(capability, AudioCapabilities.Synthesis, StringComparison.OrdinalIgnoreCase);
    }
}
