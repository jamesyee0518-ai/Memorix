using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Application.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Minio;
using Minio.DataModel.Args;

namespace KnowledgeEngine.Infrastructure.Storage;

public class MinioStorageProvider : IFileStorageProvider
{
    private readonly IMinioClient _minioClient;
    private readonly MinioSettings _settings;
    private readonly ILogger<MinioStorageProvider> _logger;

    public MinioStorageProvider(
        IOptions<MinioSettings> settings,
        ILogger<MinioStorageProvider> logger)
    {
        _settings = settings.Value;
        _logger = logger;

        var builder = new MinioClient()
            .WithEndpoint(_settings.Endpoint)
            .WithCredentials(_settings.AccessKey, _settings.SecretKey);

        if (_settings.UseSsl)
        {
            builder = builder.WithSSL();
        }

        _minioClient = builder.Build();
    }

    public async Task UploadFileAsync(string bucket, string objectKey, Stream stream, string contentType, long? fileSize = null, CancellationToken cancellationToken = default)
    {
        await EnsureBucketExistsAsync(bucket, cancellationToken);

        var putArgs = new PutObjectArgs()
            .WithBucket(bucket)
            .WithObject(objectKey)
            .WithStreamData(stream)
            .WithObjectSize(fileSize ?? stream.Length)
            .WithContentType(contentType);

        await _minioClient.PutObjectAsync(putArgs, cancellationToken);

        _logger.LogInformation("File uploaded to {Bucket}/{ObjectKey}", bucket, objectKey);
    }

    public async Task<string> GetPresignedDownloadUrlAsync(string bucket, string objectKey, int expiry, CancellationToken cancellationToken = default)
    {
        var args = new PresignedGetObjectArgs()
            .WithBucket(bucket)
            .WithObject(objectKey)
            .WithExpiry(expiry);

        var url = await _minioClient.PresignedGetObjectAsync(args);
        return url;
    }

    public async Task EnsureBucketExistsAsync(string bucket, CancellationToken cancellationToken = default)
    {
        var existsArgs = new BucketExistsArgs().WithBucket(bucket);
        var exists = await _minioClient.BucketExistsAsync(existsArgs, cancellationToken);
        if (!exists)
        {
            var makeArgs = new MakeBucketArgs().WithBucket(bucket);
            await _minioClient.MakeBucketAsync(makeArgs, cancellationToken);
            _logger.LogInformation("Bucket created: {Bucket}", bucket);
        }
    }

    public async Task<Stream> DownloadFileAsync(string bucket, string objectKey, CancellationToken cancellationToken = default)
    {
        var ms = new MemoryStream();
        var getArgs = new GetObjectArgs()
            .WithBucket(bucket)
            .WithObject(objectKey)
            .WithCallbackStream(async (stream, ct) =>
            {
                await stream.CopyToAsync(ms, ct);
                await stream.DisposeAsync();
            });

        await _minioClient.GetObjectAsync(getArgs, cancellationToken);
        ms.Position = 0;

        _logger.LogInformation("File downloaded from {Bucket}/{ObjectKey}", bucket, objectKey);
        return ms;
    }

    public async Task DeleteFileAsync(string bucket, string objectKey, CancellationToken cancellationToken = default)
    {
        var args = new RemoveObjectArgs()
            .WithBucket(bucket)
            .WithObject(objectKey);
        await _minioClient.RemoveObjectAsync(args, cancellationToken);
        _logger.LogInformation("File deleted from {Bucket}/{ObjectKey}", bucket, objectKey);
    }

    public Task<string?> GetFilePathAsync(string bucket, string objectKey, CancellationToken cancellationToken = default)
    {
        // Cloud mode: no local file path
        return Task.FromResult<string?>(null);
    }
}
