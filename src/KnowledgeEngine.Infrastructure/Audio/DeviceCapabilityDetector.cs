using System.Diagnostics;
using System.Runtime.InteropServices;
using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Application.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KnowledgeEngine.Infrastructure.Audio;

/// <summary>
/// Server-side implementation of <see cref="IDeviceCapabilityDetector"/>.
/// Infers device capabilities from the host environment (CPU, memory, GPU, local toolchain)
/// and applies heuristic rules to recommend an audio processing mode.
/// </summary>
public class DeviceCapabilityDetector : IDeviceCapabilityDetector
{
    /// <summary>Recommended mode for high-end devices capable of local ASR and TTS.</summary>
    public const string ModeBatch = "batch";

    /// <summary>Recommended mode for real-time streaming on capable devices.</summary>
    public const string ModeRealtime = "realtime";

    /// <summary>Recommended mode for low-end devices: record now, process later.</summary>
    public const string ModeOffline = "offline";

    // Heuristic thresholds (aligned with task specification).
    private const int HighEndMinCores = 8;
    private const long HighEndMinMemoryMb = 4096;
    private const int MidRangeMinCores = 4;
    private const long MidRangeMinMemoryMb = 2048;

    private readonly IOptions<AudioSettings> _settings;
    private readonly ILogger<DeviceCapabilityDetector> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="DeviceCapabilityDetector"/> class.
    /// </summary>
    /// <param name="settings">Audio configuration settings.</param>
    /// <param name="logger">The logger for diagnostic output.</param>
    public DeviceCapabilityDetector(
        IOptions<AudioSettings> settings,
        ILogger<DeviceCapabilityDetector> logger)
    {
        _settings = settings;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<DeviceCapabilityResult> DetectAsync(CancellationToken ct)
    {
        var cpuCores = Environment.ProcessorCount;
        var memoryMb = GetAvailableMemoryMb();
        var availableStorageMb = GetAvailableStorageMb();
        var (gpuAvailable, gpuName) = DetectGpu();
        var ffmpegAvailable = IsExecutableOnPath("ffmpeg");
        var whisperAvailable = IsExecutableOnPath("whisper");

        // Local ASR requires whisper on PATH plus sufficient CPU/memory.
        var supportsLocalAsr = whisperAvailable
            && cpuCores >= _settings.Value.MinCoresForLocalAsr
            && memoryMb >= _settings.Value.MinMemoryMbForLocalAsr;

        // Local TTS requires sufficient CPU/memory (no external binary dependency
        // in this heuristic; a TTS engine check can be added later).
        var supportsLocalTts = cpuCores >= _settings.Value.MinCoresForLocalTts
            && memoryMb >= _settings.Value.MinMemoryMbForLocalTts;

        var (mode, reason) = DetermineRecommendation(
            cpuCores, memoryMb, gpuAvailable, supportsLocalAsr, supportsLocalTts);

        var result = new DeviceCapabilityResult
        {
            CpuCores = cpuCores,
            MemoryMb = memoryMb,
            GpuAvailable = gpuAvailable,
            GpuName = gpuName,
            AvailableStorageMb = availableStorageMb,
            ThermalState = "nominal",
            BatteryLevel = null,
            SupportsLocalAsr = supportsLocalAsr,
            SupportsLocalTts = supportsLocalTts,
            RecommendedMode = mode,
            RecommendationReason = reason
        };

        _logger.LogInformation(
            "Server device detection: cores={Cores}, memory={MemoryMb}MB, gpu={Gpu}, " +
            "ffmpeg={Ffmpeg}, whisper={Whisper}, mode={Mode}",
            result.CpuCores, result.MemoryMb, result.GpuAvailable,
            ffmpegAvailable, whisperAvailable, result.RecommendedMode);

        return Task.FromResult(result);
    }

    /// <inheritdoc />
    public Task<DeviceCapabilityResult> ReportAsync(DeviceCapabilityReport report, CancellationToken ct)
    {
        // Determine local support based on reported hardware and configured thresholds.
        var supportsLocalAsr = report.CpuCores >= _settings.Value.MinCoresForLocalAsr
            && report.MemoryMb >= _settings.Value.MinMemoryMbForLocalAsr;

        var supportsLocalTts = report.CpuCores >= _settings.Value.MinCoresForLocalTts
            && report.MemoryMb >= _settings.Value.MinMemoryMbForLocalTts;

        var (mode, reason) = DetermineRecommendation(
            report.CpuCores, report.MemoryMb, report.GpuAvailable,
            supportsLocalAsr, supportsLocalTts);

        var result = new DeviceCapabilityResult
        {
            CpuCores = report.CpuCores,
            MemoryMb = report.MemoryMb,
            GpuAvailable = report.GpuAvailable,
            GpuName = report.GpuName,
            AvailableStorageMb = report.AvailableStorageMb,
            ThermalState = report.ThermalState,
            BatteryLevel = report.BatteryLevel,
            SupportsLocalAsr = supportsLocalAsr,
            SupportsLocalTts = supportsLocalTts,
            RecommendedMode = mode,
            RecommendationReason = reason
        };

        _logger.LogInformation(
            "Client device report: model={Model}, os={Os}, cores={Cores}, memory={MemoryMb}MB, " +
            "gpu={Gpu}, mode={Mode}",
            report.DeviceModel, report.OsVersion, report.CpuCores, report.MemoryMb,
            report.GpuAvailable, result.RecommendedMode);

        return Task.FromResult(result);
    }

    /// <summary>
    /// Applies three-tier heuristics to determine the recommended processing mode:
    /// High-end (8+ cores, 4GB+ free, GPU) → batch with local ASR/TTS.
    /// Mid-range (4+ cores, 2GB+ free) → batch with local ASR only.
    /// Low-end → offline (record only, process when connected).
    /// </summary>
    private (string mode, string reason) DetermineRecommendation(
        int cpuCores, long memoryMb, bool gpuAvailable,
        bool supportsLocalAsr, bool supportsLocalTts)
    {
        // High-end: 8+ cores, 4 GB+ free memory, GPU available.
        if (cpuCores >= HighEndMinCores && memoryMb >= HighEndMinMemoryMb && gpuAvailable)
        {
            return (ModeBatch,
                "High-end device with GPU detected: batch mode with local ASR and TTS.");
        }

        // Mid-range: 4+ cores, 2 GB+ free memory.
        if (cpuCores >= MidRangeMinCores && memoryMb >= MidRangeMinMemoryMb)
        {
            return (ModeBatch,
                "Mid-range device detected: batch mode with local ASR only.");
        }

        // Low-end: insufficient resources for local processing.
        return (ModeOffline,
            "Low-end device detected: offline mode (record only, process when connected).");
    }

    /// <summary>
    /// Gets the approximate available memory in megabytes using the GC memory info API.
    /// </summary>
    private static long GetAvailableMemoryMb()
    {
        var gcInfo = GC.GetGCMemoryInfo();
        return gcInfo.TotalAvailableMemoryBytes / (1024 * 1024);
    }

    /// <summary>
    /// Gets the available free storage in megabytes on the drive containing the temp directory.
    /// </summary>
    private static long GetAvailableStorageMb()
    {
        try
        {
            var tempDir = Path.GetTempPath();
            var drive = new DriveInfo(Path.GetPathRoot(tempDir) ?? tempDir);
            return drive.AvailableFreeSpace / (1024 * 1024);
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>
    /// Attempts to detect a GPU available for compute acceleration.
    /// Checks for nvidia-smi on Linux/Windows or Apple Silicon (Metal) on macOS.
    /// </summary>
    private (bool available, string? name) DetectGpu()
    {
        if (!_settings.Value.GpuDetectionEnabled)
        {
            return (false, null);
        }

        // NVIDIA GPU via nvidia-smi (Linux / Windows).
        if (IsExecutableOnPath("nvidia-smi"))
        {
            return (true, "NVIDIA GPU");
        }

        // Apple Silicon (Metal) on macOS Arm64.
        if (OperatingSystem.IsMacOS() && RuntimeInformation.OSArchitecture == Architecture.Arm64)
        {
            return (true, "Apple Metal");
        }

        return (false, null);
    }

    /// <summary>
    /// Checks whether the given executable is resolvable on the system PATH.
    /// </summary>
    private static bool IsExecutableOnPath(string fileName)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var dir in path.Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(dir))
            {
                continue;
            }

            try
            {
                var fullPath = Path.Combine(dir, fileName);
                if (File.Exists(fullPath))
                {
                    return true;
                }

                // On Windows, also check for .exe extension.
                if (OperatingSystem.IsWindows() && File.Exists(fullPath + ".exe"))
                {
                    return true;
                }
            }
            catch
            {
                // Ignore path traversal errors (e.g. invalid characters).
            }
        }

        return false;
    }
}
