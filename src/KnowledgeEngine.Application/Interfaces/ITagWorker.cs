namespace KnowledgeEngine.Application.Interfaces;

/// <summary>
/// On-demand AI tag recommendation worker (Phase 4).
/// Not a BackgroundService — invoked explicitly (e.g. via API action endpoint).
/// </summary>
public interface ITagWorker
{
    /// <summary>
    /// Generate AI tag recommendations for a document, persist Tag + DocumentTag records,
    /// and update the document's tag status.
    /// </summary>
    Task ProcessDocumentAsync(Guid documentId, CancellationToken ct = default);
}
