using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Application.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KnowledgeEngine.Infrastructure.Audio;

/// <summary>
/// File-based audio cache service for FFmpeg-normalized audio deduplication.
/// <para>
/// Cache files are stored as <c>{cacheKey}.wav</c> in the configured cache directory.
/// The cache key encodes the source SHA-256, target sample rate, channel count, and
/// normalization version so that identical inputs skip redundant transcoding.
/// </para>
/// </summary>
public class AudioCacheService : IAudioCacheService
{
    private readonly AudioSettings _settings;
    private readonly ILogger<AudioCacheService> _logger;
    private readonly string _cacheDir;

    /// <summary>
    /// Buffer size (80 KB) used for async file copy operations.
    /// </summary>
    private const int CopyBufferSize = 81920;

    /// <summary>
    /// Creates a new <see cref="AudioCacheService"/>.
    /// </summary>
    /// <param name="options">Audio settings (provides <see cref="AudioSettings.AudioCacheDir"/> and <see cref="AudioSettings.CacheMaxAgeHours"/>).</param>
    /// <param name="logger">Logger for cache operations.</param>
    public AudioCacheService(IOptions<AudioSettings> options, ILogger<AudioCacheService> logger)
    {
        _settings = options.Value;
        _logger = logger;

        _cacheDir = !string.IsNullOrWhiteSpace(_settings.AudioCacheDir)
            ? _settings.AudioCacheDir
            : Path.Combine(Path.GetTempPath(), "memorix-audio-cache");

        Directory.CreateDirectory(_cacheDir);
    }

    /// <inheritdoc />
    public Task<string?> GetAsync(string cacheKey, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var path = GetCachePath(cacheKey);
        if (!File.Exists(path))
        {
            return Task.FromResult<string?>(null);
        }

        var maxAge = TimeSpan.FromHours(_settings.CacheMaxAgeHours);
        var lastWrite = File.GetLastWriteTimeUtc(path);
        if (DateTime.UtcNow - lastWrite > maxAge)
        {
            _logger.LogDebug(
                "Cache entry {CacheKey} expired (age {Age:hh\\h\\ mm\\m}, max {MaxHours}h)",
                cacheKey, DateTime.UtcNow - lastWrite, _settings.CacheMaxAgeHours);
            return Task.FromResult<string?>(null);
        }

        _logger.LogDebug("Cache hit for {CacheKey} -> {Path}", cacheKey, path);
        return Task.FromResult<string?>(path);
    }

    /// <inheritdoc />
    public async Task<string> PutAsync(string cacheKey, string sourceFilePath, CancellationToken ct)
    {
        var destPath = GetCachePath(cacheKey);

        // Write to a unique temp file first, then atomically move to the final destination.
        // This prevents partial writes from corrupting the cache on interruption.
        var tempPath = Path.Combine(_cacheDir, $"{cacheKey}.{Guid.NewGuid():N}.tmp");

        await using (var source = new FileStream(
                         sourceFilePath,
                         FileMode.Open,
                         FileAccess.Read,
                         FileShare.Read,
                         CopyBufferSize,
                         useAsync: true))
        await using (var dest = new FileStream(
                         tempPath,
                         FileMode.Create,
                         FileAccess.Write,
                         FileShare.None,
                         CopyBufferSize,
                         useAsync: true))
        {
            await source.CopyToAsync(dest, ct);
        }

        // Atomic replace: delete old file (if any) and move temp into place.
        if (File.Exists(destPath))
        {
            File.Delete(destPath);
        }

        File.Move(tempPath, destPath);

        _logger.LogDebug(
            "Cached normalized audio {CacheKey} -> {Path} ({SizeBytes} bytes)",
            cacheKey, destPath, new FileInfo(destPath).Length);

        return destPath;
    }

    /// <inheritdoc />
    public string ComputeCacheKey(string sourceSha256, int sampleRate, int channels, int normalizeVersion = 1)
    {
        return $"{sourceSha256}_{sampleRate}_{channels}_v{normalizeVersion}";
    }

    /// <inheritdoc />
    public Task PurgeAsync(TimeSpan maxAge, CancellationToken ct)
    {
        var cutoff = DateTime.UtcNow - maxAge;
        var deleted = 0;
        var errors = 0;

        // Purge both cached .wav files and leftover .tmp files.
        foreach (var file in Directory.EnumerateFiles(_cacheDir))
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                if (File.GetLastWriteTimeUtc(file) >= cutoff)
                {
                    continue;
                }

                File.Delete(file);
                deleted++;
            }
            catch (Exception ex)
            {
                errors++;
                _logger.LogWarning(ex, "Failed to purge cache file {File}", file);
            }
        }

        if (deleted > 0 || errors > 0)
        {
            _logger.LogInformation(
                "Purged {Deleted} expired audio cache files ({Errors} errors) from {CacheDir}",
                deleted, errors, _cacheDir);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Resolves the on-disk path for a given cache key.
    /// The cache key components (hex hash, integers, underscores) are filesystem-safe,
    /// but a defensive sanitization is applied to handle unexpected characters.
    /// </summary>
    private string GetCachePath(string cacheKey)
    {
        var safeKey = string.Concat(cacheKey.Select(SanitizeChar));
        return Path.Combine(_cacheDir, safeKey + ".wav");
    }

    private static char SanitizeChar(char c) =>
        char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_';
}
