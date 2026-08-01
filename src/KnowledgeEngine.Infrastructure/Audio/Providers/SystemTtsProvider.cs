using System.Diagnostics;
using System.Runtime.InteropServices;
using KnowledgeEngine.Application.DTOs;
using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace KnowledgeEngine.Infrastructure.Audio.Providers;

/// <summary>
/// System TTS fallback provider that uses the OS built-in TTS engine.
/// On macOS it shells out to the <c>say</c> command; on Linux it uses <c>espeak</c>.
/// This is the lowest-quality provider in the degradation chain and requires
/// no credentials and no network access — audio never leaves the device.
/// </summary>
public class SystemTtsProvider : ITtsProvider
{
    private const string ProviderIdValue = "system_tts";
    private const string ModelIdValue = "os-default";
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromMinutes(2);

    private readonly ILogger<SystemTtsProvider> _logger;

    /// <summary>
    /// Creates a new <see cref="SystemTtsProvider"/>.
    /// </summary>
    /// <param name="logger">Logger for diagnostics.</param>
    public SystemTtsProvider(ILogger<SystemTtsProvider> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    public Task<TtsProviderDescriptor> GetDescriptorAsync(CancellationToken ct)
    {
        var descriptor = new TtsProviderDescriptor
        {
            ProviderId = ProviderIdValue,
            ModelId = ModelIdValue,
            ExecutionModes = [ExecutionMode.LOCAL_DEVICE],
            CredentialModes = [CredentialMode.NO_CREDENTIAL],
            SupportedLanguages = ["zh", "en"],
            SupportsStreaming = false,
            SupportsBatch = true,
            SupportsVoiceCloning = false,
            SupportsSpeedControl = true,
            SupportsPitchControl = false,
            OutputFormats = ["wav", "aiff"],
            SupportedSampleRates = [22050],
            SendsAudioOffDevice = false,
            StoresProviderData = ProviderDataRetention.NO,
            PricingUnit = null
        };

        return Task.FromResult(descriptor);
    }

    /// <inheritdoc/>
    public Task<ValidationResult> ValidateRequestAsync(TtsRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Text))
        {
            return Task.FromResult(ValidationResult.Fail("Text is required for TTS synthesis."));
        }

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX) &&
            !RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return Task.FromResult(ValidationResult.Fail(
                "SystemTtsProvider is only supported on macOS and Linux."));
        }

        return Task.FromResult(ValidationResult.Ok());
    }

    /// <inheritdoc/>
    public async Task<TtsResult> SynthesizeAsync(TtsRequest request, CancellationToken ct)
    {
        var outputPath = Path.Combine(Path.GetTempPath(), $"memorix-tts-{Guid.NewGuid():N}.wav");
        var speed = request.Speed > 0 ? (double)request.Speed : 1.0;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            // macOS `say` command: -o outputs to file, --data-format sets WAV format.
            var wordsPerMinute = (int)(175.0 * speed);
            await RunProcessAsync(
                "say",
                $"-o \"{outputPath}\" --data-format=LEF32@{request.SampleRate} -r {wordsPerMinute} \"{request.Text}\"",
                ct);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            // Linux `espeak` command: -w outputs WAV file, -s sets words-per-minute.
            var wordsPerMinute = (int)(170.0 * speed);
            await RunProcessAsync(
                "espeak",
                $"\"{request.Text}\" -w \"{outputPath}\" -s {wordsPerMinute}",
                ct);
        }
        else
        {
            throw new PlatformNotSupportedException(
                "SystemTtsProvider is only supported on macOS and Linux.");
        }

        var fileInfo = new FileInfo(outputPath);
        if (!fileInfo.Exists || fileInfo.Length == 0)
        {
            throw new InvalidOperationException(
                "System TTS command did not produce a valid output file.");
        }

        _logger.LogInformation(
            "System TTS synthesis completed: {FilePath} ({SizeBytes} bytes)",
            outputPath, fileInfo.Length);

        return new TtsResult
        {
            ProviderId = ProviderIdValue,
            ModelId = ModelIdValue,
            OutputFilePath = outputPath,
            OutputFormat = "wav",
            FileSizeBytes = fileInfo.Length,
            VoiceId = request.VoiceId,
            Metadata = new Dictionary<string, object>
            {
                ["engine"] = RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "say" : "espeak"
            }
        };
    }

    /// <inheritdoc/>
    public IAsyncEnumerable<AudioChunk>? SynthesizeStream(TtsStreamRequest request, CancellationToken ct)
    {
        // System TTS does not support streaming — it produces a complete file only.
        return null;
    }

    /// <inheritdoc/>
    public Task<List<VoiceProfile>> ListVoicesAsync(CancellationToken ct)
    {
        var voices = new List<VoiceProfile>();

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            voices.Add(new VoiceProfile
            {
                VoiceId = "os-default",
                Name = "macOS System Voice",
                Language = "en",
                Gender = "unknown"
            });
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            voices.Add(new VoiceProfile
            {
                VoiceId = "os-default",
                Name = "eSpeak Default Voice",
                Language = "en",
                Gender = "unknown"
            });
        }

        return Task.FromResult(voices);
    }

    /// <inheritdoc/>
    public Task<CostEstimate>? EstimateCostAsync(TtsRequest request, CancellationToken ct)
    {
        // Local execution — no monetary cost.
        return null;
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
            string command;
            string args;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                command = "say";
                args = "-v";
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                command = "espeak";
                args = "--version";
            }
            else
            {
                sw.Stop();
                health.IsHealthy = false;
                health.LatencyMs = sw.ElapsedMilliseconds;
                health.StatusMessage = "System TTS is not supported on this platform.";
                return health;
            }

            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = command,
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            process.Start();
            await process.WaitForExitAsync(ct);
            sw.Stop();

            // The process starting successfully means the binary is on PATH.
            // Exit code may be non-zero for some argument forms (e.g. `say -v` on macOS),
            // but the key indicator is that the process did not throw on start.
            health.IsHealthy = true;
            health.LatencyMs = sw.ElapsedMilliseconds;
            health.StatusMessage = $"{command} is available on PATH";
        }
        catch (Exception ex)
        {
            sw.Stop();
            health.IsHealthy = false;
            health.LatencyMs = sw.ElapsedMilliseconds;
            var cmd = RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "say" : "espeak";
            health.StatusMessage = $"{cmd} not found on PATH: {ex.Message}";
        }

        return health;
    }

    // ── Private helpers ──

    /// <summary>
    /// Runs a CLI process with a timeout, matching the pattern from
    /// <see cref="WhisperCppAsrProvider"/>.
    /// </summary>
    private async Task RunProcessAsync(string fileName, string arguments, CancellationToken ct)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Cannot start {fileName}. Ensure it is installed and on PATH.", ex);
        }

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
            throw new TimeoutException(
                $"{fileName} timed out after {CommandTimeout.TotalMinutes} minutes.");
        }

        var stderr = await stderrTask;
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"{fileName} failed: {stderr}".Trim());
        }
    }
}
