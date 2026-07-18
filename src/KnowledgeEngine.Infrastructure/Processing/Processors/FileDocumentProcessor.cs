using System.Text;
using System.IO.Compression;
using System.Xml.Linq;
using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KnowledgeEngine.Infrastructure.Processing.Processors;

public class FileDocumentProcessor : ISourceProcessor
{
    private const string ParserVersion = "1.0";
    private const int MaxSpreadsheetCells = 200_000;

    private readonly IAppDbContext _db;
    private readonly IFileStorageFactory _fileStorageFactory;
    private readonly ILogger<FileDocumentProcessor> _logger;

    public FileDocumentProcessor(
        IAppDbContext db,
        IFileStorageFactory fileStorageFactory,
        ILogger<FileDocumentProcessor> logger)
    {
        _db = db;
        _fileStorageFactory = fileStorageFactory;
        _logger = logger;
    }

    public bool Supports(string sourceType)
        => sourceType is "markdown" or "text_file" or "word" or "spreadsheet" or "csv";

    public async Task<ParseResult> ParseAsync(Source source, CancellationToken ct = default)
    {
        if (source.OriginalFileId == null)
        {
            throw new InvalidOperationException("File source has no original file");
        }

        var fileObject = await _db.Files.FirstOrDefaultAsync(
            file => file.Id == source.OriginalFileId.Value, ct)
            ?? throw new InvalidOperationException("File object not found");

        var storageProvider = await _fileStorageFactory.GetProviderForWorkspaceAsync(
            fileObject.WorkspaceId.ToString(), ct);
        await using var stream = await storageProvider.DownloadFileAsync(
            fileObject.Bucket, fileObject.ObjectKey, ct);

        var extension = Path.GetExtension(fileObject.OriginalFilename ?? fileObject.ObjectKey).ToLowerInvariant();
        var text = extension switch
        {
            ".md" or ".markdown" or ".txt" or ".csv" => await ReadTextAsync(stream, ct),
            ".docx" => ReadDocx(stream),
            ".xlsx" => ReadXlsx(stream),
            ".doc" or ".xls" => await ReadLegacyOfficeAsync(stream, ct),
            _ => throw new InvalidOperationException($"Unsupported document format: {extension}")
        };

        _logger.LogInformation(
            "File parsed: file={FileId}, extension={Extension}, text length={Length}",
            fileObject.Id, extension, text.Length);

        return new ParseResult
        {
            Title = source.Title,
            Author = source.Author,
            PublishedAt = source.PublishedAt,
            RawText = text,
            Markdown = text,
            ParserName = $"file-{extension.TrimStart('.')}",
            ParserVersion = ParserVersion
        };
    }

    private static async Task<string> ReadTextAsync(Stream stream, CancellationToken ct)
    {
        using var reader = new StreamReader(
            stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        return await reader.ReadToEndAsync(ct);
    }

    private static string ReadDocx(Stream stream)
    {
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        var entry = archive.GetEntry("word/document.xml")
            ?? throw new InvalidOperationException("Invalid DOCX file: document.xml is missing");
        using var entryStream = entry.Open();
        var document = XDocument.Load(entryStream);
        XNamespace word = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

        return string.Join(
            Environment.NewLine,
            document.Descendants(word + "p")
                .Select(paragraph => string.Concat(paragraph.Descendants(word + "t").Select(text => text.Value)))
                .Where(paragraph => !string.IsNullOrWhiteSpace(paragraph)));
    }

    private static string ReadXlsx(Stream stream)
    {
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        XNamespace spreadsheet = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace relationships = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace packageRelationships = "http://schemas.openxmlformats.org/package/2006/relationships";

        var sharedStrings = ReadSharedStrings(archive, spreadsheet);
        var workbook = LoadXmlEntry(archive, "xl/workbook.xml");
        var workbookRelationships = LoadXmlEntry(archive, "xl/_rels/workbook.xml.rels");
        var relationshipTargets = workbookRelationships
            .Descendants(packageRelationships + "Relationship")
            .Where(element => element.Attribute("Id") != null && element.Attribute("Target") != null)
            .ToDictionary(
                element => element.Attribute("Id")!.Value,
                element => element.Attribute("Target")!.Value);

        var output = new StringBuilder();
        var cellCount = 0;

        foreach (var sheet in workbook.Descendants(spreadsheet + "sheet"))
        {
            var sheetName = sheet.Attribute("name")?.Value ?? "Sheet";
            var relationshipId = sheet.Attribute(relationships + "id")?.Value;
            if (relationshipId == null || !relationshipTargets.TryGetValue(relationshipId, out var target))
            {
                continue;
            }

            var sheetPath = target.StartsWith('/')
                ? target.TrimStart('/')
                : $"xl/{target.TrimStart('/')}";
            var worksheet = LoadXmlEntry(archive, sheetPath.Replace("xl/../", string.Empty));
            output.AppendLine($"## {sheetName}");

            foreach (var row in worksheet.Descendants(spreadsheet + "row"))
            {
                var values = new List<string>();
                foreach (var cell in row.Elements(spreadsheet + "c"))
                {
                    values.Add(ReadSpreadsheetCell(cell, sharedStrings, spreadsheet));
                    cellCount++;
                    if (cellCount >= MaxSpreadsheetCells)
                    {
                        values.Add("[内容过长，已截断]");
                        break;
                    }
                }

                if (values.Any(value => !string.IsNullOrWhiteSpace(value)))
                {
                    output.AppendLine(string.Join('\t', values));
                }

                if (cellCount >= MaxSpreadsheetCells)
                {
                    return output.ToString();
                }
            }

            output.AppendLine();
        }

        return output.ToString();
    }

    private static XDocument LoadXmlEntry(ZipArchive archive, string path)
    {
        var entry = archive.GetEntry(path)
            ?? throw new InvalidOperationException($"Invalid Office document: {path} is missing");
        using var stream = entry.Open();
        return XDocument.Load(stream);
    }

    private static List<string> ReadSharedStrings(ZipArchive archive, XNamespace spreadsheet)
    {
        var entry = archive.GetEntry("xl/sharedStrings.xml");
        if (entry == null)
        {
            return [];
        }

        using var stream = entry.Open();
        var document = XDocument.Load(stream);
        return document.Descendants(spreadsheet + "si")
            .Select(item => string.Concat(item.Descendants(spreadsheet + "t").Select(text => text.Value)))
            .ToList();
    }

    private static string ReadSpreadsheetCell(
        XElement cell,
        IReadOnlyList<string> sharedStrings,
        XNamespace spreadsheet)
    {
        var type = cell.Attribute("t")?.Value;
        if (type == "inlineStr")
        {
            return string.Concat(cell.Descendants(spreadsheet + "t").Select(text => text.Value));
        }

        var value = cell.Element(spreadsheet + "v")?.Value ?? string.Empty;
        if (type == "s" && int.TryParse(value, out var sharedStringIndex)
            && sharedStringIndex >= 0 && sharedStringIndex < sharedStrings.Count)
        {
            return sharedStrings[sharedStringIndex];
        }

        return value;
    }

    private static async Task<string> ReadLegacyOfficeAsync(Stream stream, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, ct);
        var bytes = buffer.ToArray();
        var fragments = new List<string>();

        ExtractPrintableRuns(Encoding.Unicode.GetString(bytes), fragments, 4);
        ExtractPrintableRuns(Encoding.Latin1.GetString(bytes), fragments, 6);

        var text = string.Join(Environment.NewLine, fragments.Distinct());
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidOperationException(
                "No readable text was found in the legacy .doc file; please convert it to .docx");
        }

        return text;
    }

    private static void ExtractPrintableRuns(string input, ICollection<string> output, int minimumLength)
    {
        var current = new StringBuilder();
        foreach (var character in input)
        {
            if (!char.IsControl(character) || character is '\r' or '\n' or '\t')
            {
                current.Append(character);
                continue;
            }

            AddFragment(current, output, minimumLength);
        }
        AddFragment(current, output, minimumLength);
    }

    private static void AddFragment(StringBuilder fragment, ICollection<string> output, int minimumLength)
    {
        var value = fragment.ToString().Trim();
        if (value.Length >= minimumLength)
        {
            output.Add(value);
        }
        fragment.Clear();
    }
}
