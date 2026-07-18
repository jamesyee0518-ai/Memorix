namespace KnowledgeEngine.Infrastructure.Reports;

/// <summary>
/// 报告计划（对应设计文档 §7.2）。
/// 报告计划用于让生成过程可解释、可调试，描述报告的目标、结构与检索约束。
/// </summary>
public class ReportPlan
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string ReportType { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Goal { get; set; } = string.Empty;
    public List<ReportSectionPlan> Sections { get; set; } = new();
    public Dictionary<string, object> Filters { get; set; } = new();
    public int MaxSources { get; set; } = 30;
    public int MaxChunks { get; set; } = 80;
    public string? ModelConfig { get; set; }
}

/// <summary>
/// 报告分节计划。每一节描述其用途、期望证据类型与最少引用数。
/// </summary>
public class ReportSectionPlan
{
    public string Key { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Purpose { get; set; } = string.Empty;
    public string ExpectedEvidenceType { get; set; } = "facts"; // facts|trends|risks|opportunities|summary
    public int MinCitations { get; set; } = 1;
}

/// <summary>
/// 根据报告类型生成 <see cref="ReportPlan"/>。
/// 不同报告类型（日报 / 周报 / 专题报告）拥有不同的分节结构与检索约束。
/// </summary>
public class ReportPlanBuilder
{
    /// <summary>
    /// 根据报告类型生成报告计划。
    /// </summary>
    /// <param name="reportType">报告类型：daily / weekly / topic</param>
    /// <param name="title">报告标题（可为空，召回阶段确定后回填）</param>
    /// <param name="query">研究问题（专题报告使用）</param>
    /// <param name="startDate">时间范围起始</param>
    /// <param name="endDate">时间范围结束</param>
    /// <param name="topicId">所属专题</param>
    /// <param name="tagIds">标签过滤（可选）</param>
    /// <param name="entityIds">实体过滤（可选）</param>
    /// <param name="depth">报告深度：brief / standard / deep（可为空，按 standard 处理）</param>
    /// <param name="language">输出语言，默认 zh-CN</param>
    public ReportPlan BuildPlan(
        string reportType,
        string? title,
        string? query,
        DateTime? startDate,
        DateTime? endDate,
        Guid? topicId,
        List<string>? tagIds = null,
        List<string>? entityIds = null,
        string? depth = null,
        string language = "zh-CN")
    {
        var plan = new ReportPlan
        {
            ReportType = reportType,
            Title = title ?? string.Empty,
            Goal = BuildGoal(reportType, query),
            Sections = BuildSections(reportType),
            Filters = BuildFilters(reportType, startDate, endDate, topicId, tagIds, entityIds, language),
            MaxSources = GetMaxSources(depth),
            MaxChunks = GetMaxChunks(depth),
            ModelConfig = null
        };

        return plan;
    }

    // ===== 分节定义 =====

    private static List<ReportSectionPlan> BuildSections(string reportType)
    {
        return reportType.ToLowerInvariant() switch
        {
            "daily" => BuildDailySections(),
            "weekly" => BuildWeeklySections(),
            "topic" => BuildTopicSections(),
            _ => BuildDailySections()
        };
    }

    /// <summary>
    /// 日报分节：summary, key_info, trends, risks, questions, sources
    /// </summary>
    private static List<ReportSectionPlan> BuildDailySections()
    {
        return
        [
            new ReportSectionPlan
            {
                Key = "summary",
                Title = "摘要",
                Purpose = "用一段话概括当日最重要的信息",
                ExpectedEvidenceType = "summary",
                MinCitations = 1
            },
            new ReportSectionPlan
            {
                Key = "key_info",
                Title = "今日最重要信息",
                Purpose = "列出今日最重要的 3-5 条信息",
                ExpectedEvidenceType = "facts",
                MinCitations = 3
            },
            new ReportSectionPlan
            {
                Key = "trends",
                Title = "关键趋势",
                Purpose = "识别当日信息中体现的关键趋势",
                ExpectedEvidenceType = "trends",
                MinCitations = 1
            },
            new ReportSectionPlan
            {
                Key = "risks",
                Title = "风险与不确定性",
                Purpose = "提示当日信息中的潜在风险与不确定性",
                ExpectedEvidenceType = "risks",
                MinCitations = 1
            },
            new ReportSectionPlan
            {
                Key = "questions",
                Title = "值得后续深挖的问题",
                Purpose = "列出值得后续深入挖掘的问题",
                ExpectedEvidenceType = "facts",
                MinCitations = 0
            },
            new ReportSectionPlan
            {
                Key = "sources",
                Title = "来源列表",
                Purpose = "列出所有引用来源",
                ExpectedEvidenceType = "summary",
                MinCitations = 0
            }
        ];
    }

    /// <summary>
    /// 周报分节：conclusion, changes, trends, timeline, high_freq_entities,
    /// opportunities, risks, next_week, sources
    /// </summary>
    private static List<ReportSectionPlan> BuildWeeklySections()
    {
        return
        [
            new ReportSectionPlan
            {
                Key = "conclusion",
                Title = "一句话结论",
                Purpose = "用一句话总结本周核心结论",
                ExpectedEvidenceType = "summary",
                MinCitations = 1
            },
            new ReportSectionPlan
            {
                Key = "changes",
                Title = "本周核心变化",
                Purpose = "梳理本周发生的核心变化",
                ExpectedEvidenceType = "facts",
                MinCitations = 3
            },
            new ReportSectionPlan
            {
                Key = "trends",
                Title = "重要趋势",
                Purpose = "分析本周的重要趋势",
                ExpectedEvidenceType = "trends",
                MinCitations = 1
            },
            new ReportSectionPlan
            {
                Key = "timeline",
                Title = "关键事件时间线",
                Purpose = "按时间顺序列出本周关键事件",
                ExpectedEvidenceType = "facts",
                MinCitations = 1
            },
            new ReportSectionPlan
            {
                Key = "high_freq_entities",
                Title = "高频公司/产品/技术/人物",
                Purpose = "提取本周高频出现的公司、产品、技术、人物",
                ExpectedEvidenceType = "facts",
                MinCitations = 0
            },
            new ReportSectionPlan
            {
                Key = "opportunities",
                Title = "机会信号",
                Purpose = "识别本周的机会信号",
                ExpectedEvidenceType = "opportunities",
                MinCitations = 1
            },
            new ReportSectionPlan
            {
                Key = "risks",
                Title = "风险信号",
                Purpose = "识别本周的风险信号",
                ExpectedEvidenceType = "risks",
                MinCitations = 1
            },
            new ReportSectionPlan
            {
                Key = "next_week",
                Title = "下周需要关注的问题",
                Purpose = "提出下周需要关注的问题",
                ExpectedEvidenceType = "summary",
                MinCitations = 0
            },
            new ReportSectionPlan
            {
                Key = "sources",
                Title = "来源列表",
                Purpose = "列出所有引用来源",
                ExpectedEvidenceType = "summary",
                MinCitations = 0
            }
        ];
    }

    /// <summary>
    /// 专题报告分节：summary, conclusion, background, key_facts, viewpoints,
    /// evidence, opportunities, risks, uncertainty, actions, questions, sources
    /// </summary>
    private static List<ReportSectionPlan> BuildTopicSections()
    {
        return
        [
            new ReportSectionPlan
            {
                Key = "summary",
                Title = "摘要",
                Purpose = "概括报告核心内容",
                ExpectedEvidenceType = "summary",
                MinCitations = 1
            },
            new ReportSectionPlan
            {
                Key = "conclusion",
                Title = "一句话结论",
                Purpose = "用一句话给出明确结论",
                ExpectedEvidenceType = "summary",
                MinCitations = 1
            },
            new ReportSectionPlan
            {
                Key = "background",
                Title = "背景与问题定义",
                Purpose = "阐述问题背景与定义",
                ExpectedEvidenceType = "facts",
                MinCitations = 1
            },
            new ReportSectionPlan
            {
                Key = "key_facts",
                Title = "关键事实",
                Purpose = "列出与问题相关的关键事实",
                ExpectedEvidenceType = "facts",
                MinCitations = 3
            },
            new ReportSectionPlan
            {
                Key = "viewpoints",
                Title = "主要观点",
                Purpose = "梳理不同视角的主要观点",
                ExpectedEvidenceType = "facts",
                MinCitations = 2
            },
            new ReportSectionPlan
            {
                Key = "evidence",
                Title = "证据与来源分析",
                Purpose = "分析证据及其来源可靠性",
                ExpectedEvidenceType = "facts",
                MinCitations = 3
            },
            new ReportSectionPlan
            {
                Key = "opportunities",
                Title = "机会",
                Purpose = "识别潜在机会",
                ExpectedEvidenceType = "opportunities",
                MinCitations = 1
            },
            new ReportSectionPlan
            {
                Key = "risks",
                Title = "风险",
                Purpose = "识别潜在风险",
                ExpectedEvidenceType = "risks",
                MinCitations = 1
            },
            new ReportSectionPlan
            {
                Key = "uncertainty",
                Title = "不确定性",
                Purpose = "说明尚存的不确定性",
                ExpectedEvidenceType = "risks",
                MinCitations = 0
            },
            new ReportSectionPlan
            {
                Key = "actions",
                Title = "建议行动",
                Purpose = "给出可执行的建议行动",
                ExpectedEvidenceType = "opportunities",
                MinCitations = 0
            },
            new ReportSectionPlan
            {
                Key = "questions",
                Title = "后续研究问题",
                Purpose = "提出后续研究方向的问题",
                ExpectedEvidenceType = "facts",
                MinCitations = 0
            },
            new ReportSectionPlan
            {
                Key = "sources",
                Title = "来源列表",
                Purpose = "列出所有引用来源",
                ExpectedEvidenceType = "summary",
                MinCitations = 0
            }
        ];
    }

    // ===== 目标 =====

    private static string BuildGoal(string reportType, string? query)
    {
        return reportType.ToLowerInvariant() switch
        {
            "daily" => "总结指定日期内新增资料的核心信息、趋势与风险，帮助用户快速掌握当日知识动态。",
            "weekly" => "回顾指定周内资料的核心变化、趋势、机会与风险，帮助用户把握一周知识脉络并规划下周关注重点。",
            "topic" => string.IsNullOrWhiteSpace(query)
                ? "围绕用户提出的研究问题，基于资料进行深度分析，给出结论、证据、机会、风险与建议行动。"
                : $"围绕研究问题「{query}」，基于资料进行深度分析，给出结论、证据、机会、风险与建议行动。",
            _ => "基于资料生成结构化报告。"
        };
    }

    // ===== 检索过滤条件 =====

    private static Dictionary<string, object> BuildFilters(
        string reportType,
        DateTime? startDate,
        DateTime? endDate,
        Guid? topicId,
        List<string>? tagIds,
        List<string>? entityIds,
        string language)
    {
        var filters = new Dictionary<string, object>
        {
            ["reportType"] = reportType,
            ["language"] = language
        };

        if (startDate.HasValue)
        {
            filters["startDate"] = startDate.Value;
        }
        if (endDate.HasValue)
        {
            filters["endDate"] = endDate.Value;
        }
        if (topicId.HasValue)
        {
            filters["topicId"] = topicId.Value;
        }
        if (tagIds is { Count: > 0 })
        {
            filters["tagIds"] = tagIds;
        }
        if (entityIds is { Count: > 0 })
        {
            filters["entityIds"] = entityIds;
        }

        return filters;
    }

    // ===== 深度对应的检索上限 =====

    private static int GetMaxSources(string? depth)
    {
        return (depth ?? "standard").ToLowerInvariant() switch
        {
            "brief" => 15,
            "deep" => 50,
            _ => 30 // standard
        };
    }

    private static int GetMaxChunks(string? depth)
    {
        return (depth ?? "standard").ToLowerInvariant() switch
        {
            "brief" => 40,
            "deep" => 150,
            _ => 80 // standard
        };
    }
}
