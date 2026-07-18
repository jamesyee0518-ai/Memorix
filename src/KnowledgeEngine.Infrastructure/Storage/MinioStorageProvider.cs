using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Application.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KnowledgeEngine.Infrastructure.Storage;

/// <summary>
/// S3-compatible cloud storage provider. The historical class name is retained
/// to avoid changing factory and dependency-injection contracts.
/// </summary>
public sealed class MinioStorageProvider : IFileStorageProvider, IDisposable
{
    private readonly AmazonS3Client _s3Client;
    private readonly MinioSettings _settings;
    private readonly ILogger<MinioStorageProvider> _logger;

    public MinioStorageProvider(
        IOptions<MinioSettings> settings,
        ILogger<MinioStorageProvider> logger)
    {
        _settings = settings.Value;
        _logger = logger;

        var endpoint = _settings.Endpoint.TrimEnd('/');
        if (!endpoint.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !endpoint.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            endpoint = $"{(_settings.UseSsl ? "https" : "http")}://{endpoint}";
        }

        var config = new AmazonS3Config
        {
            ServiceURL = endpoint,
            ForcePathStyle = _settings.ForcePathStyle,
            AuthenticationRegion = _settings.Region
        };

        _s3Client = new AmazonS3Client(
            new BasicAWSCredentials(_settings.AccessKey, _settings.SecretKey),
            config);
    }

    public async Task UploadFileAsync(string bucket, string objectKey, Stream stream, string contentType, long? fileSize = null, CancellationToken cancellationToken = default)
    {
        await EnsureBucketExistsAsync(bucket, cancellationToken);
        var target = ResolveTarget(bucket, objectKey);

        var request = new PutObjectRequest
        {
            BucketName = target.Bucket,
            Key = target.Key,
            InputStream = stream,
            ContentType = contentType,
            AutoCloseStream = false
        };

        await _s3Client.PutObjectAsync(request, cancellationToken);
        _logger.LogInformation("File uploaded to {Bucket}/{ObjectKey}", bucket, objectKey);
    }

    public Task<string> GetPresignedDownloadUrlAsync(string bucket, string objectKey, int expiry, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var target = ResolveTarget(bucket, objectKey);
        var request = new GetPreSignedUrlRequest
        {
            BucketName = target.Bucket,
            Key = target.Key,
            Expires = DateTime.UtcNow.AddSeconds(expiry),
            Verb = HttpVerb.GET
        };

        return _s3Client.GetPreSignedURLAsync(request);
    }

    public async Task EnsureBucketExistsAsync(string bucket, CancellationToken cancellationToken = default)
    {
        if (!_settings.AutoCreateBucket)
        {
            return;
        }

        var targetBucket = ResolveTarget(bucket, string.Empty).Bucket;
        try
        {
            await _s3Client.GetBucketLocationAsync(
                new GetBucketLocationRequest { BucketName = targetBucket },
                cancellationToken);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            await _s3Client.PutBucketAsync(
                new PutBucketRequest { BucketName = targetBucket },
                cancellationToken);
            _logger.LogInformation("Bucket created: {Bucket}", targetBucket);
        }
    }

    public async Task<Stream> DownloadFileAsync(string bucket, string objectKey, CancellationToken cancellationToken = default)
    {
        var target = ResolveTarget(bucket, objectKey);
        using var response = await _s3Client.GetObjectAsync(target.Bucket, target.Key, cancellationToken);
        var output = new MemoryStream();
        await response.ResponseStream.CopyToAsync(output, cancellationToken);
        output.Position = 0;

        _logger.LogInformation("File downloaded from {Bucket}/{ObjectKey}", bucket, objectKey);
        return output;
    }

    public async Task DeleteFileAsync(string bucket, string objectKey, CancellationToken cancellationToken = default)
    {
        var target = ResolveTarget(bucket, objectKey);
        await _s3Client.DeleteObjectAsync(target.Bucket, target.Key, cancellationToken);
        _logger.LogInformation("File deleted from {Bucket}/{ObjectKey}", bucket, objectKey);
    }

    public Task<string?> GetFilePathAsync(string bucket, string objectKey, CancellationToken cancellationToken = default) =>
        Task.FromResult<string?>(null);

    private (string Bucket, string Key) ResolveTarget(string logicalBucket, string objectKey)
    {
        if (!_settings.UseConfiguredBucket ||
            string.Equals(logicalBucket, _settings.Bucket, StringComparison.Ordinal))
        {
            return (logicalBucket, objectKey);
        }

        var prefix = logicalBucket.Trim('/');
        var key = objectKey.TrimStart('/');
        return (_settings.Bucket, string.IsNullOrEmpty(key) ? prefix : $"{prefix}/{key}");
    }

    public void Dispose() => _s3Client.Dispose();
}
