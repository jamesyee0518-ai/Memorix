using System.Diagnostics;
using System.Text;
using KnowledgeEngine.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace KnowledgeEngine.Application.Services;

/// <summary>
/// Processes media inbox items into text and imports the result into the local knowledge base.
/// Image OCR uses the local "tesseract" CLI. Audio transcription uses the local "whisper" CLI.
/// </summary>
public class MediaProcessingService
{
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromMinutes(10);
    private readonly IKnowledgeRepository _repo;
    private readonly IFileStorageFactory _fileStorageFactory;
    private readonly InboxImportService _inboxImportService;
    private readonly IPushNotificationService _pushNotifications;
    private readonly ILogger<MediaProcessingService> _logger;

    public MediaProcessingService(
        IKnowledgeRepository repo,
        IFileStorageFactory fileStorageFactory,
        InboxImportService inboxImportService,
        IPushNotificationService pushNotifications,
        ILogger<MediaProcessingService> logger)
    {
        _repo = repo;
        _fileStorageFactory = fileStorageFactory;
        _inboxImportService = inboxImportService;
        _pushNotifications = pushNotifications;
        _logger = logger;
    }

    public async Task<SourceDto> ProcessAndImportAsync(string inboxItemId, CancellationToken ct = default)
    {
        var item = await _repo.GetInboxItemAsync(inboxItemId, ct);
        if (item == null)
        {
            throw new InvalidOperationException($"Inbox item not found: {inboxItemId}");
        }

        if (item.InputType != "image" && item.InputType != "audio")
        {
            throw new InvalidOperationException($"Inbox item {inboxItemId} is not a media item.");
        }

        await _repo.UpdateInboxItemStatusAsync(inboxItemId, "processing", null, ct);
        await _repo.CreateInboxEventAsync(item.WorkspaceId, inboxItemId, "media_processing_started",
            $"{{\"inputType\":\"{item.InputType}\"}}", null, ct);

        try
        {
            var mediaPath = await ResolveMediaPathAsync(item, ct);
            var text = item.InputType == "image"
                ? await RunOcrAsync(mediaPath, ct)
                : await RunTranscriptionAsync(mediaPath, ct);

            if (string.IsNullOrWhiteSpace(text))
            {
                throw new InvalidOperationException(item.InputType == "image"
                    ? "OCR 未识别到可用文本。"
                    : "音频转写未生成可用文本。");
            }

            var title = item.Title ?? (item.InputType == "image" ? "图片 OCR 文本" : "音频转写文本");
            await _repo.UpdateInboxItemAsync(inboxItemId, new UpdateInboxItemInput
            {
                Title = title,
                ContentText = text.Trim(),
                TopicId = item.TopicId
            }, ct);
            await _repo.CreateInboxEventAsync(item.WorkspaceId, inboxItemId, "media_processed",
                $"{{\"inputType\":\"{item.InputType}\",\"textLength\":{text.Trim().Length}}}", null, ct);

            var source = await _inboxImportService.ImportOneAsync(inboxItemId, item.TopicId, ct);
            _logger.LogInformation("Processed and imported media inbox item {InboxItemId}", inboxItemId);
            return source;
        }
        catch (Exception ex)
        {
            await _repo.UpdateInboxItemStatusAsync(inboxItemId, "failed", ex.Message, ct);
            await _repo.CreateInboxEventAsync(item.WorkspaceId, inboxItemId, "media_processing_failed",
                $"{{\"inputType\":\"{item.InputType}\",\"error\":\"{EscapeJson(ex.Message)}\"}}", null, ct);
            await _pushNotifications.SendToDeviceAsync(
                item.WorkspaceId,
                item.OriginDeviceId,
                item.InputType == "image" ? "图片 OCR 失败" : "音频转写失败",
                ex.Message,
                new Dictionary<string, string>
                {
                    ["event"] = "media_processing_failed",
                    ["inboxItemId"] = inboxItemId,
                    ["inputType"] = item.InputType
                },
                ct);
            _logger.LogError(ex, "Failed to process media inbox item {InboxItemId}", inboxItemId);
            throw;
        }
    }

    private async Task<string> ResolveMediaPathAsync(InboxItemDto item, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(item.FilePath) && File.Exists(item.FilePath))
        {
            return item.FilePath;
        }

        var attachments = item.Attachments.Count > 0
            ? item.Attachments
            : await _repo.ListInboxAttachmentsAsync(item.Id, ct);
        var attachment = attachments.FirstOrDefault();
        if (attachment == null)
        {
            throw new InvalidOperationException("未找到媒体附件。");
        }

        var file = await _repo.GetFileObjectAsync(attachment.FileId, ct);
        if (file == null)
        {
            throw new InvalidOperationException("未找到媒体文件对象。");
        }

        if (!string.IsNullOrWhiteSpace(file.LocalPath) && File.Exists(file.LocalPath))
        {
            return file.LocalPath;
        }

        if (string.IsNullOrWhiteSpace(file.Bucket) || string.IsNullOrWhiteSpace(file.ObjectKey))
        {
            throw new InvalidOperationException("媒体文件没有可读取的本地路径或对象存储位置。");
        }

        var provider = await _fileStorageFactory.GetProviderForWorkspaceAsync(item.WorkspaceId, ct);
        await using var stream = await provider.DownloadFileAsync(file.Bucket, file.ObjectKey, ct);
        var extension = file.Extension;
        if (string.IsNullOrWhiteSpace(extension))
        {
            extension = Path.GetExtension(file.OriginalFilename);
        }
        if (!string.IsNullOrWhiteSpace(extension) && !extension.StartsWith('.'))
        {
            extension = $".{extension}";
        }
        if (string.IsNullOrWhiteSpace(extension))
        {
            extension = item.InputType == "audio" ? ".audio" : ".image";
        }

        var tempPath = Path.Combine(Path.GetTempPath(), $"memorix-media-{Guid.NewGuid():N}{extension}");
        await using var output = File.Create(tempPath);
        await stream.CopyToAsync(output, ct);
        return tempPath;
    }

    private static async Task<string> RunOcrAsync(string imagePath, CancellationToken ct)
    {
        var configuredLanguage = Environment.GetEnvironmentVariable("MEMORIX_OCR_LANG");
        var language = configuredLanguage ?? "eng+chi_sim";
        try
        {
            var result = await RunCommandAsync("tesseract", $"\"{imagePath}\" stdout -l {language}", ct);
            return result.Stdout.Trim();
        }
        catch when (string.IsNullOrWhiteSpace(configuredLanguage) && language != "eng")
        {
            var fallback = await RunCommandAsync("tesseract", $"\"{imagePath}\" stdout -l eng", ct);
            return fallback.Stdout.Trim();
        }
    }

    private static async Task<string> RunTranscriptionAsync(string audioPath, CancellationToken ct)
    {
        var model = Environment.GetEnvironmentVariable("MEMORIX_WHISPER_MODEL") ?? "base";
        var tempDir = Path.Combine(Path.GetTempPath(), $"memorix-whisper-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            await RunCommandAsync("whisper",
                $"\"{audioPath}\" --model {model} --output_format txt --output_dir \"{tempDir}\"",
                ct);

            var outputFile = Directory.GetFiles(tempDir, "*.txt").FirstOrDefault();
            if (outputFile == null)
            {
                throw new InvalidOperationException("whisper 未生成转写文本文件。");
            }

            return (await File.ReadAllTextAsync(outputFile, ct)).Trim();
        }
        finally
        {
            try
            {
                Directory.Delete(tempDir, recursive: true);
            }
            catch
            {
                // Best-effort cleanup only.
            }
        }
    }

    private static async Task<(string Stdout, string Stderr)> RunCommandAsync(
        string fileName,
        string arguments,
        CancellationToken ct)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"无法启动 {fileName}。请先安装并确认它在 PATH 中。", ex);
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = process.StandardError.ReadToEndAsync(ct);
        var waitTask = process.WaitForExitAsync(ct);
        var completed = await Task.WhenAny(waitTask, Task.Delay(CommandTimeout, ct));
        if (completed != waitTask)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // Ignore kill failures; the command is already considered failed.
            }
            throw new TimeoutException($"{fileName} 处理超时。");
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"{fileName} 处理失败：{stderr}".Trim());
        }

        return (stdout, stderr);
    }

    private static string EscapeJson(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
