using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KnowledgeEngine.Infrastructure.Processing.Processors;

public class PdfProcessor : ISourceProcessor
{
    private const string ParserVersion = "1.0";

    private readonly IAppDbContext _db;
    private readonly IContentProcessor _contentProcessor;
    private readonly IFileStorageProvider _fileStorageProvider;
    private readonly ILogger<PdfProcessor> _logger;

    public PdfProcessor(
        IAppDbContext db,
        IContentProcessor contentProcessor,
        IFileStorageProvider fileStorageProvider,
        ILogger<PdfProcessor> logger)
    {
        _db = db;
        _contentProcessor = contentProcessor;
        _fileStorageProvider = fileStorageProvider;
        _logger = logger;
    }

    public bool Supports(string sourceType)
    {
        return string.Equals(sourceType, "pdf", StringComparison.OrdinalIgnoreCase)
            || string.Equals(sourceType, "file", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<ParseResult> ParseAsync(Source source, CancellationToken ct = default)
    {
        if (source.OriginalFileId == null)
        {
            throw new InvalidOperationException("PDF source has no original file");
        }

        var fileObject = await _db.Files.FirstOrDefaultAsync(f => f.Id == source.OriginalFileId.Value, ct);
        if (fileObject == null)
        {
            throw new InvalidOperationException("PDF file object not found");
        }

        await using var stream = await _fileStorageProvider.DownloadFileAsync(
            fileObject.Bucket, fileObject.ObjectKey, ct);
        var pdfText = await _contentProcessor.ExtractPdfTextAsync(stream);

        var result = new ParseResult
        {
            Title = source.Title,
            Author = source.Author,
            PublishedAt = source.PublishedAt,
            RawText = pdfText,
            Markdown = pdfText,
            ParserName = "pdf",
            ParserVersion = ParserVersion
        };

        _logger.LogInformation("PDF parsed: file={FileId}, text length={Length}",
            fileObject.Id, pdfText.Length);

        return result;
    }
}
