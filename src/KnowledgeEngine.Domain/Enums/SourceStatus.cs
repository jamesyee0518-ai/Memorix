namespace KnowledgeEngine.Domain.Enums;

public enum SourceStatus
{
    Pending,
    Fetching,
    Uploaded,
    Saved,
    Queued,
    Parsing,
    Cleaning,
    AiProcessing,
    Indexing,
    Done,
    Failed,
    Archived
}
