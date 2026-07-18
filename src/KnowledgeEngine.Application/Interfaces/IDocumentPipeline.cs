namespace KnowledgeEngine.Application.Interfaces;

public interface IDocumentPipeline
{
    Task ProcessSourceAsync(Guid sourceId, Guid userId, CancellationToken ct = default);
}
