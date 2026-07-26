using KnowledgeEngine.Application.DTOs;

namespace KnowledgeEngine.Application.Interfaces;

public interface IEntityGovernanceService
{
    Task<EntityGovernanceTaskDto> StartDuplicateScanAsync(
        Guid userId,
        StartEntityScanRequest request,
        CancellationToken ct = default);
    Task<EntityGovernanceTaskDto> StartMaintenanceAsync(
        Guid userId,
        StartEntityMaintenanceRequest request,
        CancellationToken ct = default);
    Task<EntityGovernanceTaskDto?> GetTaskAsync(
        Guid userId, Guid taskId, CancellationToken ct = default);
    Task<IReadOnlyList<EntityGovernanceTaskDto>> ListCandidatesAsync(
        Guid userId, Guid taskId, CancellationToken ct = default);
    Task<IReadOnlyList<EntityGovernanceTaskDto>> ListTasksAsync(
        Guid userId,
        Guid? workspaceId,
        string? status,
        string? taskType,
        int limit = 100,
        CancellationToken ct = default);
    Task<EntityGovernanceTaskDto> DecideAsync(
        Guid userId,
        Guid taskId,
        EntityGovernanceDecisionRequest request,
        CancellationToken ct = default);
    Task<EntityQualityMetrics> GetQualityMetricsAsync(
        Guid userId, Guid? workspaceId, CancellationToken ct = default);
    Task<EntityGovernanceTaskDto> PauseAsync(
        Guid userId, Guid taskId, CancellationToken ct = default);
    Task<EntityGovernanceTaskDto> ResumeAsync(
        Guid userId, Guid taskId, CancellationToken ct = default);
    Task<EntityGovernanceTaskDto> RetryAsync(
        Guid userId, Guid taskId, CancellationToken ct = default);
    Task<bool> ProcessNextBatchAsync(CancellationToken ct = default);
}
