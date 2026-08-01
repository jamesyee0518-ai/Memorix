using System.Diagnostics;
using System.Text;
using KnowledgeEngine.Application.DTOs;
using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace KnowledgeEngine.Application.Services;

/// <summary>
/// Processes media inbox items into text and imports the result into the local knowledge base.
/// Image OCR uses the local "tesseract" CLI. Audio transcription delegates to the audio
/// capability provider system via <see cref="IAudioPolicyRouter"/>, which selects the best
/// ASR provider based on privacy, execution mode, credential, and language constraints.
/// </summary>
public class MediaProcessingService
{
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromMinutes(10);
    private readonly IKnowledgeRepository _repo;
    private readonly IFileStorageFactory _fileStorageFactory;
    private readonly InboxImportService _inboxImportService;
    private readonly IPushNotificationService _pushNotifications;
    private readonly IAudioPolicyRouter _policyRouter;
    private readonly ILogger<MediaProcessingService> _logger;

    public MediaProcessingService(
        IKnowledgeRepository repo,
        IFileStorageFactory fileStorageFactory,
        InboxImportService inboxImportService,
        IPushNotificationService pushNotifications,
        IAudioPolicyRouter policyRouter,
        ILogger<MediaProcessingService> logger)
    {
        _repo = repo;
        _fileStorageFactory = fileStorageFactory;
        _inboxImportService = inboxImportService;
        _pushNotifications = pushNotifications;
        _policyRouter = policyRouter;
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

    /// <summary>
    /// Transcribes an audio file by delegating to the audio capability provider system.
    /// Uses <see cref="IAudioPolicyRouter"/> to resolve the best ASR provider based on
    /// privacy, execution mode, credential, and language constraints, then executes
    /// transcription through the resolved provider.
    /// Falls back gracefully if no providers are available.
    /// </summary>
    private async Task<string> RunTranscriptionAsync(string audioPath, CancellationToken ct)
    {
        var fileInfo = new FileInfo(audioPath);

        var routingContext = new AsrRoutingContext
        {
            DataClassification = DataClassification.INTERNAL,
            PreferredExecutionMode = null,
            PreferredCredentialMode = null,
            PreferredProviderId = null,
            PreferredModelId = null,
            Language = null,
            EnableVad = false,
            EnableSpeakerDiarization = false,
            EnablePunctuation = true,
            EnableHotwords = false,
            EnableWordTimestamp = false,
            FileSizeBytes = fileInfo.Exists ? fileInfo.Length : 0,
            DurationMs = 0,
            MimeType = GuessMimeType(audioPath),
            FallbackPolicy = FallbackPolicies.LocalFallback,
        };

        IAsrProvider provider;
        try
        {
            provider = await _policyRouter.ResolveAsrProviderAsync(routingContext, ct);
        }
        catch (InvalidOperationException ex)
        {
            throw new InvalidOperationException(
                "没有可用的 ASR Provider。请确认已安装 whisper 或启用 FunASR。" +
                $"路由详情: {ex.Message}", ex);
        }

        var descriptor = await provider.GetDescriptorAsync(ct);

        _logger.LogInformation(
            "MediaProcessingService: resolved ASR provider {ProviderId} (model: {ModelId}) for file {FilePath}",
            descriptor.ProviderId, descriptor.ModelId, audioPath);

        var request = new AsrTranscriptionRequest
        {
            AudioFilePath = audioPath,
            MimeType = routingContext.MimeType,
            FileSizeBytes = routingContext.FileSizeBytes,
            DurationMs = 0,
            Language = null,
            EnableVad = false,
            EnableSpeakerDiarization = false,
            EnablePunctuation = true,
            DataClassification = DataClassification.INTERNAL,
            FallbackPolicy = FallbackPolicies.LocalFallback,
        };

        var result = await provider.TranscribeAsync(request, ct);

        // Prefer the FullText field; fall back to concatenating segment text.
        if (!string.IsNullOrWhiteSpace(result.FullText))
        {
            return result.FullText.Trim();
        }

        if (result.Segments is { Count: > 0 })
        {
            var sb = new StringBuilder();
            foreach (var seg in result.Segments)
            {
                if (!string.IsNullOrWhiteSpace(seg.Text))
                {
                    if (sb.Length > 0)
                        sb.Append(' ');
                    sb.Append(seg.Text.Trim());
                }
            }
            return sb.ToString();
        }

        return string.Empty;
    }

    /// <summary>
    /// Guesses a MIME type from the file extension.
    /// </summary>
    private static string GuessMimeType(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        return ext switch
        {
            ".wav" => "audio/wav",
            ".mp3" => "audio/mp3",
            ".m4a" => "audio/m4a",
            ".flac" => "audio/flac",
            ".ogg" => "audio/ogg",
            ".webm" => "audio/webm",
            ".mp4" => "audio/mp4",
            ".aac" => "audio/aac",
            _ => "audio/wav",
        };
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
