using KnowledgeEngine.Application.DTOs;
using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace KnowledgeEngine.Api.Controllers;

/// <summary>
/// Benchmark API for running model evaluations, retrieving results,
/// and producing ranked leaderboards.
/// </summary>
[ApiController]
[Route("api/audio/benchmark")]
[Authorize]
public class BenchmarkController : BaseController
{
    private readonly IBenchmarkService _benchmarkService;
    private readonly ICurrentUserContext _currentUser;
    private readonly ILogger<BenchmarkController> _logger;

    public BenchmarkController(
        IBenchmarkService benchmarkService,
        ICurrentUserContext currentUser,
        ILogger<BenchmarkController> logger)
    {
        _benchmarkService = benchmarkService;
        _currentUser = currentUser;
        _logger = logger;
    }

    /// <summary>
    /// Runs a benchmark on the specified model.
    /// Performs a health check on the model's provider and records
    /// throughput, RTF, and TTFB metrics.
    /// </summary>
    [HttpPost("run")]
    public async Task<IActionResult> RunBenchmark(
        [FromBody] RunBenchmarkRequest request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
        {
            return Unauthorized(ApiResponse<object>.FailObject(
                "UNAUTHORIZED", "User not authenticated", GetTraceId()));
        }

        if (request.ModelRegistryId == Guid.Empty)
        {
            return BadRequest(ApiResponse<object>.FailObject(
                "MISSING_MODEL_ID", "ModelRegistryId is required", GetTraceId()));
        }

        try
        {
            var result = await _benchmarkService.RunBenchmarkAsync(
                request.ModelRegistryId, request.DatasetName ?? string.Empty, ct);
            return Ok(ApiResponse<BenchmarkResult>.Ok(result, GetTraceId()));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(ApiResponse<object>.FailObject(
                "MODEL_NOT_FOUND",
                $"Model registry entry with id {request.ModelRegistryId} not found",
                GetTraceId()));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Benchmark failed for model {ModelRegistryId}",
                request.ModelRegistryId);
            return StatusCode(500, ApiResponse<object>.FailObject(
                "BENCHMARK_FAILED", ex.Message, GetTraceId()));
        }
    }

    /// <summary>
    /// Retrieves benchmark results with optional filters.
    /// </summary>
    /// <param name="modelRegistryId">Filter by model registry ID.</param>
    /// <param name="benchmarkName">Filter by benchmark name.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpGet("results")]
    public async Task<IActionResult> GetResults(
        [FromQuery] Guid? modelRegistryId,
        [FromQuery] string? benchmarkName,
        CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
        {
            return Unauthorized(ApiResponse<object>.FailObject(
                "UNAUTHORIZED", "User not authenticated", GetTraceId()));
        }

        var results = await _benchmarkService.GetResultsAsync(
            modelRegistryId, benchmarkName, ct);
        return Ok(ApiResponse<List<BenchmarkResult>>.Ok(results, GetTraceId()));
    }

    /// <summary>
    /// Produces a ranked leaderboard of models for the given category.
    /// Valid categories: fastest, most_accurate, lowest_cost, best_chinese, best_mobile, best_meeting.
    /// </summary>
    /// <param name="category">Ranking category.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpGet("rankings/{category}")]
    public async Task<IActionResult> GetRankings(string category, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
        {
            return Unauthorized(ApiResponse<object>.FailObject(
                "UNAUTHORIZED", "User not authenticated", GetTraceId()));
        }

        var validCategories = new[]
        {
            BenchmarkRankings.Fastest,
            BenchmarkRankings.MostAccurate,
            BenchmarkRankings.LowestCost,
            BenchmarkRankings.BestChinese,
            BenchmarkRankings.BestMobile,
            BenchmarkRankings.BestMeeting,
        };

        if (!validCategories.Contains(category))
        {
            return BadRequest(ApiResponse<object>.FailObject(
                "INVALID_CATEGORY",
                $"Unknown ranking category '{category}'. Valid categories: {string.Join(", ", validCategories)}",
                GetTraceId()));
        }

        var rankings = await _benchmarkService.GetRankingsAsync(category, ct);
        return Ok(ApiResponse<List<RankingEntry>>.Ok(rankings, GetTraceId()));
    }
}

// ── Controller Request DTOs ──

/// <summary>
/// Request payload for triggering a benchmark run.
/// </summary>
public class RunBenchmarkRequest
{
    public Guid ModelRegistryId { get; set; }

    /// <summary>Name of the evaluation dataset (e.g. "aishell-1", "commonvoice-chinese").</summary>
    public string? DatasetName { get; set; }
}
