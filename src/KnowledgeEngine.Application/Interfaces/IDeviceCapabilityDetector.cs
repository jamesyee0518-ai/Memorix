namespace KnowledgeEngine.Application.Interfaces;

/// <summary>
/// Result of device capability detection or client-reported capability analysis.
/// Contains hardware specs and the server's recommendation for audio processing mode.
/// </summary>
public class DeviceCapabilityResult
{
    /// <summary>Number of logical CPU cores available.</summary>
    public int CpuCores { get; set; }

    /// <summary>Available memory in megabytes.</summary>
    public long MemoryMb { get; set; }

    /// <summary>Whether a GPU is available for compute acceleration.</summary>
    public bool GpuAvailable { get; set; }

    /// <summary>Name of the detected GPU, if any.</summary>
    public string? GpuName { get; set; }

    /// <summary>Available storage in megabytes.</summary>
    public long AvailableStorageMb { get; set; }

    /// <summary>Thermal state of the device (nominal, fair, serious, critical).</summary>
    public string ThermalState { get; set; } = "nominal";

    /// <summary>Battery level as a percentage (0-100), null if on AC power.</summary>
    public decimal? BatteryLevel { get; set; }

    /// <summary>Whether the device can run local ASR (speech-to-text).</summary>
    public bool SupportsLocalAsr { get; set; }

    /// <summary>Whether the device can run local TTS (text-to-speech).</summary>
    public bool SupportsLocalTts { get; set; }

    /// <summary>
    /// Recommended processing mode: "batch", "realtime", or "offline".
    /// </summary>
    public string RecommendedMode { get; set; } = "batch";

    /// <summary>Human-readable explanation for the recommended mode.</summary>
    public string RecommendationReason { get; set; } = string.Empty;
}

/// <summary>
/// Client-reported device capability report sent from desktop or mobile apps.
/// The server uses this to determine the recommended processing mode.
/// </summary>
public class DeviceCapabilityReport
{
    /// <summary>Number of logical CPU cores on the device.</summary>
    public int CpuCores { get; set; }

    /// <summary>Available memory in megabytes.</summary>
    public long MemoryMb { get; set; }

    /// <summary>Whether a GPU is available for compute acceleration.</summary>
    public bool GpuAvailable { get; set; }

    /// <summary>Name of the detected GPU, if any.</summary>
    public string? GpuName { get; set; }

    /// <summary>Available storage in megabytes.</summary>
    public long AvailableStorageMb { get; set; }

    /// <summary>Thermal state of the device (nominal, fair, serious, critical).</summary>
    public string ThermalState { get; set; } = "nominal";

    /// <summary>Battery level as a percentage (0-100), null if on AC power.</summary>
    public decimal? BatteryLevel { get; set; }

    /// <summary>Device model identifier (e.g. "MacBookPro18,3", "Pixel 8").</summary>
    public string? DeviceModel { get; set; }

    /// <summary>Operating system version (e.g. "macOS 14.2", "Android 14").</summary>
    public string? OsVersion { get; set; }

    /// <summary>Client application version.</summary>
    public string? AppVersion { get; set; }
}

/// <summary>
/// Detects device capabilities and recommends an audio processing mode
/// (batch, realtime, or offline) based on available hardware resources.
/// </summary>
public interface IDeviceCapabilityDetector
{
    /// <summary>
    /// Detects server-side device capabilities by inferring from the host environment.
    /// Checks CPU count, available memory, GPU availability, and local toolchain (whisper, ffmpeg).
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An inferred <see cref="DeviceCapabilityResult"/> with a recommended processing mode.</returns>
    Task<DeviceCapabilityResult> DetectAsync(CancellationToken ct);

    /// <summary>
    /// Processes a client-reported device capability report and determines the recommended
    /// processing mode based on the reported hardware specifications.
    /// </summary>
    /// <param name="report">The client-reported device capability data.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A <see cref="DeviceCapabilityResult"/> with server-determined recommendations.</returns>
    Task<DeviceCapabilityResult> ReportAsync(DeviceCapabilityReport report, CancellationToken ct);
}
