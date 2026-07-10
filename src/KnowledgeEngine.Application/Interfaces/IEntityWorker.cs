namespace KnowledgeEngine.Application.Interfaces;

/// <summary>
/// On-demand AI entity extraction worker (Phase 4).
/// Not a BackgroundService — invoked explicitly (e.g. via API action endpoint).
/// </summary>
public interface IEntityWorker
{
    /// <summary>
    /// Extract entities from a document, persist Entity + DocumentEntity records,
    /// and update the document's entity status.
    /// </summary>
    Task ProcessDocumentAsync(Guid documentId, CancellationToken ct = default);
}
