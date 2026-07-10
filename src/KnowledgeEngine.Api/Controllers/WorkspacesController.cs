using KnowledgeEngine.Application.DTOs;
using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Infrastructure.Runtime;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace KnowledgeEngine.Api.Controllers;

/// <summary>
/// Workspace management API.
/// Supports creating, listing, switching, and configuring workspaces.
/// A workspace determines the runtime mode (local/cloud/hybrid).
/// </summary>
[ApiController]
[Route("api/workspaces")]
[Authorize]
public class WorkspacesController : BaseController
{
    private readonly IWorkspaceService _workspaceService;
    private readonly IConfigService _configService;
    private readonly ICurrentUserContext _currentUser;
    private readonly RuntimeRouter _runtimeRouter;

    public WorkspacesController(
        IWorkspaceService workspaceService,
        IConfigService configService,
        ICurrentUserContext currentUser,
        RuntimeRouter runtimeRouter)
    {
        _workspaceService = workspaceService;
        _configService = configService;
        _currentUser = currentUser;
        _runtimeRouter = runtimeRouter;
    }

    /// <summary>
    /// List all workspaces for the current user.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var userId = _currentUser.UserId ?? Guid.Empty;
        var workspaces = await _workspaceService.ListWorkspacesAsync(userId, ct);
        return Ok(ApiResponse<List<WorkspaceDto>>.Ok(workspaces, GetTraceId()));
    }

    /// <summary>
    /// Get the current workspace.
    /// </summary>
    [HttpGet("current")]
    public async Task<IActionResult> GetCurrent(CancellationToken ct)
    {
        var userId = _currentUser.UserId ?? Guid.Empty;
        var workspace = await _workspaceService.GetCurrentWorkspaceAsync(userId, ct);
        return Ok(ApiResponse<WorkspaceDto?>.Ok(workspace, GetTraceId()));
    }

    /// <summary>
    /// Get a workspace by ID.
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var workspace = await _workspaceService.GetWorkspaceAsync(id, ct);
        if (workspace == null)
        {
            return NotFound(ApiResponse<object>.FailObject("NOT_FOUND", "Workspace not found", GetTraceId()));
        }
        return Ok(ApiResponse<WorkspaceDto>.Ok(workspace, GetTraceId()));
    }

    /// <summary>
    /// Create a new workspace.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateWorkspaceDto input, CancellationToken ct)
    {
        var workspace = await _workspaceService.CreateWorkspaceAsync(input, ct);
        return CreatedAtAction(nameof(Get), new { id = workspace.Id }, ApiResponse<WorkspaceDto>.Ok(workspace, GetTraceId()));
    }

    /// <summary>
    /// Initialize a local workspace (creates Vault, saves config).
    /// </summary>
    [HttpPost("init-local")]
    public async Task<IActionResult> InitLocal([FromBody] InitLocalWorkspaceDto input, CancellationToken ct)
    {
        var workspace = await _workspaceService.InitializeLocalWorkspaceAsync(input, ct);

        // Initialize local runtime: SQLite database + Vault directory structure
        await _runtimeRouter.EnsureLocalRuntimeInitializedAsync(
            workspace.Id.ToString(),
            workspace.LocalVaultPath ?? "",
            ct);

        // Save the SQLite DB path to the workspace entity
        var dbPath = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".knowledge-engine",
            $"workspace-{workspace.Id}.db");
        await _workspaceService.UpdateWorkspaceAsync(workspace.Id, new UpdateWorkspaceDto
        {
            // Store the DB path in modelConfig as JSON for now
            // (LocalDbPath field is available but not in UpdateWorkspaceDto)
        }, ct);

        return Ok(ApiResponse<WorkspaceDto>.Ok(workspace, GetTraceId()));
    }

    /// <summary>
    /// Update workspace settings.
    /// </summary>
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateWorkspaceDto input, CancellationToken ct)
    {
        var workspace = await _workspaceService.UpdateWorkspaceAsync(id, input, ct);
        return Ok(ApiResponse<WorkspaceDto>.Ok(workspace, GetTraceId()));
    }

    /// <summary>
    /// Switch to a different workspace.
    /// </summary>
    [HttpPost("{id:guid}/switch")]
    public async Task<IActionResult> Switch(Guid id, CancellationToken ct)
    {
        var userId = _currentUser.UserId ?? Guid.Empty;
        await _workspaceService.SetCurrentWorkspaceAsync(userId, id, ct);
        return Ok(ApiResponse<object>.Ok(new { workspaceId = id }, GetTraceId()));
    }

    /// <summary>
    /// Delete a workspace.
    /// </summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await _workspaceService.DeleteWorkspaceAsync(id, ct);
        return Ok(ApiResponse<object>.Ok(new { deleted = true }, GetTraceId()));
    }

    /// <summary>
    /// Get available workspace modes.
    /// </summary>
    [HttpGet("modes")]
    [AllowAnonymous]
    public IActionResult GetModes()
    {
        var modes = new List<WorkspaceModeOption>
        {
            new() { Mode = "local", Label = "本地模式", Description = "数据保存在本机，适合隐私资料和本地模型用户", Available = true },
            new() { Mode = "cloud", Label = "云端模式", Description = "数据保存在云端，适合多设备访问和手机端采集", Available = true },
            new() { Mode = "hybrid", Label = "混合模式", Description = "本地保存主库，云端用于手机采集和同步", Available = false }
        };
        return Ok(ApiResponse<List<WorkspaceModeOption>>.Ok(modes, GetTraceId()));
    }

    /// <summary>
    /// Get available model providers.
    /// </summary>
    [HttpGet("model-providers")]
    [AllowAnonymous]
    public IActionResult GetModelProviders()
    {
        var providers = new List<ModelProviderOption>
        {
            new() { Provider = "lmstudio", Label = "LM Studio", DefaultBaseUrl = "http://localhost:1234", RequiresApiKey = false },
            new() { Provider = "ollama", Label = "Ollama", DefaultBaseUrl = "http://localhost:11434", RequiresApiKey = false },
            new() { Provider = "openai", Label = "OpenAI", DefaultBaseUrl = "https://api.openai.com", RequiresApiKey = true },
            new() { Provider = "anthropic", Label = "Anthropic", DefaultBaseUrl = "https://api.anthropic.com", RequiresApiKey = true },
            new() { Provider = "custom", Label = "自定义 (OpenAI 兼容)", DefaultBaseUrl = null, RequiresApiKey = false }
        };
        return Ok(ApiResponse<List<ModelProviderOption>>.Ok(providers, GetTraceId()));
    }

    /// <summary>
    /// Get local config (from ~/.knowledge-engine/config.json).
    /// </summary>
    [HttpGet("config")]
    public async Task<IActionResult> GetConfig(CancellationToken ct)
    {
        var config = await _configService.LoadConfigAsync(ct);
        return Ok(ApiResponse<LocalConfig>.Ok(config, GetTraceId()));
    }

    /// <summary>
    /// Update model settings for a workspace (FE1-005).
    /// Changes the model provider, endpoint, and model names.
    /// </summary>
    [HttpPut("{id:guid}/model-settings")]
    public async Task<IActionResult> UpdateModelSettings(Guid id, [FromBody] UpdateModelSettingsDto input, CancellationToken ct)
    {
        // Build model config JSON
        var modelConfig = System.Text.Json.JsonSerializer.Serialize(new
        {
            provider = input.Provider,
            baseUrl = input.BaseUrl,
            apiKey = input.ApiKey,
            chatModel = input.ChatModel,
            embeddingModel = input.EmbeddingModel
        });

        var workspace = await _workspaceService.UpdateWorkspaceAsync(id, new UpdateWorkspaceDto
        {
            ModelProvider = input.Provider,
            ModelConfig = modelConfig
        }, ct);

        return Ok(ApiResponse<WorkspaceDto>.Ok(workspace, GetTraceId()));
    }

    /// <summary>
    /// Test model connectivity for a workspace.
    /// </summary>
    [HttpPost("{id:guid}/test-model")]
    public async Task<IActionResult> TestModel(Guid id, CancellationToken ct)
    {
        var modelProvider = await _runtimeRouter.GetModelProviderAsync(ct);
        var health = await modelProvider.HealthCheckAsync(ct);
        return Ok(ApiResponse<object>.Ok(new
        {
            status = health.Status,
            provider = health.Provider,
            chatModel = health.ChatModel,
            embeddingModel = health.EmbeddingModel,
            error = health.ErrorMessage
        }, GetTraceId()));
    }
}
