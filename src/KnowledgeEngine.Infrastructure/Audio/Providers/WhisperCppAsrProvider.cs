using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using KnowledgeEngine.Application.DTOs;
using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Domain.Enums;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace KnowledgeEngine.Infrastructure.Audio.Providers;

/// <summary>
/// Local whisper.cpp / OpenAI whisper CLI ASR provider.
/// Executes the <c>whisper</c> command-line tool with JSON output and parses
/// segment-level (and optionally word-level) timestamps into structured DTOs.
/// All processing happens on the local device; no audio leaves the machine.
/// </summary>
public class WhisperCppAsrProvider : IAsrProvider
{
    private const string ProviderIdValue = "whisper_cpp";
    private const string WhisperCommand = "whisper";
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromMinutes(10);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    private readonly IConfiguration _configuration;
    private readonly ILogger<WhisperCppAsrProvider> _logger;
    private readonly string _modelId;

    /// <summary>
    /// Creates a new <see cref="WhisperCppAsrProvider"/>.
    /// </summary>
    /// <param name="configuration">Application configuration for reading Audio settings.</param>
    /// <param name="logger">Logger for diagnostics.</param>
    public WhisperCppAsrProvider(IConfiguration configuration, ILogger<WhisperCppAsrProvider> logger)
    {
        _configuration = configuration;
        _logger = logger;

        // Model resolution order: configuration Audio:WhisperModel → env var → default "base".
        _modelId = _configuration["Audio:WhisperModel"]
                   ?? Environment.GetEnvironmentVariable("MEMORIX_WHISPER_MODEL")
                   ?? "base";
    }

    /// <inheritdoc/>
    public Task<AsrProviderDescriptor> GetDescriptorAsync(CancellationToken ct)
    {
        var descriptor = new AsrProviderDescriptor
        {
            ProviderId = ProviderIdValue,
            ModelId = _modelId,
            ExecutionModes = [ExecutionMode.LOCAL_DEVICE],
            CredentialModes = [CredentialMode.NO_CREDENTIAL],
            SupportedLanguages = [], // whisper auto-detects; empty = all languages
            SupportsStreaming = false,
            SupportsBatch = true,
            SupportsVad = true,
            SupportsPunctuation = false,
            SupportsDiarization = false,
            SupportsHotwords = false,
            SupportsWordTimestamp = true,
            SupportsSegmentTimestamp = true,
            SendsAudioOffDevice = false,
            StoresProviderData = ProviderDataRetention.NO,
            AcceptedMimeTypes =
            [
                "audio/wav", "audio/mp3", "audio/m4a",
                "audio/flac", "audio/ogg", "audio/webm"
            ]
        };

        return Task.FromResult(descriptor);
    }

    /// <inheritdoc/>
    public Task<ValidationResult> ValidateRequestAsync(AsrTranscriptionRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.AudioFilePath))
        {
            return Task.FromResult(ValidationResult.Fail("AudioFilePath is required."));
        }

        if (!File.Exists(request.AudioFilePath))
        {
            return Task.FromResult(ValidationResult.Fail($"Audio file not found: {request.AudioFilePath}"));
        }

        return Task.FromResult(ValidationResult.Ok());
    }

    /// <inheritdoc/>
    public async Task<AsrTranscriptionResult> TranscribeAsync(AsrTranscriptionRequest request, CancellationToken ct)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"memorix-whisper-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            // Build whisper CLI arguments.
            var argsBuilder = new StringBuilder();
            argsBuilder.Append($"\"{request.AudioFilePath}\"");
            argsBuilder.Append($" --model {_modelId}");
            argsBuilder.Append(" --output_format json");
            argsBuilder.Append($" --output_dir \"{tempDir}\"");

            if (!string.IsNullOrWhiteSpace(request.Language))
            {
                argsBuilder.Append($" --language {request.Language}");
            }

            if (request.EnableWordTimestamp)
            {
                argsBuilder.Append(" --word_timestamps True");
            }

            _logger.LogInformation(
                "Running whisper CLI with model={Model} for file={FilePath}",
                _modelId, request.AudioFilePath);

            await RunWhisperProcessAsync(argsBuilder.ToString(), ct);

            // Locate the generated JSON file.
            var jsonFile = Directory.GetFiles(tempDir, "*.json").FirstOrDefault();
            if (jsonFile == null)
            {
                throw new InvalidOperationException("whisper did not generate a JSON output file.");
            }

            var jsonContent = await File.ReadAllTextAsync(jsonFile, ct);
            var whisperResult = JsonSerializer.Deserialize<WhisperJsonResult>(jsonContent, JsonOptions);

            if (whisperResult?.Segments == null || whisperResult.Segments.Count == 0)
            {
                _logger.LogWarning("whisper JSON output contained no segments");
                return new AsrTranscriptionResult
                {
                    ProviderId = ProviderIdValue,
                    ModelId = _modelId,
                    Language = whisperResult?.Language ?? request.Language,
                    DurationMs = request.DurationMs,
                    FullText = whisperResult?.Text?.Trim() ?? string.Empty,
                    Segments = []
                };
            }

            // Map whisper segments to AsrSegmentDto with generated UUIDs.
            var segments = new List<AsrSegmentDto>();
            var fullTextBuilder = new StringBuilder();
            var prefix = request.SegmentUuidPrefix;

            for (var i = 0; i < whisperResult.Segments.Count; i++)
            {
                var seg = whisperResult.Segments[i];
                var startMs = (long)(seg.Start * 1000);
                var endMs = (long)(seg.End * 1000);
                var text = seg.Text?.Trim() ?? string.Empty;

                if (fullTextBuilder.Length > 0)
                {
                    fullTextBuilder.Append(' ');
                }
                fullTextBuilder.Append(text);

                var segment = new AsrSegmentDto
                {
                    SegmentUuid = GenerateSegmentUuid(prefix),
                    StartMs = startMs,
                    EndMs = endMs,
                    Text = text,
                    Confidence = 0,
                    SegmentIndex = i
                };

                // Map word-level timestamps if available.
                if (request.EnableWordTimestamp && seg.Words is { Count: > 0 })
                {
                    segment.Words = seg.Words.Select(w => new AsrWordDto
                    {
                        StartMs = (long)(w.Start * 1000),
                        EndMs = (long)(w.End * 1000),
                        Text = w.Word?.Trim() ?? string.Empty,
                        Confidence = w.Probability
                    }).ToList();
                }

                segments.Add(segment);
            }

            _logger.LogInformation(
                "whisper transcription completed: {SegmentCount} segments, {TextLength} chars",
                segments.Count, fullTextBuilder.Length);

            return new AsrTranscriptionResult
            {
                ProviderId = ProviderIdValue,
                ModelId = _modelId,
                Language = whisperResult.Language ?? request.Language,
                DurationMs = request.DurationMs,
                Segments = segments,
                FullText = !string.IsNullOrWhiteSpace(whisperResult.Text)
                    ? whisperResult.Text.Trim()
                    : fullTextBuilder.ToString().Trim(),
                Metadata = new Dictionary<string, object>
                {
                    ["engine"] = "whisper_cli",
                    ["model"] = _modelId
                }
            };
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

    /// <inheritdoc/>
    public IAsyncEnumerable<AsrPartialResult>? TranscribeStream(AsrStreamingRequest request, CancellationToken ct)
    {
        // Whisper CLI does not support streaming transcription.
        return null;
    }

    /// <inheritdoc/>
    public Task<CostEstimate>? EstimateCostAsync(AsrTranscriptionRequest request, CancellationToken ct)
    {
        // Local execution — no monetary cost.
        return null;
    }

    /// <inheritdoc/>
    public Task CancelAsync(string providerTaskId, CancellationToken ct)
    {
        // The CLI process is synchronous and short-lived; cancellation is handled
        // via the CancellationToken passed to WaitForExitAsync.
        _logger.LogDebug("CancelAsync called for providerTaskId={ProviderTaskId} (no-op for CLI)", providerTaskId);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async Task<ProviderHealth> HealthCheckAsync(CancellationToken ct)
    {
        var health = new ProviderHealth
        {
            ProviderId = ProviderIdValue,
            CheckedAt = DateTime.UtcNow
        };

        var sw = Stopwatch.StartNew();
        try
        {
            // Check if whisper is available on PATH by invoking --help.
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = WhisperCommand,
                Arguments = "--help",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            process.Start();
            await process.WaitForExitAsync(ct);
            sw.Stop();

            health.IsHealthy = process.ExitCode == 0 || process.ExitCode == 1;
            health.LatencyMs = sw.ElapsedMilliseconds;
            health.StatusMessage = health.IsHealthy
                ? "whisper CLI is available on PATH"
                : $"whisper --help exited with code {process.ExitCode}";
        }
        catch (Exception ex)
        {
            sw.Stop();
            health.IsHealthy = false;
            health.LatencyMs = sw.ElapsedMilliseconds;
            health.StatusMessage = $"whisper CLI not found on PATH: {ex.Message}";
        }

        return health;
    }

    // ── Private helpers ──

    /// <summary>
    /// Runs the whisper CLI process with a 10-minute timeout, matching the
    /// pattern from <see cref="MediaProcessingService"/>.
    /// </summary>
    private async Task RunWhisperProcessAsync(string arguments, CancellationToken ct)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = WhisperCommand,
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
                $"Cannot start {WhisperCommand}. Ensure it is installed and on PATH.", ex);
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = process.StandardError.ReadToEndAsync(ct);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(CommandTimeout);

        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // Ignore kill failures.
            }
            throw new TimeoutException($"{WhisperCommand} processing timed out after {CommandTimeout.TotalMinutes} minutes.");
        }

        var stderr = await stderrTask;
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"{WhisperCommand} failed: {stderr}".Trim());
        }
    }

    /// <summary>
    /// Generates a stable segment UUID with an optional prefix.
    /// Format: <c>{prefix}_{guid}</c> or just <c>{guid}</c> when no prefix is provided.
    /// </summary>
    private static string GenerateSegmentUuid(string? prefix)
    {
        var guid = Guid.NewGuid().ToString("N");
        return string.IsNullOrWhiteSpace(prefix) ? guid : $"{prefix}_{guid}";
    }

    // ── Whisper JSON output DTOs ──

    private sealed class WhisperJsonResult
    {
        [JsonPropertyName("text")]
        public string? Text { get; set; }

        [JsonPropertyName("segments")]
        public List<WhisperSegment>? Segments { get; set; }

        [JsonPropertyName("language")]
        public string? Language { get; set; }
    }

    private sealed class WhisperSegment
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("start")]
        public double Start { get; set; }

        [JsonPropertyName("end")]
        public double End { get; set; }

        [JsonPropertyName("text")]
        public string? Text { get; set; }

        [JsonPropertyName("words")]
        public List<WhisperWord>? Words { get; set; }
    }

    private sealed class WhisperWord
    {
        [JsonPropertyName("word")]
        public string? Word { get; set; }

        [JsonPropertyName("start")]
        public double Start { get; set; }

        [JsonPropertyName("end")]
        public double End { get; set; }

        [JsonPropertyName("probability")]
        public decimal Probability { get; set; }
    }
}
