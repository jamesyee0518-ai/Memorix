namespace KnowledgeEngine.Domain.Enums;

public enum JobType
{
    FetchUrl,
    ParseText,
    ParsePdf,
    SaveFile,
    PrepareDocument,

    // Phase 4 Job Types
    Tagging,
    EntityExtraction,
    Chunking,
    Embedding,
    VectorIndex,
    IndexRebuild
}
