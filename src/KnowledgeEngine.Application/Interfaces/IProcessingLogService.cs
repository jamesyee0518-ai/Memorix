namespace KnowledgeEngine.Application.Interfaces;

public interface IProcessingLogService
{
    Task LogAsync(string workspaceId, Guid? sourceId, Guid? documentId, string stepName, string status, string? message = null, string? errorCode = null, string? errorStack = null, int? durationMs = null, CancellationToken ct = default);
    Task<List<DTOs.ProcessingLogItem>> GetLogsByDocumentAsync(Guid documentId, CancellationToken ct = default);
    Task<List<DTOs.ProcessingLogItem>> GetLogsBySourceAsync(Guid sourceId, CancellationToken ct = default);
}
