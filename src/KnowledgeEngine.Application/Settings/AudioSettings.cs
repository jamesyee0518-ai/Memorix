namespace KnowledgeEngine.Application.Settings;

/// <summary>
/// Configuration settings for the audio capability subsystem.
/// Controls FunASR integration, VAD behavior, FFmpeg normalization, and audio caching.
/// Bind from the "Audio" configuration section.
/// </summary>
public class AudioSettings
{
    /// <summary>
    /// Base URL of the FunASR server (e.g. http://localhost:10095).
    /// Used for VAD and ASR when <see cref="FunAsrEnabled"/> is true.
    /// </summary>
    public string FunAsrBaseUrl { get; set; } = "http://localhost:10095";

    /// <summary>
    /// Whether FunASR is enabled for local VAD and ASR processing.
    /// When false, the system falls back to FFmpeg silencedetect for VAD.
    /// </summary>
    public bool FunAsrEnabled { get; set; } = false;

    /// <summary>
    /// Whisper model name used by the local whisper CLI fallback (e.g. "base", "small", "medium").
    /// </summary>
    public string WhisperModel { get; set; } = "base";

    /// <summary>
    /// Directory for the file-based audio cache.
    /// When null or empty, defaults to {TempPath}/memorix-audio-cache.
    /// </summary>
    public string? AudioCacheDir { get; set; }

    /// <summary>
    /// Whether VAD (Voice Activity Detection) is enabled in the media preparation pipeline.
    /// When enabled, audio is segmented into speech chunks before ASR.
    /// </summary>
    public bool VadEnabled { get; set; } = true;

    /// <summary>
    /// Target sample rate (Hz) for FFmpeg normalization. 16000 Hz is the ASR standard.
    /// </summary>
    public int NormalizeSampleRate { get; set; } = 16000;

    /// <summary>
    /// Target channel count for FFmpeg normalization (1 = mono).
    /// </summary>
    public int NormalizeChannels { get; set; } = 1;

    /// <summary>
    /// Maximum concurrent transcription jobs to prevent resource exhaustion.
    /// </summary>
    public int MaxConcurrentTranscriptions { get; set; } = 2;

    /// <summary>
    /// Maximum age (in hours) for cached audio files before they are eligible for purge.
    /// </summary>
    public int CacheMaxAgeHours { get; set; } = 24;

    // ── TTS (Text-to-Speech) Settings ──

    /// <summary>
    /// Base URL of the Fish-Speech TTS server (e.g. http://localhost:8080).
    /// Used for high-quality neural TTS with voice cloning when <see cref="FishSpeechEnabled"/> is true.
    /// </summary>
    public string FishSpeechBaseUrl { get; set; } = "http://localhost:8080";

    /// <summary>
    /// Whether Fish-Speech TTS is enabled for local neural speech synthesis.
    /// When false, the system degrades to Piper or System TTS.
    /// </summary>
    public bool FishSpeechEnabled { get; set; } = false;

    /// <summary>
    /// Base URL of the Piper TTS server using Wyoming HTTP protocol (e.g. http://localhost:10200).
    /// Used for mid-quality local neural TTS when <see cref="PiperEnabled"/> is true.
    /// </summary>
    public string PiperBaseUrl { get; set; } = "http://localhost:10200";

    /// <summary>
    /// Whether Piper TTS is enabled for local neural speech synthesis.
    /// When false, the system degrades to System TTS.
    /// </summary>
    public bool PiperEnabled { get; set; } = false;

    /// <summary>
    /// Default voice ID to use for TTS synthesis when no voice is specified in the request.
    /// </summary>
    public string TtsDefaultVoice { get; set; } = "default";

    // ── Memorix Cloud TTS Settings ──

    /// <summary>
    /// Base URL of the Memorix cloud TTS API (e.g. https://tts.memorix.cloud).
    /// Used by <c>CloudTtsProvider</c> for cloud-hosted neural speech synthesis.
    /// </summary>
    public string CloudTtsBaseUrl { get; set; } = "https://tts.memorix.cloud";

    /// <summary>
    /// Whether the Memorix cloud TTS service is enabled. When false, the system
    /// degrades to local TTS providers (Fish-Speech, Piper, System TTS).
    /// </summary>
    public bool CloudTtsEnabled { get; set; } = false;

    /// <summary>
    /// Platform-managed API key for the Memorix cloud TTS service, used by the
    /// PLATFORM_MANAGED credential mode. When null/empty, the provider falls back
    /// to the <c>MEMORIX_CLOUD_TTS_API_KEY</c> environment variable. For BYOK
    /// modes, credentials are resolved via <see cref="ICredentialManager"/>.
    /// </summary>
    public string? CloudTtsPlatformApiKey { get; set; }

    // ── Faster-Whisper ASR Settings ──

    /// <summary>
    /// Base URL of the local faster-whisper HTTP server (e.g. http://localhost:8000).
    /// Used by <c>FasterWhisperAsrProvider</c> for local/LAN-node speech recognition.
    /// The server should expose an OpenAI-compatible <c>/v1/audio/transcriptions</c> endpoint.
    /// </summary>
    public string FasterWhisperEndpoint { get; set; } = "http://localhost:8000";

    /// <summary>
    /// Whether the local faster-whisper HTTP server is enabled for ASR.
    /// When false, the system falls back to whisper.cpp CLI or other local providers.
    /// </summary>
    public bool FasterWhisperEnabled { get; set; } = false;

    // ── Device Capability Detection Settings ──

    /// <summary>
    /// Whether GPU detection is enabled during server-side device capability detection.
    /// When false, GpuAvailable is always reported as false.
    /// </summary>
    public bool GpuDetectionEnabled { get; set; } = false;

    /// <summary>
    /// Minimum CPU cores required for local ASR (speech-to-text) support.
    /// </summary>
    public int MinCoresForLocalAsr { get; set; } = 4;

    /// <summary>
    /// Minimum available memory (in MB) required for local ASR support.
    /// </summary>
    public long MinMemoryMbForLocalAsr { get; set; } = 2048;

    /// <summary>
    /// Minimum CPU cores required for local TTS (text-to-speech) support.
    /// </summary>
    public int MinCoresForLocalTts { get; set; } = 2;

    /// <summary>
    /// Minimum available memory (in MB) required for local TTS support.
    /// </summary>
    public long MinMemoryMbForLocalTts { get; set; } = 1024;

    // ── Cost Estimation Settings ──

    /// <summary>
    /// Provider pricing rates used by <c>CostEstimator</c> for cost estimation.
    /// Keys can be composite ("providerId:PRICING_UNIT") for provider-specific rates
    /// or bare pricing-unit names ("SECOND", "MINUTE", "REQUEST", "TOKEN") for defaults.
    /// </summary>
    public Dictionary<string, decimal> ProviderPricingRates { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["SECOND"] = 0.01m,
        ["MINUTE"] = 0.60m,
        ["REQUEST"] = 0.10m,
        ["TOKEN"] = 0.0001m
    };

    // ── LAN Node Discovery Settings ──

    /// <summary>
    /// Comma-separated list of LAN node endpoint URLs to probe during discovery
    /// (e.g. "http://192.168.1.50:8080,http://192.168.1.51:8080").
    /// </summary>
    public string? LanNodeEndpoints { get; set; }

    /// <summary>
    /// Heartbeat freshness timeout in seconds. A node whose last heartbeat is
    /// older than this is considered stale and not selected for capability delegation.
    /// </summary>
    public int LanNodeHeartbeatTimeoutSec { get; set; } = 60;

    // ── Third-Party Cloud ASR Settings ──

    /// <summary>
    /// Base URL of the Zhipu (BigModel) cloud API for GLM-ASR speech recognition
    /// (e.g. https://open.bigmodel.cn). Used by <c>ZhipuGlmAsrProvider</c>.
    /// </summary>
    public string ZhipuBaseUrl { get; set; } = "https://open.bigmodel.cn";

    /// <summary>
    /// Platform-managed Zhipu API key for the PLATFORM_MANAGED credential mode.
    /// When null/empty, the provider falls back to the MEMORIX_ZHIPU_API_KEY
    /// environment variable. For BYOK modes, credentials are resolved via
    /// <see cref="ICredentialManager"/>.
    /// </summary>
    public string? ZhipuPlatformApiKey { get; set; }

    // ── Multi-Cloud Failover / Circuit Breaker Settings ──

    /// <summary>
    /// Number of consecutive failures before the circuit breaker trips
    /// and stops routing to a provider.
    /// </summary>
    public int CircuitBreakerThreshold { get; set; } = 3;

    /// <summary>
    /// Time in seconds after which a tripped circuit breaker resets
    /// and allows retrying the provider.
    /// </summary>
    public int CircuitBreakerResetSec { get; set; } = 300;
}
