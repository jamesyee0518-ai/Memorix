namespace KnowledgeEngine.Application.Interfaces;

/// <summary>
/// Phase 3 processing error codes (§24)
/// </summary>
public static class ProcessingErrorCodes
{
    public const string FetchTimeout = "FETCH_TIMEOUT";
    public const string FetchForbidden = "FETCH_FORBIDDEN";
    public const string FetchNotFound = "FETCH_NOT_FOUND";
    public const string FetchTooLarge = "FETCH_TOO_LARGE";
    public const string ParseEmptyContent = "PARSE_EMPTY_CONTENT";
    public const string ParseUnsupportedType = "PARSE_UNSUPPORTED_TYPE";
    public const string ParsePdfScanned = "PARSE_PDF_SCANNED";
    public const string ParsePdfTooLarge = "PARSE_PDF_TOO_LARGE";
    public const string CleanFailed = "CLEAN_FAILED";
    public const string AiModelUnavailable = "AI_MODEL_UNAVAILABLE";
    public const string AiTimeout = "AI_TIMEOUT";
    public const string AiInvalidJson = "AI_INVALID_JSON";
    public const string AiContentTooLong = "AI_CONTENT_TOO_LONG";
    public const string DocumentCreateFailed = "DOCUMENT_CREATE_FAILED";
    public const string UnknownError = "UNKNOWN_ERROR";
}
