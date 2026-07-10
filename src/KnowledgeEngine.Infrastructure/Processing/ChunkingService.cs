using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace KnowledgeEngine.Infrastructure.Processing;

public class ChunkingService : IChunkingService
{
    private const int TargetChunkSize = 3200;       // ~800 tokens (4 chars/token)
    private const int MaxChunkSize = 4800;           // ~1200 tokens
    private const int OverlapSize = 480;             // ~120 tokens
    private const int MinParagraphTokens = 150;      // ~600 chars

    private readonly ILogger<ChunkingService> _logger;

    public ChunkingService(ILogger<ChunkingService> logger)
    {
        _logger = logger;
    }

    public List<DocumentChunk> ChunkDocument(Document document)
    {
        var chunks = new List<DocumentChunk>();
        var markdown = document.ContentMarkdown ?? document.ContentText ?? string.Empty;

        if (string.IsNullOrWhiteSpace(markdown))
        {
            _logger.LogWarning("Document {DocumentId} has no content to chunk", document.Id);
            return chunks;
        }

        // Parse markdown into sections based on heading hierarchy
        var sections = ParseMarkdownSections(markdown);

        // Merge sections and build chunks with sliding overlap
        var rawChunks = BuildRawChunks(sections);

        var now = DateTime.UtcNow;
        for (var i = 0; i < rawChunks.Count; i++)
        {
            var raw = rawChunks[i];
            var charCount = raw.Content.Length;
            var tokenCount = EstimateTokens(raw.Content);
            var contentHash = Sha256Hex(raw.Content);
            var headingPathStr = raw.HeadingPath.Count > 0 ? string.Join(" / ", raw.HeadingPath) : null;

            var chunk = new DocumentChunk
            {
                Id = Guid.NewGuid(),
                DocumentId = document.Id,
                SourceId = document.SourceId,
                UserId = document.UserId,
                TopicId = document.TopicId,
                ChunkIndex = i,
                ChunkTitle = raw.HeadingPath.Count > 0 ? raw.HeadingPath[^1] : document.Title,
                Content = raw.Content,
                ContentMarkdown = raw.MarkdownContent,
                TokenCount = tokenCount,
                CharCount = charCount,
                StartOffset = raw.StartOffset,
                EndOffset = raw.EndOffset,
                EmbeddingStatus = "pending",
                Metadata = BuildMetadata(raw.HeadingPath, DetermineSourceType(document)),
                CreatedAt = now,
                UpdatedAt = now,

                // Phase 4 rich fields
                ChunkUid = Sha256Hex($"{document.Id}_{i}_{contentHash}"),
                HeadingPath = headingPathStr,
                SectionLevel = raw.HeadingPath.Count,
                ContentHash = contentHash,
                IndexStatus = "pending"
            };

            chunks.Add(chunk);
        }

        // Link chunks in sequence: set PrevChunkId / NextChunkId
        for (var i = 0; i < chunks.Count; i++)
        {
            if (i > 0)
            {
                chunks[i].PrevChunkId = chunks[i - 1].Id;
            }
            if (i < chunks.Count - 1)
            {
                chunks[i].NextChunkId = chunks[i + 1].Id;
            }
        }

        _logger.LogInformation("Document {DocumentId} chunked into {Count} chunks",
            document.Id, chunks.Count);

        return chunks;
    }

    private List<MarkdownSection> ParseMarkdownSections(string markdown)
    {
        var sections = new List<MarkdownSection>();
        var lines = markdown.Split('\n');
        var headingPath = new List<string>();
        var currentContent = new System.Text.StringBuilder();
        var currentMarkdown = new System.Text.StringBuilder();
        var sectionStartOffset = 0;
        var currentOffset = 0;

        void FlushSection()
        {
            if (currentContent.Length > 0)
            {
                sections.Add(new MarkdownSection
                {
                    HeadingPath = new List<string>(headingPath),
                    Content = currentContent.ToString().TrimEnd(),
                    MarkdownContent = currentMarkdown.ToString().TrimEnd(),
                    StartOffset = sectionStartOffset,
                    EndOffset = currentOffset
                });
            }
            currentContent.Clear();
            currentMarkdown.Clear();
        }

        foreach (var line in lines)
        {
            var lineWithNewline = line + "\n";
            var trimmed = line.TrimStart();

            // Check for markdown headings (#, ##, ###, etc.)
            if (trimmed.StartsWith("#"))
            {
                FlushSection();

                var hashCount = 0;
                while (hashCount < trimmed.Length && trimmed[hashCount] == '#')
                {
                    hashCount++;
                }

                var headingText = trimmed.Substring(hashCount).Trim();

                // Update heading path based on level
                while (headingPath.Count >= hashCount)
                {
                    headingPath.RemoveAt(headingPath.Count - 1);
                }
                headingPath.Add(headingText);

                sectionStartOffset = currentOffset;
            }

            currentContent.Append(lineWithNewline);
            currentMarkdown.Append(lineWithNewline);
            currentOffset += lineWithNewline.Length;
        }

        FlushSection();

        return sections;
    }

    private List<RawChunk> BuildRawChunks(List<MarkdownSection> sections)
    {
        var rawChunks = new List<RawChunk>();

        // First, merge short sections
        var mergedSections = MergeShortSections(sections);

        // Build chunks from merged sections
        var currentContent = new System.Text.StringBuilder();
        var currentMarkdown = new System.Text.StringBuilder();
        var currentHeadingPath = new List<string>();
        var currentStartOffset = 0;
        var currentEndOffset = 0;

        foreach (var section in mergedSections)
        {
            // If adding this section would exceed max chunk size and we already have content,
            // flush the current chunk
            if (currentContent.Length > 0 &&
                currentContent.Length + section.Content.Length > MaxChunkSize)
            {
                // Save current chunk
                rawChunks.Add(new RawChunk
                {
                    HeadingPath = new List<string>(currentHeadingPath),
                    Content = currentContent.ToString().TrimEnd(),
                    MarkdownContent = currentMarkdown.ToString().TrimEnd(),
                    StartOffset = currentStartOffset,
                    EndOffset = currentEndOffset
                });

                // Start new chunk with overlap from the previous chunk
                var overlapText = GetOverlapText(currentContent.ToString(), OverlapSize);
                currentContent.Clear();
                currentMarkdown.Clear();

                if (!string.IsNullOrEmpty(overlapText))
                {
                    currentContent.Append(overlapText);
                    currentMarkdown.Append(overlapText);
                }
            }

            if (currentContent.Length == 0)
            {
                currentHeadingPath = new List<string>(section.HeadingPath);
                currentStartOffset = section.StartOffset;
            }

            currentContent.Append(section.Content).Append("\n\n");
            currentMarkdown.Append(section.MarkdownContent).Append("\n\n");
            currentEndOffset = section.EndOffset;

            // If current content exceeds target size, flush
            if (currentContent.Length >= TargetChunkSize)
            {
                rawChunks.Add(new RawChunk
                {
                    HeadingPath = new List<string>(currentHeadingPath),
                    Content = currentContent.ToString().TrimEnd(),
                    MarkdownContent = currentMarkdown.ToString().TrimEnd(),
                    StartOffset = currentStartOffset,
                    EndOffset = currentEndOffset
                });

                var overlapText = GetOverlapText(currentContent.ToString(), OverlapSize);
                currentContent.Clear();
                currentMarkdown.Clear();

                if (!string.IsNullOrEmpty(overlapText))
                {
                    currentContent.Append(overlapText);
                    currentMarkdown.Append(overlapText);
                }

                currentHeadingPath = new List<string>();
            }
        }

        // Flush remaining content
        if (currentContent.Length > 0)
        {
            rawChunks.Add(new RawChunk
            {
                HeadingPath = new List<string>(currentHeadingPath),
                Content = currentContent.ToString().TrimEnd(),
                MarkdownContent = currentMarkdown.ToString().TrimEnd(),
                StartOffset = currentStartOffset,
                EndOffset = currentEndOffset
            });
        }

        return rawChunks;
    }

    private List<MarkdownSection> MergeShortSections(List<MarkdownSection> sections)
    {
        var merged = new List<MarkdownSection>();
        var pendingContent = new System.Text.StringBuilder();
        var pendingMarkdown = new System.Text.StringBuilder();
        var pendingHeadingPath = new List<string>();
        var pendingStartOffset = 0;

        foreach (var section in sections)
        {
            var sectionTokenEstimate = EstimateTokens(section.Content);

            if (pendingContent.Length == 0)
            {
                pendingHeadingPath = new List<string>(section.HeadingPath);
                pendingStartOffset = section.StartOffset;
            }

            pendingContent.Append(section.Content).Append("\n\n");
            pendingMarkdown.Append(section.MarkdownContent).Append("\n\n");

            // If accumulated content exceeds minimum paragraph tokens, flush
            if (EstimateTokens(pendingContent.ToString()) >= MinParagraphTokens)
            {
                merged.Add(new MarkdownSection
                {
                    HeadingPath = new List<string>(pendingHeadingPath),
                    Content = pendingContent.ToString().TrimEnd(),
                    MarkdownContent = pendingMarkdown.ToString().TrimEnd(),
                    StartOffset = pendingStartOffset,
                    EndOffset = section.EndOffset
                });
                pendingContent.Clear();
                pendingMarkdown.Clear();
            }
        }

        // Flush remaining
        if (pendingContent.Length > 0)
        {
            merged.Add(new MarkdownSection
            {
                HeadingPath = new List<string>(pendingHeadingPath),
                Content = pendingContent.ToString().TrimEnd(),
                MarkdownContent = pendingMarkdown.ToString().TrimEnd(),
                StartOffset = pendingStartOffset,
                EndOffset = sections.Count > 0 ? sections[^1].EndOffset : 0
            });
        }

        return merged;
    }

    private static string GetOverlapText(string content, int overlapChars)
    {
        if (string.IsNullOrEmpty(content) || content.Length <= overlapChars)
        {
            return string.Empty;
        }

        // Find a good break point (end of a sentence or paragraph)
        var startIdx = content.Length - overlapChars;
        var breakIdx = content.IndexOf('\n', startIdx);
        if (breakIdx >= 0 && breakIdx < content.Length)
        {
            startIdx = breakIdx + 1;
        }

        return content.Substring(startIdx);
    }

    private static int EstimateTokens(string text)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        return text.Length / 4;
    }

    /// <summary>
    /// Computes the SHA-256 hash of the input string as a lowercase hex string.
    /// Used for ContentHash and ChunkUid.
    /// </summary>
    private static string Sha256Hex(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        var sb = new StringBuilder(bytes.Length * 2);
        foreach (var b in bytes)
        {
            sb.Append(b.ToString("x2"));
        }
        return sb.ToString();
    }

    private static string BuildMetadata(List<string> headingPath, string sourceType)
    {
        var metadata = new
        {
            heading_path = headingPath,
            source_type = sourceType
        };
        return JsonSerializer.Serialize(metadata);
    }

    private static string DetermineSourceType(Document document)
    {
        // Infer source type from document metadata; default to "text"
        return "text";
    }

    private class MarkdownSection
    {
        public List<string> HeadingPath { get; set; } = new();
        public string Content { get; set; } = string.Empty;
        public string MarkdownContent { get; set; } = string.Empty;
        public int StartOffset { get; set; }
        public int EndOffset { get; set; }
    }

    private class RawChunk
    {
        public List<string> HeadingPath { get; set; } = new();
        public string Content { get; set; } = string.Empty;
        public string MarkdownContent { get; set; } = string.Empty;
        public int StartOffset { get; set; }
        public int EndOffset { get; set; }
    }
}
