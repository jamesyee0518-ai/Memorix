using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Application.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KnowledgeEngine.Infrastructure.Audio;

/// <summary>
/// Media preparation service orchestrating the full pre-ASR pipeline:
/// file hashing → cache lookup → FFmpeg normalization → audio cache → VAD → physical segments.
/// </summary>
/// <remarks>
/// <para>
/// The pipeline avoids redundant work by caching normalized audio keyed on the source file's
/// SHA-256 hash combined with the target sample rate, channel count, and normalization version.
/// When a cache hit occurs, FFmpeg transcoding is skipped entirely.
/// </para>
/// <para>
/// When VAD is enabled (<see cref="AudioSettings.VadEnabled"/>), the normalized audio is
/// segmented into speech chunks, each written as a separate WAV file for independent ASR processing.
/// </para>
/// </remarks>
public class MediaPreparationService : IMediaPreparationService
{
    /// <summary>
    /// Normalization version identifier included in the cache key.
    /// Increment when the normalization logic changes to invalidate stale cache entries.
    /// </summary>
    private const int NormalizeVersion = 1;

    /// <summary>
    /// Maximum execution time for a single FFmpeg process invocation.
    /// </summary>
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Buffer size (80 KB) used for async file read operations.
    /// </summary>
    private const int ReadBufferSize = 81920;

    private static readonly Regex DurationRegex =
        new(@"Duration:\s*(\d{2}):(\d{2}):(\d{2}(?:\.\d+)?)", RegexOptions.Compiled);

    private readonly IAudioCacheService _cacheService;
    private readonly IVadService _vadService;
    private readonly AudioSettings _settings;
    private readonly ILogger<MediaPreparationService> _logger;

    /// <summary>
    /// Creates a new <see cref="MediaPreparationService"/>.
    /// </summary>
    /// <param name="cacheService">Audio cache for normalized file deduplication.</param>
    /// <param name="vadService">VAD service for speech segment detection and splitting.</param>
    /// <param name="options">Audio settings (sample rate, channels, VAD toggle).</param>
    /// <param name="logger">Logger for preparation pipeline events.</param>
    public MediaPreparationService(
        IAudioCacheService cacheService,
        IVadService vadService,
        IOptions<AudioSettings> options,
        ILogger<MediaPreparationService> logger)
    {
        _cacheService = cacheService;
        _vadService = vadService;
        _settings = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<MediaPreparationResult> PrepareAsync(
        string audioFilePath,
        string mimeType,
        CancellationToken ct)
    {
        _logger.LogInformation(
            "Preparing audio file {Path} (MIME: {MimeType})",
            audioFilePath, mimeType);

        // 1. Compute SHA-256 hash of the input file.
        var sha256 = await ComputeSha256Async(audioFilePath, ct);
        _logger.LogDebug("Computed SHA-256: {Sha256} for {Path}", sha256, audioFilePath);

        // 2. Compute cache key from hash + normalization parameters.
        var cacheKey = _cacheService.ComputeCacheKey(
            sha256,
            _settings.NormalizeSampleRate,
            _settings.NormalizeChannels,
            NormalizeVersion);

        // 3. Check the audio cache — skip normalization on hit.
        var cachedPath = await _cacheService.GetAsync(cacheKey, ct);
        string normalizedPath;
        string? normalizeStderr = null;

        if (cachedPath != null)
        {
            _logger.LogInformation(
                "Cache hit for {CacheKey}, skipping FFmpeg normalization", cacheKey);
            normalizedPath = cachedPath;
        }
        else
        {
            // 4. Run FFmpeg normalization to a temp file.
            var tempPath = Path.Combine(
                Path.GetTempPath(),
                $"memorix-norm-{Guid.NewGuid():N}.wav");

            var (_, stderr) = await RunProcessAsync(
                "ffmpeg",
                $"-y -i \"{audioFilePath}\" " +
                $"-ar {_settings.NormalizeSampleRate} " +
                $"-ac {_settings.NormalizeChannels} " +
                $"-c:a pcm_s16le \"{tempPath}\"",
                ct);

            normalizeStderr = stderr;

            // 5. Store the normalized file in cache.
            normalizedPath = await _cacheService.PutAsync(cacheKey, tempPath, ct);

            // Clean up the temp file (cache now holds a copy).
            TryDeleteFile(tempPath);

            _logger.LogInformation(
                "Normalized audio cached: {CacheKey} -> {Path}", cacheKey, normalizedPath);
        }

        // 6. Get the duration of the normalized audio.
        var durationMs = await GetDurationMsAsync(normalizedPath, normalizeStderr, ct);

        // 7. Run VAD if enabled.
        var segments = new List<VadSegment>();
        var segmentFilePaths = new List<string>();

        if (_settings.VadEnabled)
        {
            segments = await _vadService.DetectSegmentsAsync(normalizedPath, ct);

            // 8. Split audio into physical segment files if segments were found.
            if (segments.Count > 0)
            {
                var segmentDir = Path.Combine(
                    Path.GetTempPath(),
                    $"memorix-vad-{Guid.NewGuid():N}");

                segmentFilePaths = await _vadService.SplitAudioAsync(
                    normalizedPath, segments, segmentDir, ct);

                _logger.LogInformation(
                    "VAD produced {SegmentCount} segments in {Dir}",
                    segments.Count, segmentDir);
            }
            else
            {
                _logger.LogInformation("VAD enabled but no speech segments detected");
            }
        }

        // 9. Build and return the result.
        var result = new MediaPreparationResult
        {
            NormalizedFilePath = normalizedPath,
            SourceSha256 = sha256,
            CacheKey = cacheKey,
            DurationMs = durationMs,
            SampleRate = _settings.NormalizeSampleRate,
            Channels = _settings.NormalizeChannels,
            VadSegments = segments,
            SegmentFilePaths = segmentFilePaths
        };

        _logger.LogInformation(
            "Media preparation complete: duration={DurationMs}ms, segments={SegmentCount}, " +
            "sampleRate={SampleRate}, channels={Channels}",
            result.DurationMs, result.VadSegments.Count,
            result.SampleRate, result.Channels);

        return result;
    }

    /// <inheritdoc />
    public async Task<string> ComputeSha256Async(string filePath, CancellationToken ct)
    {
        using var sha256 = SHA256.Create();
        await using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            ReadBufferSize,
            useAsync: true);

        var hash = await sha256.ComputeHashAsync(stream, ct);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    // ── Duration detection ──

    /// <summary>
    /// Gets the audio duration in milliseconds using ffprobe.
    /// Falls back to parsing FFmpeg stderr from the normalization step if available.
    /// </summary>
    private async Task<long> GetDurationMsAsync(
        string filePath,
        string? ffmpegStderr,
        CancellationToken ct)
    {
        // Primary: use ffprobe.
        try
        {
            var (stdout, _) = await RunProcessAsync(
                "ffprobe",
                $"-v quiet -show_entries format=duration -of csv=p=0 \"{filePath}\"",
                ct);

            var durationStr = stdout.Trim();
            if (double.TryParse(durationStr, NumberStyles.Float,
                    CultureInfo.InvariantCulture, out var durationSec) && durationSec > 0)
            {
                return (long)(durationSec * 1000);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ffprobe duration query failed for {Path}", filePath);
        }

        // Fallback: parse Duration: from FFmpeg stderr captured during normalization.
        if (ffmpegStderr != null)
        {
            var durationSec = ParseDurationFromStderr(ffmpegStderr);
            if (durationSec is > 0)
            {
                _logger.LogDebug("Parsed duration from FFmpeg stderr: {DurationSec}s", durationSec);
                return (long)(durationSec.Value * 1000);
            }
        }

        _logger.LogWarning("Could not determine audio duration for {Path}", filePath);
        return 0;
    }

    /// <summary>
    /// Parses the total duration from FFmpeg stderr (e.g. "Duration: 00:01:23.45").
    /// </summary>
    private static double? ParseDurationFromStderr(string stderr)
    {
        var match = DurationRegex.Match(stderr);
        if (!match.Success)
        {
            return null;
        }

        var hours = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        var minutes = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
        var seconds = double.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);
        return hours * 3600 + minutes * 60 + seconds;
    }

    // ── Process execution ──

    /// <summary>
    /// Runs an external process with stdout/stderr capture and a timeout.
    /// Follows the same pattern as <c>MediaProcessingService.RunCommandAsync</c>.
    /// </summary>
    private static async Task<(string Stdout, string Stderr)> RunProcessAsync(
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
                $"Failed to start {fileName}. Ensure it is installed and in PATH.", ex);
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

            throw new TimeoutException(
                $"{fileName} timed out after {CommandTimeout.TotalMinutes} minutes.");
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"{fileName} failed with exit code {process.ExitCode}: {stderr}".Trim());
        }

        return (stdout, stderr);
    }

    /// <summary>
    /// Best-effort file deletion that swallows exceptions (temp cleanup only).
    /// </summary>
    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best-effort cleanup only.
        }
    }
}
