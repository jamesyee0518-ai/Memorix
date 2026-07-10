using System.Text.Json;
using KnowledgeEngine.Application.DTOs;
using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeEngine.Api.Controllers;

[Authorize]
[Route("api/agent-profiles")]
public class AgentProfilesController : BaseController
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUserContext _currentUser;
    private readonly IAgentToolService _agentToolService;
    private readonly IWebHostEnvironment _env;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public AgentProfilesController(
        IAppDbContext db,
        ICurrentUserContext currentUser,
        IAgentToolService agentToolService,
        IWebHostEnvironment env)
    {
        _db = db;
        _currentUser = currentUser;
        _agentToolService = agentToolService;
        _env = env;
    }

    // ===== GET /api/agent-profiles =====
    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var userId = _currentUser.UserId;
        if (userId == null) return Unauthorized();

        var profiles = await _db.AgentProfiles
            .Where(p => p.UserId == userId.Value)
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync(ct);

        var items = profiles.Select(MapToDto).ToList();
        return Ok(ApiResponse<List<AgentProfileDto>>.Ok(items, GetTraceId()));
    }

    // ===== GET /api/agent-profiles/{id} =====
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById([FromRoute] Guid id, CancellationToken ct)
    {
        var userId = _currentUser.UserId;
        if (userId == null) return Unauthorized();

        var profile = await _db.AgentProfiles
            .FirstOrDefaultAsync(p => p.Id == id && p.UserId == userId.Value, ct);

        if (profile == null)
        {
            return Ok(ApiResponse<AgentProfileDto>.Fail("not_found", "Agent Profile not found", GetTraceId()));
        }

        return Ok(ApiResponse<AgentProfileDto>.Ok(MapToDto(profile), GetTraceId()));
    }

    // ===== POST /api/agent-profiles =====
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] AgentProfileUpsertRequest request, CancellationToken ct)
    {
        var userId = _currentUser.UserId;
        if (userId == null) return Unauthorized();

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return Ok(ApiResponse<AgentProfileDto>.Fail("validation_error", "Name is required", GetTraceId()));
        }

        var now = DateTime.UtcNow;
        var profile = new AgentProfile
        {
            Id = Guid.NewGuid(),
            UserId = userId.Value,
            Name = request.Name,
            Description = request.Description,
            AllowedToolNames = request.AllowedToolNames != null
                ? JsonSerializer.Serialize(request.AllowedToolNames, JsonOptions)
                : null,
            AllowedTopicIds = request.AllowedTopicIds != null
                ? JsonSerializer.Serialize(request.AllowedTopicIds, JsonOptions)
                : null,
            AllowSensitiveDocuments = request.AllowSensitiveDocuments ?? false,
            MaxResultsPerCall = request.MaxResultsPerCall ?? 20,
            RateLimitPerMinute = request.RateLimitPerMinute ?? 60,
            DailyQuota = request.DailyQuota ?? 1000,
            ApiKeyId = request.ApiKeyId,
            Transport = string.IsNullOrWhiteSpace(request.Transport) ? "stdio" : request.Transport,
            McpServerPath = request.McpServerPath,
            Status = "active",
            CreatedAt = now,
            UpdatedAt = now
        };

        _db.AgentProfiles.Add(profile);
        await _db.SaveChangesAsync(ct);

        return StatusCode(201, ApiResponse<AgentProfileDto>.Ok(MapToDto(profile), GetTraceId()));
    }

    // ===== PUT /api/agent-profiles/{id} =====
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update([FromRoute] Guid id, [FromBody] AgentProfileUpsertRequest request, CancellationToken ct)
    {
        var userId = _currentUser.UserId;
        if (userId == null) return Unauthorized();

        var profile = await _db.AgentProfiles
            .FirstOrDefaultAsync(p => p.Id == id && p.UserId == userId.Value, ct);

        if (profile == null)
        {
            return Ok(ApiResponse<AgentProfileDto>.Fail("not_found", "Agent Profile not found", GetTraceId()));
        }

        if (!string.IsNullOrWhiteSpace(request.Name))
            profile.Name = request.Name;

        profile.Description = request.Description;
        profile.AllowedToolNames = request.AllowedToolNames != null
            ? JsonSerializer.Serialize(request.AllowedToolNames, JsonOptions)
            : null;
        profile.AllowedTopicIds = request.AllowedTopicIds != null
            ? JsonSerializer.Serialize(request.AllowedTopicIds, JsonOptions)
            : null;
        profile.AllowSensitiveDocuments = request.AllowSensitiveDocuments ?? false;
        profile.MaxResultsPerCall = request.MaxResultsPerCall ?? profile.MaxResultsPerCall;
        profile.RateLimitPerMinute = request.RateLimitPerMinute ?? profile.RateLimitPerMinute;
        profile.DailyQuota = request.DailyQuota ?? profile.DailyQuota;
        profile.ApiKeyId = request.ApiKeyId;
        profile.Transport = string.IsNullOrWhiteSpace(request.Transport) ? profile.Transport : request.Transport;
        profile.McpServerPath = request.McpServerPath;
        profile.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);

        return Ok(ApiResponse<AgentProfileDto>.Ok(MapToDto(profile), GetTraceId()));
    }

    // ===== DELETE /api/agent-profiles/{id} =====
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete([FromRoute] Guid id, CancellationToken ct)
    {
        var userId = _currentUser.UserId;
        if (userId == null) return Unauthorized();

        var profile = await _db.AgentProfiles
            .FirstOrDefaultAsync(p => p.Id == id && p.UserId == userId.Value, ct);

        if (profile == null)
        {
            return Ok(ApiResponse<object>.Fail("not_found", "Agent Profile not found", GetTraceId()));
        }

        _db.AgentProfiles.Remove(profile);
        await _db.SaveChangesAsync(ct);

        return Ok(ApiResponse<object>.Ok(new { id, deleted = true }, GetTraceId()));
    }

    // ===== GET /api/agent-profiles/{id}/mcp-config =====
    [HttpGet("{id:guid}/mcp-config")]
    public async Task<IActionResult> GetMcpConfig([FromRoute] Guid id, CancellationToken ct)
    {
        var userId = _currentUser.UserId;
        if (userId == null) return Unauthorized();

        var profile = await _db.AgentProfiles
            .FirstOrDefaultAsync(p => p.Id == id && p.UserId == userId.Value, ct);

        if (profile == null)
        {
            return Ok(ApiResponse<object>.Fail("not_found", "Agent Profile not found", GetTraceId()));
        }

        // Derive the API project path from the content root
        var apiProjectPath = Path.GetFullPath(Path.Combine(_env.ContentRootPath));

        var config = new McpConfigResponse
        {
            McpServers = new McpServerEntry
            {
                Memorix = new McpServerConfig
                {
                    Command = "dotnet",
                    Args = new List<string> { "run", "--project", apiProjectPath, "--", "--mcp" },
                    Env = new Dictionary<string, string>
                    {
                        ["MEMORIX_MCP_USER_ID"] = userId.Value.ToString(),
                        ["MEMORIX_AGENT_PROFILE_ID"] = profile.Id.ToString(),
                        ["ASPNETCORE_ENVIRONMENT"] = _env.EnvironmentName
                    }
                }
            }
        };

        return Ok(ApiResponse<McpConfigResponse>.Ok(config, GetTraceId()));
    }

    // ===== POST /api/agent-profiles/{id}/test =====
    [HttpPost("{id:guid}/test")]
    public async Task<IActionResult> TestConnection([FromRoute] Guid id, CancellationToken ct)
    {
        var userId = _currentUser.UserId;
        if (userId == null) return Unauthorized();

        var profile = await _db.AgentProfiles
            .FirstOrDefaultAsync(p => p.Id == id && p.UserId == userId.Value, ct);

        if (profile == null)
        {
            return Ok(ApiResponse<object>.Fail("not_found", "Agent Profile not found", GetTraceId()));
        }

        try
        {
            var tools = await _agentToolService.ListToolsAsync(profile.Id, ct);
            var toolDtos = tools.Select(t => new AgentToolDefinitionDto
            {
                Name = t.Name,
                Description = t.Description,
                InputSchema = t.InputSchema
            }).ToList();

            var result = new AgentTestResult
            {
                Success = true,
                Message = $"Connection successful. {toolDtos.Count} tool(s) available.",
                Tools = toolDtos
            };

            return Ok(ApiResponse<AgentTestResult>.Ok(result, GetTraceId()));
        }
        catch (Exception ex)
        {
            var result = new AgentTestResult
            {
                Success = false,
                Message = $"Connection failed: {ex.Message}"
            };

            return Ok(ApiResponse<AgentTestResult>.Ok(result, GetTraceId()));
        }
    }

    // ===== GET /api/agent-profiles/tools =====
    [HttpGet("tools")]
    public async Task<IActionResult> ListTools([FromQuery] string? profileId, CancellationToken ct)
    {
        var userId = _currentUser.UserId;
        if (userId == null) return Unauthorized();

        Guid? pid = null;
        if (!string.IsNullOrWhiteSpace(profileId) && Guid.TryParse(profileId, out var parsed))
        {
            pid = parsed;
        }

        var tools = await _agentToolService.ListToolsAsync(pid, ct);
        var toolDtos = tools.Select(t => new AgentToolDefinitionDto
        {
            Name = t.Name,
            Description = t.Description,
            InputSchema = t.InputSchema
        }).ToList();

        return Ok(ApiResponse<List<AgentToolDefinitionDto>>.Ok(toolDtos, GetTraceId()));
    }

    // ===== GET /api/agent-profiles/logs =====
    [HttpGet("logs")]
    public async Task<IActionResult> GetLogs(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? toolName = null,
        [FromQuery] string? status = null,
        CancellationToken ct = default)
    {
        var userId = _currentUser.UserId;
        if (userId == null) return Unauthorized();

        var query = _db.AgentInvocationLogs
            .Where(l => l.UserId == userId.Value);

        if (!string.IsNullOrWhiteSpace(toolName))
        {
            query = query.Where(l => l.ToolName == toolName);
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(l => l.Status == status);
        }

        var total = await query.CountAsync(ct);

        var logs = await query
            .OrderByDescending(l => l.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var items = logs.Select(l => new AgentInvocationLogDto
        {
            Id = l.Id,
            AgentProfileId = l.AgentProfileId,
            Transport = l.Transport,
            ToolName = l.ToolName,
            Status = l.Status,
            ResultCount = l.ResultCount,
            LatencyMs = l.LatencyMs,
            ErrorCode = l.ErrorCode,
            ErrorMessage = l.ErrorMessage,
            CreatedAt = l.CreatedAt
        }).ToList();

        var paged = new PagedResult<AgentInvocationLogDto>
        {
            Items = items,
            Total = total,
            Page = page,
            PageSize = pageSize
        };

        return Ok(ApiResponse<PagedResult<AgentInvocationLogDto>>.Ok(paged, GetTraceId()));
    }

    // ===== Helpers =====

    private AgentProfileDto MapToDto(AgentProfile p)
    {
        return new AgentProfileDto
        {
            Id = p.Id,
            Name = p.Name,
            Description = p.Description,
            AllowedToolNames = DeserializeStringList(p.AllowedToolNames),
            AllowedTopicIds = DeserializeStringList(p.AllowedTopicIds),
            AllowSensitiveDocuments = p.AllowSensitiveDocuments,
            MaxResultsPerCall = p.MaxResultsPerCall,
            RateLimitPerMinute = p.RateLimitPerMinute,
            DailyQuota = p.DailyQuota,
            ApiKeyId = p.ApiKeyId,
            Transport = p.Transport,
            McpServerPath = p.McpServerPath,
            Status = p.Status,
            CreatedAt = p.CreatedAt,
            UpdatedAt = p.UpdatedAt
        };
    }

    private static List<string>? DeserializeStringList(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<List<string>>(json);
        }
        catch
        {
            return null;
        }
    }
}

// ===== DTOs =====

public class AgentProfileDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public List<string>? AllowedToolNames { get; set; }
    public List<string>? AllowedTopicIds { get; set; }
    public bool AllowSensitiveDocuments { get; set; }
    public int MaxResultsPerCall { get; set; }
    public int RateLimitPerMinute { get; set; }
    public int DailyQuota { get; set; }
    public Guid? ApiKeyId { get; set; }
    public string Transport { get; set; } = "stdio";
    public string? McpServerPath { get; set; }
    public string Status { get; set; } = "active";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class AgentProfileUpsertRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public List<string>? AllowedToolNames { get; set; }
    public List<string>? AllowedTopicIds { get; set; }
    public bool? AllowSensitiveDocuments { get; set; }
    public int? MaxResultsPerCall { get; set; }
    public int? RateLimitPerMinute { get; set; }
    public int? DailyQuota { get; set; }
    public Guid? ApiKeyId { get; set; }
    public string? Transport { get; set; }
    public string? McpServerPath { get; set; }
}

public class AgentInvocationLogDto
{
    public Guid Id { get; set; }
    public Guid? AgentProfileId { get; set; }
    public string Transport { get; set; } = string.Empty;
    public string ToolName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int? ResultCount { get; set; }
    public int LatencyMs { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class AgentToolDefinitionDto
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public Dictionary<string, object> InputSchema { get; set; } = new();
}

public class AgentTestResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public List<AgentToolDefinitionDto>? Tools { get; set; }
}

public class McpConfigResponse
{
    public McpServerEntry McpServers { get; set; } = new();
}

public class McpServerEntry
{
    public McpServerConfig Memorix { get; set; } = new();
}

public class McpServerConfig
{
    public string Command { get; set; } = string.Empty;
    public List<string> Args { get; set; } = new();
    public Dictionary<string, string> Env { get; set; } = new();
}
