using KnowledgeEngine.Application.DTOs;
using KnowledgeEngine.Application.Services;
using KnowledgeEngine.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeEngine.Api.Controllers;

[Authorize]
[Route("api/[controller]")]
public class FilesController : BaseController
{
    private readonly FileStorageService _fileStorageService;
    private readonly IAppDbContext _db;
    private readonly IWorkspaceAuthorizationService _workspaceAuthorization;

    public FilesController(
        FileStorageService fileStorageService,
        IAppDbContext db,
        IWorkspaceAuthorizationService workspaceAuthorization)
    {
        _fileStorageService = fileStorageService;
        _db = db;
        _workspaceAuthorization = workspaceAuthorization;
    }

    [HttpGet("{id:guid}/download-url")]
    public async Task<IActionResult> GetDownloadUrl([FromRoute] Guid id, CancellationToken ct)
    {
        var workspaceId = await _db.Files
            .Where(x => x.Id == id)
            .Select(x => (Guid?)x.WorkspaceId)
            .FirstOrDefaultAsync(ct);
        if (!workspaceId.HasValue)
        {
            return NotFound(ApiResponse<object>.FailObject(
                "FILE_NOT_FOUND", "文件不存在", GetTraceId()));
        }
        if (await _workspaceAuthorization.AuthorizeAsync(workspaceId.Value, ct) !=
            WorkspaceAccessResult.Allowed)
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                ApiResponse<object>.FailObject(
                    "WORKSPACE_FORBIDDEN", "无权访问该工作区文件", GetTraceId()));
        }
        var result = await _fileStorageService.GetDownloadUrlAsync(id, ct);
        return Ok(ApiResponse<object>.Ok(result.Data!, GetTraceId()));
    }
}
