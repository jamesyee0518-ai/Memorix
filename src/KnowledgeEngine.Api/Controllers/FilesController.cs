using KnowledgeEngine.Application.DTOs;
using KnowledgeEngine.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace KnowledgeEngine.Api.Controllers;

[Authorize]
[Route("api/[controller]")]
public class FilesController : BaseController
{
    private readonly FileStorageService _fileStorageService;

    public FilesController(FileStorageService fileStorageService)
    {
        _fileStorageService = fileStorageService;
    }

    [HttpGet("{id:guid}/download-url")]
    public async Task<IActionResult> GetDownloadUrl([FromRoute] Guid id, CancellationToken ct)
    {
        var result = await _fileStorageService.GetDownloadUrlAsync(id, ct);
        return Ok(ApiResponse<object>.Ok(result.Data!, GetTraceId()));
    }
}
