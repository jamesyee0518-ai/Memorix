namespace KnowledgeEngine.Application.Interfaces;

/// <summary>
/// Cloud Runtime interfaces (stubs for Phase 1).
/// These define the contract for cloud implementations.
/// Actual cloud implementations will be added in later phases.
/// </summary>

/// <summary>
/// Cloud repository: accesses data via HTTP API → Cloud Backend → PostgreSQL.
/// </summary>
public interface ICloudKnowledgeRepository : IKnowledgeRepository
{
    /// <summary>
    /// Configure the cloud API base URL and auth token.
    /// </summary>
    void Configure(string apiBaseUrl, string authToken);
}

/// <summary>
/// Cloud file storage: uses S3 / MinIO via HTTP API.
/// </summary>
public interface ICloudFileStorage : IFileStorageProvider
{
    void Configure(string apiBaseUrl, string authToken);
}

/// <summary>
/// Cloud model provider: uses cloud model APIs (OpenAI, Anthropic, etc.).
/// </summary>
public interface ICloudModelProvider : IModelProvider
{
    void Configure(string apiKey, string? chatModel, string? embeddingModel);
}

/// <summary>
/// Cloud job queue: uses Redis / external queue service.
/// </summary>
public interface ICloudJobQueue : IJobQueue
{
    void Configure(string redisConnectionString);
}

/// <summary>
/// Marker interface for runtime mode.
/// </summary>
public interface IRuntime
{
    string Mode { get; }
}
