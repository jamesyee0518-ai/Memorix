namespace KnowledgeEngine.Application.Interfaces;

public interface IContentProcessor
{
    Task<string> CleanHtmlAsync(string html);
    Task<string> ExtractPdfTextAsync(Stream pdfStream);
    string NormalizeText(string text);
    List<string> ChunkText(string text, int maxChunkSize = 2000);
}
