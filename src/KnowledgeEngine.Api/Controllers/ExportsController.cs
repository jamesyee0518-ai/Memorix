using KnowledgeEngine.Application.DTOs;
using KnowledgeEngine.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace KnowledgeEngine.Api.Controllers;

[Authorize]
[Route("api/exports")]
public class ExportsController : BaseController
{
    private readonly IExportService _exportService;
    private readonly ICurrentUserContext _currentUser;
    private readonly IAppDbContext _dbContext;
    private readonly IFileStorageFactory _fileStorageFactory;

    public ExportsController(
        IExportService exportService,
        ICurrentUserContext currentUser,
        IAppDbContext dbContext,
        IFileStorageFactory fileStorageFactory)
    {
        _exportService = exportService;
        _currentUser = currentUser;
        _dbContext = dbContext;
        _fileStorageFactory = fileStorageFactory;
    }

    [HttpPost("document/markdown")]
    public async Task<IActionResult> ExportDocumentMarkdown([FromBody] ExportDocumentRequest request, CancellationToken ct)
    {
        var userId = _currentUser.UserId;
        if (userId == null)
        {
            return Unauthorized();
        }

        var result = await _exportService.ExportDocumentMarkdownAsync(userId.Value, request, ct);

        if (!result.Success)
        {
            return Ok(ApiResponse<ExportJobResponse>.Fail(result.Error!.Code, result.Error!.Message, GetTraceId()));
        }

        return Ok(ApiResponse<ExportJobResponse>.Ok(result.Data!, GetTraceId()));
    }

    [HttpPost("report/markdown")]
    public async Task<IActionResult> ExportReportMarkdown([FromBody] ExportReportRequest request, CancellationToken ct)
    {
        var userId = _currentUser.UserId;
        if (userId == null)
        {
            return Unauthorized();
        }

        var result = await _exportService.ExportReportMarkdownAsync(userId.Value, request, ct);

        if (!result.Success)
        {
            return Ok(ApiResponse<ExportJobResponse>.Fail(result.Error!.Code, result.Error!.Message, GetTraceId()));
        }

        return Ok(ApiResponse<ExportJobResponse>.Ok(result.Data!, GetTraceId()));
    }

    [HttpPost("report/json")]
    public async Task<IActionResult> ExportReportJson([FromBody] ExportReportJsonRequest request, CancellationToken ct)
    {
        var userId = _currentUser.UserId;
        if (userId == null)
        {
            return Unauthorized();
        }

        var result = await _exportService.ExportReportJsonAsync(userId.Value, request, ct);

        if (!result.Success)
        {
            return Ok(ApiResponse<ExportJobResponse>.Fail(result.Error!.Code, result.Error!.Message, GetTraceId()));
        }

        return Ok(ApiResponse<ExportJobResponse>.Ok(result.Data!, GetTraceId()));
    }

    [HttpPost("topic/obsidian")]
    public async Task<IActionResult> ExportTopicObsidian([FromBody] ExportTopicRequest request, CancellationToken ct)
    {
        var userId = _currentUser.UserId;
        if (userId == null)
        {
            return Unauthorized();
        }

        var result = await _exportService.ExportTopicObsidianAsync(userId.Value, request, ct);

        if (!result.Success)
        {
            return Ok(ApiResponse<ExportJobResponse>.Fail(result.Error!.Code, result.Error!.Message, GetTraceId()));
        }

        return Ok(ApiResponse<ExportJobResponse>.Ok(result.Data!, GetTraceId()));
    }

    [HttpPost("search/json")]
    public async Task<IActionResult> ExportSearchJson([FromBody] ExportSearchRequest request, CancellationToken ct)
    {
        var userId = _currentUser.UserId;
        if (userId == null)
        {
            return Unauthorized();
        }

        var result = await _exportService.ExportSearchJsonAsync(userId.Value, request, ct);

        if (!result.Success)
        {
            return Ok(ApiResponse<ExportJobResponse>.Fail(result.Error!.Code, result.Error!.Message, GetTraceId()));
        }

        return Ok(ApiResponse<ExportJobResponse>.Ok(result.Data!, GetTraceId()));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetExportJob([FromRoute] Guid id, CancellationToken ct)
    {
        var userId = _currentUser.UserId;
        if (userId == null)
        {
            return Unauthorized();
        }

        var result = await _exportService.GetExportJobAsync(userId.Value, id, ct);

        if (!result.Success)
        {
            return Ok(ApiResponse<ExportJobDetail>.Fail(result.Error!.Code, result.Error!.Message, GetTraceId()));
        }

        return Ok(ApiResponse<ExportJobDetail>.Ok(result.Data!, GetTraceId()));
    }

    [HttpPost("{id:guid}/open-directory")]
    public async Task<IActionResult> OpenDirectory([FromRoute] Guid id, CancellationToken ct)
    {
        var userId = _currentUser.UserId;
        if (userId == null)
        {
            return Unauthorized();
        }

        // 获取导出任务
        var job = await _dbContext.ExportJobs
            .FirstOrDefaultAsync(j => j.Id == id && j.UserId == userId.Value, ct);

        if (job == null)
        {
            return Ok(ApiResponse<object>.Fail("export_job_not_found", "导出任务不存在", GetTraceId()));
        }

        if (job.Status != "done")
        {
            return Ok(ApiResponse<object>.Fail("export_not_done", "导出任务尚未完成，无法打开目录", GetTraceId()));
        }

        if (!job.FileId.HasValue)
        {
            return Ok(ApiResponse<object>.Fail("no_file", "导出任务没有关联的文件", GetTraceId()));
        }

        // 获取关联的 FileObject
        var file = await _dbContext.Files
            .FirstOrDefaultAsync(f => f.Id == job.FileId.Value && f.WorkspaceId == userId.Value, ct);

        if (file == null)
        {
            return Ok(ApiResponse<object>.Fail("file_not_found", "文件记录不存在", GetTraceId()));
        }

        // 根据文件记录所属工作区选择存储提供者，避免当前配置已切换到其他工作区。
        var storageProvider = await _fileStorageFactory.GetProviderForWorkspaceAsync(
            file.WorkspaceId.ToString(), ct);

        // 获取本地文件路径（云端模式返回 null）
        var filePath = await storageProvider.GetFilePathAsync(file.Bucket, file.ObjectKey, ct);

        if (string.IsNullOrEmpty(filePath))
        {
            // 云端模式（MinIO/S3）不支持打开本地目录
            return Ok(ApiResponse<object>.Fail("cloud_mode_unsupported", "云端模式不支持打开本地目录", GetTraceId()));
        }

        if (!System.IO.File.Exists(filePath))
        {
            return Ok(ApiResponse<object>.Fail("file_not_exists", "文件不存在于本地路径", GetTraceId()));
        }

        // 获取文件所在目录
        var directory = System.IO.Path.GetDirectoryName(filePath);
        if (string.IsNullOrEmpty(directory) || !System.IO.Directory.Exists(directory))
        {
            return Ok(ApiResponse<object>.Fail("directory_not_exists", "文件所在目录不存在", GetTraceId()));
        }

        try
        {
            // 根据操作系统选择打开目录的命令
            var (fileName, arguments) = OpenDirectoryCommand(directory);
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = true
            };
            Process.Start(psi);

            return Ok(ApiResponse<object>.Ok(new { directory }, GetTraceId()));
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse<object>.Fail("open_directory_failed", $"打开目录失败: {ex.Message}", GetTraceId()));
        }
    }

    /// <summary>
    /// 根据当前操作系统返回打开目录的命令。
    /// </summary>
    private static (string FileName, string Arguments) OpenDirectoryCommand(string directory)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            // macOS: 使用 open 命令打开 Finder
            return ("open", directory);
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // Windows: 使用 explorer.exe 打开资源管理器
            return ("explorer.exe", directory);
        }

        // Linux 及其他平台: 使用 xdg-open
        return ("xdg-open", directory);
    }
}
