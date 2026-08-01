using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Application.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KnowledgeEngine.Infrastructure.Audio;

/// <summary>
/// VAD (Voice Activity Detection) service supporting both FunASR API and FFmpeg silencedetect fallback.
/// </summary>
/// <remarks>
/// <para>
/// When <see cref="AudioSettings.FunAsrEnabled"/> is true, audio is POST-ed to the FunASR VAD
/// endpoint (<c>{FunAsrBaseUrl}/api/vad</c>) and the JSON response is parsed for speech segments.
/// </para>
/// <para>
/// When FunASR is disabled or unavailable, the service falls back to FFmpeg's
/// <c>silencedetect</c> audio filter (<c>noise=-30dB:d=0.5</c>) and parses stderr for
/// <c>silence_start</c>/<c>silence_end</c> markers to derive speech segments.
/// </para>
/// <para>
/// VAD segments serve as the universal time baseline for all downstream audio capabilities
/// (transcription, diarization, punctuation, etc.).
/// </para>
/// </remarks>
public class VadService : IVadService
{
    /// <summary>
    /// Maximum execution time for a single FFmpeg process invocation.
    /// </summary>
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromMinutes(5);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly AudioSettings _settings;
    private readonly ILogger<VadService> _logger;

    // ── FFmpeg stderr parsing regexes (compiled once) ──

    private static readonly Regex SilenceStartRegex =
        new(@"silence_start:\s*([\d.]+)", RegexOptions.Compiled);

    private static readonly Regex SilenceEndRegex =
        new(@"silence_end:\s*([\d.]+)", RegexOptions.Compiled);

    private static readonly Regex DurationRegex =
        new(@"Duration:\s*(\d{2}):(\d{2}):(\d{2}(?:\.\d+)?)", RegexOptions.Compiled);

    /// <summary>
    /// Creates a new <see cref="VadService"/>.
    /// </summary>
    /// <param name="httpClientFactory">HTTP client factory for FunASR API calls.</param>
    /// <param name="options">Audio settings (provides <see cref="AudioSettings.FunAsrEnabled"/> and <see cref="AudioSettings.FunAsrBaseUrl"/>).</param>
    /// <param name="logger">Logger for VAD operations.</param>
    public VadService(
        IHttpClientFactory httpClientFactory,
        IOptions<AudioSettings> options,
        ILogger<VadService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _settings = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<List<VadSegment>> DetectSegmentsAsync(string audioFilePath, CancellationToken ct)
    {
        if (_settings.FunAsrEnabled)
        {
            try
            {
                var segments = await DetectViaFunAsrAsync(audioFilePath, ct);
                _logger.LogInformation(
                    "FunASR VAD detected {Count} speech segments for {Path}",
                    segments.Count, audioFilePath);
                return segments;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "FunASR VAD failed for {Path}, falling back to FFmpeg silencedetect",
                    audioFilePath);
            }
        }

        var ffmpegSegments = await DetectViaFfmpegAsync(audioFilePath, ct);
        _logger.LogInformation(
            "FFmpeg silencedetect VAD detected {Count} speech segments for {Path}",
            ffmpegSegments.Count, audioFilePath);
        return ffmpegSegments;
    }

    /// <inheritdoc />
    public async Task<List<string>> SplitAudioAsync(
        string audioFilePath,
        List<VadSegment> segments,
        string outputDir,
        CancellationToken ct)
    {
        Directory.CreateDirectory(outputDir);

        var paths = new List<string>(segments.Count);

        for (var i = 0; i < segments.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            var seg = segments[i];
            var startSec = (seg.StartMs / 1000.0).ToString("F3", CultureInfo.InvariantCulture);
            var endSec = (seg.EndMs / 1000.0).ToString("F3", CultureInfo.InvariantCulture);
            var outputPath = Path.Combine(outputDir, $"segment_{i}.wav");

            // -ss (start) and -to (end) placed after -i for accurate seeking.
            // -c copy avoids re-encoding (safe for WAV PCM input).
            // -y forces overwrite in case the output already exists.
            await RunProcessAsync(
                "ffmpeg",
                $"-y -i \"{audioFilePath}\" -ss {startSec} -to {endSec} -c copy \"{outputPath}\"",
                ct);

            paths.Add(outputPath);
            _logger.LogDebug(
                "Split segment {Index}/{Total}: [{StartMs}ms - {EndMs}ms] -> {Path}",
                i, segments.Count, seg.StartMs, seg.EndMs, outputPath);
        }

        return paths;
    }

    // ── FunASR VAD ──

    /// <summary>
    /// Calls the FunASR VAD API and parses the JSON response for speech segments.
    /// </summary>
    private async Task<List<VadSegment>> DetectViaFunAsrAsync(string audioFilePath, CancellationToken ct)
    {
        var baseUrl = _settings.FunAsrBaseUrl.TrimEnd('/');
        var url = $"{baseUrl}/api/vad";

        var client = _httpClientFactory.CreateClient();

        using var fileStream = new FileStream(
            audioFilePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81920,
            useAsync: true);

        using var content = new MultipartFormDataContent();
        content.Add(new StreamContent(fileStream), "audio", Path.GetFileName(audioFilePath));

        using var response = await client.PostAsync(url, content, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        return ParseFunAsrResponse(json);
    }

    /// <summary>
    /// Parses the FunASR VAD JSON response into <see cref="VadSegment"/> objects.
    /// Supports multiple response shapes:
    /// <list type="bullet">
    /// <item><c>{ "segments": [{ "start": 0, "end": 2300, "confidence": 0.95 }] }</c></item>
    /// <item><c>{ "segments": [[0, 2300], [2400, 5600]] }</c></item>
    /// <item><c>{ "timestamp": [[0, 2300], [2400, 5600]] }</c> (FunASR native format)</item>
    /// </list>
    /// Timestamp values are interpreted as milliseconds.
    /// </summary>
    private static List<VadSegment> ParseFunAsrResponse(string json)
    {
        var segments = new List<VadSegment>();

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // Try "segments" array (object-based or array-based entries).
        if (root.TryGetProperty("segments", out var segEl) && segEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var seg in segEl.EnumerateArray())
            {
                if (seg.ValueKind == JsonValueKind.Object)
                {
                    var startMs = TryGetMs(seg, "start", "start_ms", "begin");
                    var endMs = TryGetMs(seg, "end", "end_ms", "stop");
                    var confidence = seg.TryGetProperty("confidence", out var conf) && conf.ValueKind == JsonValueKind.Number
                        ? conf.GetDecimal()
                        : 1.0m;

                    if (startMs.HasValue && endMs.HasValue && endMs.Value > startMs.Value)
                    {
                        segments.Add(new VadSegment
                        {
                            StartMs = startMs.Value,
                            EndMs = endMs.Value,
                            Confidence = confidence
                        });
                    }
                }
                else if (seg.ValueKind == JsonValueKind.Array && seg.GetArrayLength() >= 2)
                {
                    var arr = seg.EnumerateArray().ToArray();
                    var startMs = TryGetMsFromElement(arr[0]);
                    var endMs = TryGetMsFromElement(arr[1]);

                    if (startMs.HasValue && endMs.HasValue && endMs.Value > startMs.Value)
                    {
                        segments.Add(new VadSegment
                        {
                            StartMs = startMs.Value,
                            EndMs = endMs.Value,
                            Confidence = 1.0m
                        });
                    }
                }
            }
        }
        // Try "timestamp" array (FunASR native format: [[start, end], ...]).
        else if (root.TryGetProperty("timestamp", out var tsEl) && tsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var ts in tsEl.EnumerateArray())
            {
                if (ts.ValueKind == JsonValueKind.Array && ts.GetArrayLength() >= 2)
                {
                    var arr = ts.EnumerateArray().ToArray();
                    var startMs = TryGetMsFromElement(arr[0]);
                    var endMs = TryGetMsFromElement(arr[1]);

                    if (startMs.HasValue && endMs.HasValue && endMs.Value > startMs.Value)
                    {
                        segments.Add(new VadSegment
                        {
                            StartMs = startMs.Value,
                            EndMs = endMs.Value,
                            Confidence = 1.0m
                        });
                    }
                }
            }
        }

        return segments;
    }

    /// <summary>
    /// Attempts to read a millisecond timestamp from a JSON object using multiple property name candidates.
    /// </summary>
    private static long? TryGetMs(JsonElement obj, params string[] propertyNames)
    {
        foreach (var name in propertyNames)
        {
            if (obj.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.Number)
            {
                return TryGetMsFromElement(prop);
            }
        }

        return null;
    }

    /// <summary>
    /// Converts a JSON number element to milliseconds.
    /// FunASR native timestamps are integers in milliseconds.
    /// Values with fractional parts within a plausible seconds range (&lt; 3600)
    /// are treated as seconds and converted to milliseconds.
    /// </summary>
    private static long? TryGetMsFromElement(JsonElement el)
    {
        if (el.ValueKind != JsonValueKind.Number)
        {
            return null;
        }

        var value = el.GetDouble();

        // Heuristic: fractional values below 3600 (1 hour in seconds) are treated as seconds.
        if (value > 0 && value < 3600 && value != Math.Floor(value))
        {
            return (long)(value * 1000);
        }

        return (long)value;
    }

    // ── FFmpeg silencedetect fallback ──

    /// <summary>
    /// Runs FFmpeg with the silencedetect filter and parses stderr to derive speech segments.
    /// </summary>
    private async Task<List<VadSegment>> DetectViaFfmpegAsync(string audioFilePath, CancellationToken ct)
    {
        var (_, stderr) = await RunProcessAsync(
            "ffmpeg",
            $"-i \"{audioFilePath}\" -af silencedetect=noise=-30dB:d=0.5 -f null -",
            ct);

        var durationSec = ParseDurationFromStderr(stderr);
        var silenceStarts = ParseDoubleValues(stderr, SilenceStartRegex);
        var silenceEnds = ParseDoubleValues(stderr, SilenceEndRegex);

        return BuildSpeechSegments(silenceStarts, silenceEnds, durationSec);
    }

    /// <summary>
    /// Converts silence periods into speech segments.
    /// Speech is the complement of silence: [0, first_silence_start],
    /// [silence_end_i, silence_start_{i+1}], ..., [last_silence_end, total_duration].
    /// </summary>
    private static List<VadSegment> BuildSpeechSegments(
        List<double> silenceStarts,
        List<double> silenceEnds,
        double? durationSec)
    {
        var segments = new List<VadSegment>();

        // Pair silence starts and ends into silence intervals.
        var silences = new List<(double Start, double End)>();
        for (var i = 0; i < silenceStarts.Count; i++)
        {
            var start = silenceStarts[i];
            var end = i < silenceEnds.Count
                ? silenceEnds[i]
                : (durationSec ?? start);
            silences.Add((start, end));
        }

        // No silence detected: the entire audio is one speech segment.
        if (silences.Count == 0)
        {
            if (durationSec is > 0)
            {
                segments.Add(new VadSegment
                {
                    StartMs = 0,
                    EndMs = (long)(durationSec.Value * 1000),
                    Confidence = 1.0m
                });
            }

            return segments;
        }

        // First speech segment: [0, first_silence_start].
        if (silences[0].Start > 0)
        {
            segments.Add(new VadSegment
            {
                StartMs = 0,
                EndMs = (long)(silences[0].Start * 1000),
                Confidence = 1.0m
            });
        }

        // Middle speech segments: between each silence end and the next silence start.
        for (var i = 0; i < silences.Count - 1; i++)
        {
            var speechStart = silences[i].End;
            var speechEnd = silences[i + 1].Start;
            if (speechEnd > speechStart)
            {
                segments.Add(new VadSegment
                {
                    StartMs = (long)(speechStart * 1000),
                    EndMs = (long)(speechEnd * 1000),
                    Confidence = 1.0m
                });
            }
        }

        // Last speech segment: [last_silence_end, total_duration].
        if (durationSec is not null && durationSec.Value > silences[^1].End)
        {
            segments.Add(new VadSegment
            {
                StartMs = (long)(silences[^1].End * 1000),
                EndMs = (long)(durationSec.Value * 1000),
                Confidence = 1.0m
            });
        }

        return segments;
    }

    // ── FFmpeg stderr parsing helpers ──

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

    /// <summary>
    /// Extracts all numeric values matching the given regex from the text.
    /// </summary>
    private static List<double> ParseDoubleValues(string text, Regex regex)
    {
        var values = new List<double>();
        foreach (Match match in regex.Matches(text))
        {
            if (double.TryParse(match.Groups[1].Value, NumberStyles.Float,
                    CultureInfo.InvariantCulture, out var val))
            {
                values.Add(val);
            }
        }

        return values;
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
}
