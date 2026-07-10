using KnowledgeEngine.Domain.Entities;

namespace KnowledgeEngine.Application.Interfaces;

public interface IChunkingService
{
    List<DocumentChunk> ChunkDocument(Document document);
}
