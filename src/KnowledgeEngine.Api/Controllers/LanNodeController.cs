using KnowledgeEngine.Application.DTOs;
using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeEngine.Api.Controllers;

/// <summary>
/// LAN node discovery and management API.
/// Lists, discovers, registers, and unregisters LAN compute nodes
/// that can execute audio capabilities on the local network.
/// </summary>
[ApiController]
[Route("api/audio/lan-nodes")]
[Authorize]
public class LanNodeController : BaseController
{
    private readonly ILanNodeDiscovery _nodeDiscovery;
    private readonly IAppDbContext _db;
    private readonly ILogger<LanNodeController> _logger;

    public LanNodeController(
        ILanNodeDiscovery nodeDiscovery,
        IAppDbContext db,
        ILogger<LanNodeController> logger)
    {
        _nodeDiscovery = nodeDiscovery;
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// Lists all registered LAN nodes.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> ListNodes(CancellationToken ct)
    {
        var nodes = await _db.LanNodes
            .OrderByDescending(n => n.RegisteredAt)
            .ToListAsync(ct);

        return Ok(ApiResponse<List<LanNode>>.Ok(nodes, GetTraceId()));
    }

    /// <summary>
    /// Triggers LAN node discovery by probing configured endpoints.
    /// Returns the list of nodes found online.
    /// </summary>
    [HttpPost("discover")]
    public async Task<IActionResult> DiscoverNodes(CancellationToken ct)
    {
        var discovered = await _nodeDiscovery.DiscoverNodesAsync(ct);

        return Ok(ApiResponse<List<LanNode>>.Ok(discovered, GetTraceId()));
    }

    /// <summary>
    /// Registers a LAN node at the given endpoint URL.
    /// If a node with the same endpoint already exists, it is re-registered.
    /// </summary>
    [HttpPost("register")]
    public async Task<IActionResult> RegisterNode([FromBody] RegisterLanNodeRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Endpoint))
        {
            return BadRequest(ApiResponse<object>.FailObject(
                "MISSING_ENDPOINT", "Endpoint URL is required", GetTraceId()));
        }

        if (!Uri.TryCreate(request.Endpoint, UriKind.Absolute, out _))
        {
            return BadRequest(ApiResponse<object>.FailObject(
                "INVALID_ENDPOINT", "Endpoint must be a valid absolute URL", GetTraceId()));
        }

        var node = await _nodeDiscovery.RegisterNodeAsync(request.Endpoint, ct);

        return Ok(ApiResponse<LanNode>.Ok(node, GetTraceId()));
    }

    /// <summary>
    /// Unregisters (removes) a LAN node by ID.
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> UnregisterNode(Guid id, CancellationToken ct)
    {
        var removed = await _nodeDiscovery.UnregisterNodeAsync(id, ct);

        if (!removed)
        {
            return NotFound(ApiResponse<object>.FailObject(
                "NOT_FOUND", $"LAN node {id} not found", GetTraceId()));
        }

        return Ok(ApiResponse<object>.Ok(new { id, status = "unregistered" }, GetTraceId()));
    }
}

/// <summary>
/// Request body for registering a LAN node.
/// </summary>
public class RegisterLanNodeRequest
{
    public string Endpoint { get; set; } = string.Empty;
}
