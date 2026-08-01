using KnowledgeEngine.Application.DTOs;
using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace KnowledgeEngine.Api.Controllers;

/// <summary>
/// Provider marketplace API.
/// Browse, install, uninstall, and rate audio capability providers
/// from the marketplace catalog.
/// </summary>
[ApiController]
[Route("api/audio/marketplace")]
[Authorize]
public class MarketplaceController : BaseController
{
    private readonly IProviderMarketplaceService _marketplace;
    private readonly ILogger<MarketplaceController> _logger;

    public MarketplaceController(
        IProviderMarketplaceService marketplace,
        ILogger<MarketplaceController> logger)
    {
        _marketplace = marketplace;
        _logger = logger;
    }

    /// <summary>
    /// Browses marketplace entries with optional capability and provider filters.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Browse(
        [FromQuery] string? capability,
        [FromQuery] string? providerId,
        CancellationToken ct)
    {
        var entries = await _marketplace.BrowseAsync(capability, providerId, ct);

        return Ok(ApiResponse<List<ProviderMarketplaceEntry>>.Ok(entries, GetTraceId()));
    }

    /// <summary>
    /// Installs a marketplace entry by ID.
    /// Marks the entry as installed and increments the install count.
    /// </summary>
    [HttpPost("{id}/install")]
    public async Task<IActionResult> Install(Guid id, CancellationToken ct)
    {
        try
        {
            var entry = await _marketplace.InstallAsync(id, ct);
            return Ok(ApiResponse<ProviderMarketplaceEntry>.Ok(entry, GetTraceId()));
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(ApiResponse<object>.FailObject(
                "NOT_FOUND", ex.Message, GetTraceId()));
        }
    }

    /// <summary>
    /// Uninstalls a marketplace entry by ID.
    /// Marks the entry as not installed.
    /// </summary>
    [HttpDelete("{id}/install")]
    public async Task<IActionResult> Uninstall(Guid id, CancellationToken ct)
    {
        var removed = await _marketplace.UninstallAsync(id, ct);

        if (!removed)
        {
            return NotFound(ApiResponse<object>.FailObject(
                "NOT_FOUND", $"Marketplace entry {id} not found", GetTraceId()));
        }

        return Ok(ApiResponse<object>.Ok(new { id, status = "uninstalled" }, GetTraceId()));
    }

    /// <summary>
    /// Rates a marketplace entry. Rating must be between 0 and 5.
    /// </summary>
    [HttpPost("{id}/rate")]
    public async Task<IActionResult> Rate(Guid id, [FromBody] RateEntryRequest request, CancellationToken ct)
    {
        if (request.Rating < 0 || request.Rating > 5)
        {
            return BadRequest(ApiResponse<object>.FailObject(
                "INVALID_RATING", "Rating must be between 0 and 5", GetTraceId()));
        }

        try
        {
            await _marketplace.RateAsync(id, request.Rating, ct);
            return Ok(ApiResponse<object>.Ok(new { id, rating = request.Rating }, GetTraceId()));
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(ApiResponse<object>.FailObject(
                "NOT_FOUND", ex.Message, GetTraceId()));
        }
    }
}

/// <summary>
/// Request body for rating a marketplace entry.
/// </summary>
public class RateEntryRequest
{
    public int Rating { get; set; }
}
