using KnowledgeEngine.Application.DTOs;
using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace KnowledgeEngine.Api.Controllers;

/// <summary>
/// Prompt Registry and A/B Testing API.
/// Manages versioned prompts (create, publish, archive) and A/B tests
/// for comparing prompt variants.
/// </summary>
[ApiController]
[Route("api/prompts")]
[Authorize]
public class PromptRegistryController : BaseController
{
    private readonly IPromptRegistryService _promptService;
    private readonly IPromptABTestService _abTestService;
    private readonly ICurrentUserContext _currentUser;
    private readonly ILogger<PromptRegistryController> _logger;

    public PromptRegistryController(
        IPromptRegistryService promptService,
        IPromptABTestService abTestService,
        ICurrentUserContext currentUser,
        ILogger<PromptRegistryController> logger)
    {
        _promptService = promptService;
        _abTestService = abTestService;
        _currentUser = currentUser;
        _logger = logger;
    }

    // ===== Prompt Registry Endpoints =====

    /// <summary>
    /// Gets the active (published) prompt for the given key,
    /// optionally filtered by language.
    /// </summary>
    [HttpGet("{key}/active")]
    public async Task<IActionResult> GetActivePrompt(
        string key, [FromQuery] string? language, CancellationToken ct)
    {
        try
        {
            var prompt = await _promptService.GetActivePromptAsync(key, language, ct);
            return Ok(ApiResponse<PromptRegistry>.Ok(prompt, GetTraceId()));
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(ApiResponse<object>.FailObject(
                "PROMPT_NOT_FOUND", ex.Message, GetTraceId()));
        }
    }

    /// <summary>
    /// Lists all versions of a prompt by key.
    /// </summary>
    [HttpGet("{key}/versions")]
    public async Task<IActionResult> ListVersions(string key, CancellationToken ct)
    {
        var versions = await _promptService.ListVersionsAsync(key, ct);
        return Ok(ApiResponse<List<PromptRegistry>>.Ok(versions, GetTraceId()));
    }

    /// <summary>
    /// Creates a new prompt version in draft status.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> CreatePrompt(
        [FromBody] CreatePromptRequest request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated || _currentUser.UserId == null)
        {
            return Unauthorized(ApiResponse<object>.FailObject(
                "UNAUTHORIZED", "User not authenticated", GetTraceId()));
        }

        if (string.IsNullOrWhiteSpace(request.PromptKey))
        {
            return BadRequest(ApiResponse<object>.FailObject(
                "MISSING_PROMPT_KEY", "PromptKey is required", GetTraceId()));
        }

        if (string.IsNullOrWhiteSpace(request.SystemPrompt))
        {
            return BadRequest(ApiResponse<object>.FailObject(
                "MISSING_SYSTEM_PROMPT", "SystemPrompt is required", GetTraceId()));
        }

        var prompt = new PromptRegistry
        {
            PromptKey = request.PromptKey,
            Version = request.Version ?? "1.0.0",
            Title = request.Title ?? request.PromptKey,
            Description = request.Description,
            SystemPrompt = request.SystemPrompt,
            UserPromptTemplate = request.UserPromptTemplate ?? string.Empty,
            Language = request.Language,
            ProviderCompatibility = request.ProviderCompatibility ?? string.Empty,
            CreatedBy = _currentUser.Email ?? _currentUser.UserId.Value.ToString()
        };

        var created = await _promptService.CreateAsync(prompt, ct);

        return Ok(ApiResponse<PromptRegistry>.Ok(created, GetTraceId()));
    }

    /// <summary>
    /// Publishes a draft prompt, activating it and archiving the previous active version.
    /// </summary>
    [HttpPost("{id}/publish")]
    public async Task<IActionResult> PublishPrompt(Guid id, CancellationToken ct)
    {
        try
        {
            var published = await _promptService.PublishAsync(id, ct);
            return Ok(ApiResponse<PromptRegistry>.Ok(published, GetTraceId()));
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(ApiResponse<object>.FailObject(
                "PROMPT_NOT_FOUND", ex.Message, GetTraceId()));
        }
    }

    /// <summary>
    /// Archives a published prompt, deactivating it.
    /// </summary>
    [HttpPost("{id}/archive")]
    public async Task<IActionResult> ArchivePrompt(Guid id, CancellationToken ct)
    {
        try
        {
            var archived = await _promptService.ArchiveAsync(id, ct);
            return Ok(ApiResponse<PromptRegistry>.Ok(archived, GetTraceId()));
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(ApiResponse<object>.FailObject(
                "PROMPT_NOT_FOUND", ex.Message, GetTraceId()));
        }
    }

    // ===== A/B Test Endpoints =====

    /// <summary>
    /// Creates a new A/B test in "created" status.
    /// </summary>
    [HttpPost("abtest")]
    public async Task<IActionResult> CreateABTest(
        [FromBody] CreateABTestRequest request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated || _currentUser.UserId == null)
        {
            return Unauthorized(ApiResponse<object>.FailObject(
                "UNAUTHORIZED", "User not authenticated", GetTraceId()));
        }

        if (request.VariantAId == Guid.Empty || request.VariantBId == Guid.Empty)
        {
            return BadRequest(ApiResponse<object>.FailObject(
                "MISSING_VARIANT", "Both VariantAId and VariantBId are required", GetTraceId()));
        }

        var test = new PromptABTest
        {
            Name = request.Name ?? "Untitled A/B Test",
            VariantAId = request.VariantAId,
            VariantBId = request.VariantBId,
            TrafficSplitPercent = request.TrafficSplitPercent,
            CreatedBy = _currentUser.Email ?? _currentUser.UserId.Value.ToString()
        };

        try
        {
            var created = await _abTestService.CreateTestAsync(test, ct);
            return Ok(ApiResponse<PromptABTest>.Ok(created, GetTraceId()));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<object>.FailObject(
                "ABTEST_CREATE_FAILED", ex.Message, GetTraceId()));
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return BadRequest(ApiResponse<object>.FailObject(
                "INVALID_TRAFFIC_SPLIT", ex.Message, GetTraceId()));
        }
    }

    /// <summary>
    /// Starts an A/B test, transitioning it to "running" status.
    /// </summary>
    [HttpPost("abtest/{id}/start")]
    public async Task<IActionResult> StartABTest(Guid id, CancellationToken ct)
    {
        try
        {
            var started = await _abTestService.StartTestAsync(id, ct);
            return Ok(ApiResponse<PromptABTest>.Ok(started, GetTraceId()));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<object>.FailObject(
                "ABTEST_START_FAILED", ex.Message, GetTraceId()));
        }
    }

    /// <summary>
    /// Completes an A/B test, recording the winning variant.
    /// </summary>
    [HttpPost("abtest/{id}/complete")]
    public async Task<IActionResult> CompleteABTest(
        Guid id, [FromBody] CompleteABTestRequest request, CancellationToken ct)
    {
        if (request.WinnerVariantId == Guid.Empty)
        {
            return BadRequest(ApiResponse<object>.FailObject(
                "MISSING_WINNER", "WinnerVariantId is required", GetTraceId()));
        }

        try
        {
            await _abTestService.CompleteTestAsync(id, request.WinnerVariantId, ct);
            return Ok(ApiResponse<object>.Ok(
                new { testId = id, winnerVariantId = request.WinnerVariantId, status = "completed" },
                GetTraceId()));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<object>.FailObject(
                "ABTEST_COMPLETE_FAILED", ex.Message, GetTraceId()));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ApiResponse<object>.FailObject(
                "INVALID_WINNER_VARIANT", ex.Message, GetTraceId()));
        }
    }
}

// ===== Request DTOs =====

public class CreatePromptRequest
{
    public string PromptKey { get; set; } = string.Empty;
    public string? Version { get; set; }
    public string? Title { get; set; }
    public string? Description { get; set; }
    public string SystemPrompt { get; set; } = string.Empty;
    public string? UserPromptTemplate { get; set; }
    public string? Language { get; set; }
    public string? ProviderCompatibility { get; set; }
}

public class CreateABTestRequest
{
    public string? Name { get; set; }
    public Guid VariantAId { get; set; }
    public Guid VariantBId { get; set; }
    public int TrafficSplitPercent { get; set; }
}

public class CompleteABTestRequest
{
    public Guid WinnerVariantId { get; set; }
}
