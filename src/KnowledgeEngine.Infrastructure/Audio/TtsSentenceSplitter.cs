using Microsoft.Extensions.Logging;

namespace KnowledgeEngine.Infrastructure.Audio;

/// <summary>
/// Splits text into 20–80 character chunks at sentence boundaries for streaming TTS.
/// This reduces time-to-first-audio by allowing the TTS engine to begin synthesizing
/// the first sentence while subsequent sentences are still being processed.
/// Splits on both Chinese (。！？) and English (.!?) sentence-ending punctuation,
/// and respects configurable minimum and maximum chunk lengths.
/// </summary>
public class TtsSentenceSplitter
{
    /// <summary>
    /// Maximum chunk length in characters. Sentences longer than this are
    /// further split at comma/semicolon boundaries or by hard character count.
    /// </summary>
    public const int DefaultMaxChunkLength = 80;

    /// <summary>
    /// Minimum chunk length in characters. Sentences shorter than this are
    /// merged with the following sentence when possible.
    /// </summary>
    public const int DefaultMinChunkLength = 20;

    private static readonly char[] SentenceEndings = ['。', '！', '？', '.', '!', '?'];
    private static readonly char[] SubClauseBreaks = ['，', '；', ',', ';', '、', '\n', '\r'];

    private readonly int _maxChunkLength;
    private readonly int _minChunkLength;
    private readonly ILogger<TtsSentenceSplitter> _logger;

    /// <summary>
    /// Creates a new <see cref="TtsSentenceSplitter"/> with default length bounds.
    /// </summary>
    /// <param name="logger">Logger for diagnostics.</param>
    public TtsSentenceSplitter(ILogger<TtsSentenceSplitter> logger)
        : this(DefaultMaxChunkLength, DefaultMinChunkLength, logger)
    {
    }

    /// <summary>
    /// Creates a new <see cref="TtsSentenceSplitter"/> with custom length bounds.
    /// </summary>
    /// <param name="maxChunkLength">Maximum characters per chunk.</param>
    /// <param name="minChunkLength">Minimum characters per chunk.</param>
    /// <param name="logger">Logger for diagnostics.</param>
    public TtsSentenceSplitter(int maxChunkLength, int minChunkLength, ILogger<TtsSentenceSplitter> logger)
    {
        _maxChunkLength = maxChunkLength > 0 ? maxChunkLength : DefaultMaxChunkLength;
        _minChunkLength = minChunkLength > 0 ? minChunkLength : DefaultMinChunkLength;
        _logger = logger;
    }

    /// <summary>
    /// Splits the input text into chunks suitable for streaming TTS.
    /// Each chunk is between <see cref="_minChunkLength"/> and <see cref="_maxChunkLength"/>
    /// characters where possible, breaking at sentence boundaries.
    /// </summary>
    /// <param name="text">The text to split.</param>
    /// <returns>A list of text chunks.</returns>
    public List<string> Split(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        text = text.Trim();
        var rawSentences = SplitAtSentenceEndings(text);
        var merged = MergeShortSentences(rawSentences);
        var result = new List<string>();

        foreach (var sentence in merged)
        {
            if (sentence.Length <= _maxChunkLength)
            {
                result.Add(sentence);
            }
            else
            {
                // Sentence is too long — split at sub-clause boundaries.
                var subChunks = SplitAtSubClauseBreaks(sentence);
                result.AddRange(subChunks);
            }
        }

        _logger.LogDebug(
            "Split text ({TotalChars} chars) into {ChunkCount} chunks",
            text.Length, result.Count);

        return result;
    }

    /// <summary>
    /// Splits text at sentence-ending punctuation (。！？.!?),
    /// preserving the punctuation with the preceding chunk.
    /// </summary>
    private List<string> SplitAtSentenceEndings(string text)
    {
        var sentences = new List<string>();
        var current = new System.Text.StringBuilder();

        for (var i = 0; i < text.Length; i++)
        {
            current.Append(text[i]);

            if (Array.IndexOf(SentenceEndings, text[i]) >= 0)
            {
                // Include any trailing closing quotes/brackets after the sentence end.
                while (i + 1 < text.Length &&
                       (text[i + 1] == '"' || text[i + 1] == '」' ||
                        text[i + 1] == '」' || text[i + 1] == '』' ||
                        text[i + 1] == ')' || text[i + 1] == '）'))
                {
                    i++;
                    current.Append(text[i]);
                }

                var chunk = current.ToString().Trim();
                if (!string.IsNullOrEmpty(chunk))
                {
                    sentences.Add(chunk);
                }
                current.Clear();
            }
        }

        // Add any remaining text as a final chunk.
        var remaining = current.ToString().Trim();
        if (!string.IsNullOrEmpty(remaining))
        {
            sentences.Add(remaining);
        }

        return sentences;
    }

    /// <summary>
    /// Merges consecutive sentences that are shorter than the minimum chunk length
    /// into a single chunk, respecting the maximum chunk length.
    /// </summary>
    private List<string> MergeShortSentences(List<string> sentences)
    {
        if (sentences.Count == 0)
            return sentences;

        var merged = new List<string>();
        var accumulator = new System.Text.StringBuilder();

        foreach (var sentence in sentences)
        {
            if (accumulator.Length > 0 &&
                accumulator.Length + sentence.Length + 1 > _maxChunkLength)
            {
                // Adding this sentence would exceed max — flush the accumulator.
                merged.Add(accumulator.ToString().Trim());
                accumulator.Clear();
            }

            if (accumulator.Length > 0)
            {
                accumulator.Append(' ');
            }
            accumulator.Append(sentence);

            if (accumulator.Length >= _minChunkLength)
            {
                // Accumulator has reached the minimum — flush it.
                merged.Add(accumulator.ToString().Trim());
                accumulator.Clear();
            }
        }

        // Flush any remaining text.
        if (accumulator.Length > 0)
        {
            merged.Add(accumulator.ToString().Trim());
        }

        return merged;
    }

    /// <summary>
    /// Splits a long sentence at comma/semicolon/newline boundaries.
    /// Each sub-chunk respects the maximum chunk length.
    /// If a sub-clause is still too long, it is hard-split at the character limit.
    /// </summary>
    private List<string> SplitAtSubClauseBreaks(string sentence)
    {
        var chunks = new List<string>();
        var current = new System.Text.StringBuilder();

        for (var i = 0; i < sentence.Length; i++)
        {
            current.Append(sentence[i]);

            if (Array.IndexOf(SubClauseBreaks, sentence[i]) >= 0 &&
                current.Length >= _minChunkLength)
            {
                var chunk = current.ToString().Trim();
                if (!string.IsNullOrEmpty(chunk))
                {
                    chunks.Add(chunk);
                }
                current.Clear();
            }
            else if (current.Length >= _maxChunkLength)
            {
                // Hard split at max length — look back for a space to break cleanly.
                var breakPos = current.Length;
                for (var j = current.Length - 1; j > _minChunkLength; j--)
                {
                    if (current[j - 1] == ' ')
                    {
                        breakPos = j;
                        break;
                    }
                }

                var chunk = current.ToString(0, breakPos).Trim();
                if (!string.IsNullOrEmpty(chunk))
                {
                    chunks.Add(chunk);
                }

                // Carry over the remainder.
                var remainder = current.ToString(breakPos, current.Length - breakPos);
                current.Clear();
                current.Append(remainder);
            }
        }

        var last = current.ToString().Trim();
        if (!string.IsNullOrEmpty(last))
        {
            chunks.Add(last);
        }

        return chunks;
    }
}
