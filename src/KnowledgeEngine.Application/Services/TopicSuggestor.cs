using KnowledgeEngine.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace KnowledgeEngine.Application.Services;

/// <summary>
/// Suggests topics and titles for inbox items (§17.6).
/// Uses simple keyword-based matching for now (no AI required).
/// Can be upgraded to use LLM-based suggestions in a later phase.
/// </summary>
public class TopicSuggestor
{
    private readonly IKnowledgeRepository _repo;
    private readonly ILogger<TopicSuggestor> _logger;

    // Keyword-to-topic mapping for common knowledge domains
    private static readonly Dictionary<string, string> KeywordTopics = new(StringComparer.OrdinalIgnoreCase)
    {
        { "AI", "人工智能" },
        { "人工智能", "人工智能" },
        { "machine learning", "人工智能" },
        { "机器学习", "人工智能" },
        { "deep learning", "人工智能" },
        { "深度学习", "人工智能" },
        { "LLM", "人工智能" },
        { "GPT", "人工智能" },
        { "区块链", "区块链" },
        { "blockchain", "区块链" },
        { "crypto", "区块链" },
        { "加密货币", "区块链" },
        { "数据库", "数据库" },
        { "database", "数据库" },
        { "SQL", "数据库" },
        { "前端", "前端开发" },
        { "frontend", "前端开发" },
        { "React", "前端开发" },
        { "Vue", "前端开发" },
        { "后端", "后端开发" },
        { "backend", "后端开发" },
        { "API", "后端开发" },
        { "云计算", "云计算" },
        { "cloud", "云计算" },
        { "AWS", "云计算" },
        { "Azure", "云计算" },
        { "产品", "产品设计" },
        { "product", "产品设计" },
        { "设计", "产品设计" },
        { "UX", "产品设计" },
        { "市场", "市场营销" },
        { "marketing", "市场营销" },
        { "营销", "市场营销" },
        { "金融", "金融" },
        { "finance", "金融" },
        { "投资", "金融" },
        { "医疗", "医疗健康" },
        { "health", "医疗健康" },
        { "教育", "教育" },
        { "education", "教育" },
    };

    public TopicSuggestor(IKnowledgeRepository repo, ILogger<TopicSuggestor> logger)
    {
        _repo = repo;
        _logger = logger;
    }

    /// <summary>
    /// Suggests a topic name based on keyword matching in the title and content.
    /// Returns the best-matching topic name, or null if no match is found.
    /// </summary>
    public async Task<string?> SuggestTopicAsync(string workspaceId, string? title, string? content, CancellationToken ct = default)
    {
        var combinedText = $"{title} {content}".Trim();
        if (string.IsNullOrWhiteSpace(combinedText))
            return null;

        // Try to match keywords in the combined text
        foreach (var (keyword, topicName) in KeywordTopics)
        {
            if (combinedText.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogDebug("Topic suggested: {Topic} (matched keyword: {Keyword})", topicName, keyword);

                // Try to find an existing topic with this name in the workspace
                var topics = await _repo.ListTopicsAsync(workspaceId, ct);
                var existing = topics.FirstOrDefault(t =>
                    t.Name.Equals(topicName, StringComparison.OrdinalIgnoreCase));
                if (existing != null)
                {
                    return existing.Name;
                }

                return topicName;
            }
        }

        return null;
    }

    /// <summary>
    /// Suggests a title based on the content.
    /// Returns the first line if it's short enough, otherwise the first 50 characters.
    /// </summary>
    public string? SuggestTitle(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return null;

        var trimmed = content.Trim();

        // Try to use the first line as the title
        var firstLineEnd = trimmed.IndexOfAny(new[] { '\n', '\r' });
        if (firstLineEnd > 0)
        {
            var firstLine = trimmed.Substring(0, firstLineEnd).Trim();
            if (firstLine.Length <= 100)
                return firstLine;
        }

        // Fall back to first 50 characters
        return trimmed.Length > 50 ? trimmed.Substring(0, 50) + "..." : trimmed;
    }
}
