using KnowledgeEngine.Application.Interfaces;

namespace KnowledgeEngine.Infrastructure.Processing;

/// <summary>
/// Manages system and user prompts for document AI summary analysis.
/// Implements the §15.4 prompt specification (summary_v1).
/// Extracted from DocumentPipeline to allow reuse and independent versioning.
/// </summary>
public class SummaryPromptManager : ISummaryPromptManager
{
    private const string PromptVersion = "summary_v1";

    private static readonly string SystemPrompt =
        "你是一个知识资产整理助手。请基于用户提供的文档内容，生成结构化中文摘要。\n\n" +
        "要求：\n" +
        "1. 必须忠实于原文，不得编造原文没有的信息。\n" +
        "2. 不要输出 Markdown，不要输出解释，只输出严格 JSON。\n" +
        "3. summary 使用中文，长度 300-800 字。\n" +
        "4. one_sentence_conclusion 用一句话概括最重要结论。\n" +
        "5. key_points 输出 3-8 条，每条包含 text、importance、evidence。\n" +
        "6. value_score 为 0-100 的整数，表示该资料对长期知识资产的价值。\n" +
        "7. recommended_tags 输出 3-8 个标签。\n" +
        "8. should_deep_process 表示是否值得进入后续分块、向量化和深度分析。\n\n" +
        "评分参考：\n" +
        "- 90-100：高价值资料，适合长期保存和深度研究。\n" +
        "- 70-89：有明显价值，适合进入知识库。\n" +
        "- 50-69：一般资料，可保存但不必优先处理。\n" +
        "- 30-49：低价值资料，可能只是资讯噪音。\n" +
        "- 0-29：无明显知识价值或解析质量很差。";

    /// <inheritdoc />
    public string GetSystemPrompt() => SystemPrompt;

    /// <inheritdoc />
    public string GetPromptVersion() => PromptVersion;

    /// <inheritdoc />
    public string GetUserPrompt(string title, string contentText, string sourceType)
    {
        // Truncate content if too long to avoid exceeding token limits
        const int maxContentLength = 12000;
        var truncatedContent = contentText.Length > maxContentLength
            ? contentText.Substring(0, maxContentLength) + "\n\n[... 内容已截断 ...]"
            : contentText;

        var titleLine = string.IsNullOrWhiteSpace(title)
            ? "(无标题)"
            : title;
        var typeLine = string.IsNullOrWhiteSpace(sourceType)
            ? "(未知)"
            : sourceType;

        return $@"请严格返回如下 JSON：
{{
  ""summary"": """",
  ""one_sentence_conclusion"": """",
  ""key_points"": [
    {{""text"": """", ""importance"": ""high|medium|low"", ""evidence"": """"}}
  ],
  ""value_score"": 0,
  ""value_score_reason"": """",
  ""recommended_tags"": [],
  ""should_deep_process"": true
}}

文档标题：{titleLine}
资料类型：{typeLine}
正文内容：
{truncatedContent}";
    }
}
