using KnowledgeEngine.Application.Interfaces;

namespace KnowledgeEngine.Infrastructure.Processing;

/// <summary>
/// Manages system and user prompts for document AI summary analysis.
/// Implements the §15.4 prompt specification (summary_v1).
/// Extracted from DocumentPipeline to allow reuse and independent versioning.
/// </summary>
public class SummaryPromptManager : ISummaryPromptManager
{
    private const string PromptVersion = "summary_v2";

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
        "8. business_signals 提取商业模式、市场、用户、增长、竞争、成本或收入等信号。\n" +
        "9. technical_signals 提取架构、协议、模型、算法、工程实践、性能或安全等信号。\n" +
        "10. risks 提取文中明确或可以由原文直接推导的风险、约束和不确定性。\n" +
        "11. opportunities 提取可行动的产品、市场、研究或技术机会。\n" +
        "12. reusable_materials 提取可直接复用的观点、方法、框架、数据、案例或文案。\n" +
        "13. 上述五个数组必须始终返回；每项用一条完整、具体的中文短句。确实没有时才返回空数组，不得因资料类型而省略字段。\n" +
        "14. should_deep_process 表示是否值得进入后续分块、向量化和深度分析。\n\n" +
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
  ""business_signals"": [""具体商业信号""],
  ""technical_signals"": [""具体技术信号""],
  ""risks"": [""具体风险或约束""],
  ""opportunities"": [""具体机会""],
  ""reusable_materials"": [""可直接复用的内容或方法""],
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
