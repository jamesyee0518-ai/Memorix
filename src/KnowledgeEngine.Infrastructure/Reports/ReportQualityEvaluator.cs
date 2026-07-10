using System.Globalization;
using System.Text.RegularExpressions;

namespace KnowledgeEngine.Infrastructure.Reports;

/// <summary>
/// Evaluates the quality of a generated report across seven weighted dimensions
/// according to design document §14 (报告质量评估).
///
/// Dimensions and weights:
///   - 结构完整度 (Structural Completeness)        20%
///   - 引用覆盖率 (Citation Coverage)              25%
///   - 来源多样性 (Source Diversity)               15%
///   - 信息密度 (Information Density)              15%
///   - 时间范围覆盖 (Time Range Coverage)          10%
///   - 风险/不确定性表达 (Risk/Uncertainty Expr.)  10%
///   - 格式规范 (Format Compliance)                 5%
/// </summary>
public class ReportQualityEvaluator
{
    // Dimension weights (must sum to 1.0)
    private const double WeightStructure = 0.20;
    private const double WeightCitation = 0.25;
    private const double WeightDiversity = 0.15;
    private const double WeightDensity = 0.15;
    private const double WeightTimeRange = 0.10;
    private const double WeightRisk = 0.10;
    private const double WeightFormat = 0.05;

    // Matches numeric citation markers like [1], [12], [CIT-1] -> we focus on [n]
    private static readonly Regex CitationMarkerRegex = new(
        @"\[(\d{1,3})\]",
        RegexOptions.Compiled);

    // Matches Markdown ATX headings: ## Title, # Title, ### Title
    private static readonly Regex HeadingRegex = new(
        @"^(#{1,6})\s+(.+?)\s*$",
        RegexOptions.Compiled);

    /// <summary>
    /// Evaluates the quality of the given report content.
    /// </summary>
    /// <param name="contentMarkdown">The report body in Markdown.</param>
    /// <param name="sourceCount">Number of distinct source documents referenced.</param>
    /// <param name="citationCount">Number of system-generated citation entries.</param>
    /// <param name="reportType">Report type: daily / weekly / topic.</param>
    /// <param name="startDate">Report coverage start date (nullable).</param>
    /// <param name="endDate">Report coverage end date (nullable).</param>
    public QualityEvaluation Evaluate(
        string contentMarkdown,
        int sourceCount,
        int citationCount,
        string reportType,
        DateTime? startDate,
        DateTime? endDate)
    {
        var content = contentMarkdown ?? string.Empty;
        var type = (reportType ?? string.Empty).ToLowerInvariant();

        var issues = new List<string>();
        var dimensions = new Dictionary<string, int>();

        // ----- 1. 结构完整度 (Structural Completeness) -----
        var structureScore = EvaluateStructure(content, type, issues);
        dimensions["structure"] = structureScore;

        // ----- 2. 引用覆盖率 (Citation Coverage) -----
        var (coverageScore, citationCoverage, evidenceCount) = EvaluateCitationCoverage(content, citationCount, issues);
        dimensions["citation_coverage"] = coverageScore;

        // ----- 3. 来源多样性 (Source Diversity) -----
        var diversityScore = EvaluateSourceDiversity(sourceCount, issues);
        dimensions["source_diversity"] = diversityScore;

        // ----- 4. 信息密度 (Information Density) -----
        var densityScore = EvaluateInformationDensity(content, issues);
        dimensions["information_density"] = densityScore;

        // ----- 5. 时间范围覆盖 (Time Range Coverage) -----
        var timeRangeScore = EvaluateTimeRange(content, type, startDate, endDate, issues);
        dimensions["time_range"] = timeRangeScore;

        // ----- 6. 风险/不确定性表达 (Risk/Uncertainty Expression) -----
        var riskScore = EvaluateRiskExpression(content, issues);
        dimensions["risk_expression"] = riskScore;

        // ----- 7. 格式规范 (Format Compliance) -----
        var formatScore = EvaluateFormat(content, citationCount, issues);
        dimensions["format"] = formatScore;

        // ----- Weighted total -----
        var qualityScore = (int)Math.Round(
            structureScore * WeightStructure +
            coverageScore * WeightCitation +
            diversityScore * WeightDiversity +
            densityScore * WeightDensity +
            timeRangeScore * WeightTimeRange +
            riskScore * WeightRisk +
            formatScore * WeightFormat);

        if (qualityScore < 0) qualityScore = 0;
        if (qualityScore > 100) qualityScore = 100;

        if (qualityScore < 60)
        {
            issues.Add($"质量评分 {qualityScore} 低于阈值 60，建议重新生成。");
        }

        return new QualityEvaluation
        {
            QualityScore = qualityScore,
            CitationCoverage = citationCoverage,
            EvidenceCount = evidenceCount,
            DimensionScores = dimensions,
            Issues = issues
        };
    }

    // ====================================================================
    // Dimension 1: 结构完整度 (Structural Completeness) - 20%
    // Checks whether the report contains the sections required by the
    // template (summary, key findings, trends, risks, opportunities,
    // recommendations, sources).
    // ====================================================================

    private static int EvaluateStructure(string content, string reportType, List<string> issues)
    {
        // Concept groups: each concept is considered present if any heading
        // matches one of its keyword variants.
        var conceptGroups = new (string Concept, string[] Keywords)[]
        {
            ("summary",          new[] { "摘要", "概述", "概况", "本周概述", "总结", "概述" }),
            ("key_findings",     new[] { "关键事实", "重要发现", "核心发现", "核心主题", "重要进展", "关键要点", "核心进展" }),
            ("trends",           new[] { "趋势", "信号", "趋势分析", "趋势与信号" }),
            ("risks",            new[] { "风险", "风险与机遇", "风险与不确定性" }),
            ("opportunities",    new[] { "机会", "机遇", "风险与机遇" }),
            ("recommendations",  new[] { "建议", "建议关注", "建议行动", "下周建议", "结论与建议", "行动建议", "后续" }),
            ("sources",          new[] { "参考来源", "来源", "参考资料", "引用来源" })
        };

        var headings = ExtractHeadings(content);
        var headingText = headings.Select(h => h.text).ToList();

        int present = 0;
        foreach (var group in conceptGroups)
        {
            if (headingText.Any(h => group.Keywords.Any(k => h.Contains(k, StringComparison.OrdinalIgnoreCase))))
            {
                present++;
            }
        }

        var total = conceptGroups.Length;
        var score = (int)Math.Round((double)present / total * 100);

        if (present < total)
        {
            var missing = conceptGroups
                .Where(g => !headingText.Any(h => g.Keywords.Any(k => h.Contains(k, StringComparison.OrdinalIgnoreCase))))
                .Select(g => g.Concept)
                .ToList();
            if (missing.Count > 0)
            {
                issues.Add($"结构完整度：缺少章节概念 {string.Join("、", missing)}。");
            }
        }

        return score;
    }

    // ====================================================================
    // Dimension 2: 引用覆盖率 (Citation Coverage) - 25%
    // citation_coverage = 带引用的关键段落数量 / 关键段落总数量
    // Key paragraphs belong to sections about: 关键事实, 趋势判断,
    // 风险判断, 机会判断, 建议行动.
    // ====================================================================

    private static (int score, double coverage, int evidenceCount) EvaluateCitationCoverage(
        string content,
        int citationCount,
        List<string> issues)
    {
        // Keywords that identify "key paragraph" sections
        var keySectionKeywords = new[]
        {
            "关键事实", "重要发现", "核心发现", "核心主题", "重要进展", "关键要点",
            "趋势", "信号",
            "风险",
            "机会", "机遇",
            "建议", "结论", "行动"
        };

        var sections = ParseSections(content);

        // Collect paragraphs (non-heading text blocks) from key sections
        var keyParagraphs = new List<string>();
        foreach (var section in sections)
        {
            if (keySectionKeywords.Any(k => section.Heading.Contains(k, StringComparison.OrdinalIgnoreCase)))
            {
                foreach (var para in section.Paragraphs)
                {
                    if (!string.IsNullOrWhiteSpace(para))
                    {
                        keyParagraphs.Add(para);
                    }
                }
            }
        }

        // Distinct citation indices referenced in the whole content
        var citedIndices = ExtractCitationIndices(content);
        var evidenceCount = citedIndices.Count;

        if (keyParagraphs.Count == 0)
        {
            issues.Add("引用覆盖率：未找到关键段落（关键事实/趋势/风险/机会/建议），无法计算引用覆盖率。");
            return (0, 0d, evidenceCount);
        }

        int citedKeyParagraphs = keyParagraphs.Count(p => CitationMarkerRegex.IsMatch(p));
        double coverage = (double)citedKeyParagraphs / keyParagraphs.Count;

        var score = (int)Math.Round(coverage * 100);

        if (coverage < 0.5)
        {
            issues.Add($"引用覆盖率 {coverage:P0} 偏低，关键段落引用不足。");
        }

        if (citationCount > 0 && evidenceCount == 0)
        {
            issues.Add("报告内容中未检测到任何引用标记 [n]。");
        }

        return (score, coverage, evidenceCount);
    }

    // ====================================================================
    // Dimension 3: 来源多样性 (Source Diversity) - 15%
    // 来源文档数 > 3 得满分；1 个文档得 30%。
    // Linear interpolation between 1 (30) and 4 (100).
    // ====================================================================

    private static int EvaluateSourceDiversity(int sourceCount, List<string> issues)
    {
        int score;
        if (sourceCount <= 0)
        {
            score = 0;
        }
        else if (sourceCount == 1)
        {
            score = 30;
        }
        else if (sourceCount > 3)
        {
            score = 100;
        }
        else
        {
            // Linear: 1 -> 30, 4 -> 100, slope = 70/3
            score = (int)Math.Round(30 + (sourceCount - 1) * (70.0 / 3.0));
        }

        if (sourceCount == 1)
        {
            issues.Add("来源多样性：仅依赖单一文档，来源多样性不足。");
        }
        else if (sourceCount <= 3 && sourceCount > 0)
        {
            issues.Add($"来源多样性：来源文档数 {sourceCount}，建议增加资料来源以提升多样性。");
        }

        return score;
    }

    // ====================================================================
    // Dimension 4: 信息密度 (Information Density) - 15%
    // 报告字数 / 段落数。每段平均字数在 100-500 为满分，
    // 过低或过高扣分。
    // ====================================================================

    private static int EvaluateInformationDensity(string content, List<string> issues)
    {
        var paragraphs = content
            .Split(new[] { "\r\n\r\n", "\n\n" }, StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Trim())
            .Where(p => p.Length > 0 && !IsHeadingLine(p))
            .ToList();

        if (paragraphs.Count == 0)
        {
            issues.Add("信息密度：未检测到正文段落。");
            return 0;
        }

        // Character count excluding whitespace
        var totalChars = paragraphs.Sum(p => p.Count(c => !char.IsWhiteSpace(c)));
        var avg = (double)totalChars / paragraphs.Count;

        int score;
        if (avg >= 100 && avg <= 500)
        {
            score = 100;
        }
        else if (avg < 100)
        {
            // Too sparse: scale linearly from 0..99
            score = (int)Math.Round(avg);
        }
        else
        {
            // Too dense: deduct gradually, 500->100, 750->50, 1000->0
            score = (int)Math.Round(Math.Max(0, 100 - (avg - 500) / 5.0));
        }

        if (avg < 100)
        {
            issues.Add($"信息密度偏低：平均每段 {avg:F0} 字（建议 100-500 字）。");
        }
        else if (avg > 500)
        {
            issues.Add($"信息密度偏高：平均每段 {avg:F0} 字（建议 100-500 字）。");
        }

        return score;
    }

    // ====================================================================
    // Dimension 5: 时间范围覆盖 (Time Range Coverage) - 10%
    // 日报覆盖当天、周报覆盖一周范围内资料。
    // Heuristic: check whether the report content mentions the expected
    // date(s).
    // ====================================================================

    private static int EvaluateTimeRange(
        string content,
        string reportType,
        DateTime? startDate,
        DateTime? endDate,
        List<string> issues)
    {
        // Build a set of date string variants to look for
        var dateVariants = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (startDate.HasValue)
        {
            AddDateVariants(dateVariants, startDate.Value);
        }
        if (endDate.HasValue && (!startDate.HasValue || endDate.Value.Date != startDate.Value.Date))
        {
            AddDateVariants(dateVariants, endDate.Value);
        }

        // Also look for generic "today" markers for daily reports
        var hasTodayMarker = content.Contains("今天", StringComparison.OrdinalIgnoreCase)
                          || content.Contains("今日", StringComparison.OrdinalIgnoreCase);

        // Determine which expected dates were mentioned
        int matched = 0;
        int expected = 0;

        if (reportType == "daily")
        {
            expected = 1;
            if (startDate.HasValue)
            {
                if (dateVariants.Any(d => content.Contains(d, StringComparison.OrdinalIgnoreCase)) || hasTodayMarker)
                {
                    matched = 1;
                }
            }
            else
            {
                // No date constraint; rely on today markers
                matched = hasTodayMarker ? 1 : 0;
                expected = 1;
            }
        }
        else if (reportType == "weekly")
        {
            expected = 2;
            if (startDate.HasValue)
            {
                if (dateVariants.Any(d => content.Contains(d, StringComparison.OrdinalIgnoreCase)))
                {
                    matched++;
                }
            }
            // Check if any date in the weekly range is mentioned
            if (startDate.HasValue && endDate.HasValue)
            {
                if (AnyDateInRangeMentioned(content, startDate.Value, endDate.Value))
                {
                    matched = Math.Max(matched, 1);
                }
            }
        }
        else
        {
            // topic report: time range is less critical
            if (!startDate.HasValue && !endDate.HasValue)
            {
                return 70;
            }
            expected = 1;
            if (dateVariants.Any(d => content.Contains(d, StringComparison.OrdinalIgnoreCase)))
            {
                matched = 1;
            }
        }

        if (expected == 0)
        {
            return 70;
        }

        var score = (int)Math.Round((double)matched / expected * 100);

        if (matched == 0)
        {
            issues.Add("时间范围覆盖：报告内容中未提及预期时间范围内的日期。");
        }

        return score;
    }

    // ====================================================================
    // Dimension 6: 风险/不确定性表达 (Risk/Uncertainty Expression) - 10%
    // 是否包含"风险""不确定""可能"等词汇。
    // ====================================================================

    private static readonly string[] RiskKeywords =
    {
        "风险", "不确定", "可能", "预计", "预期", "或许", "潜在", "估计", "似乎", "大概",
        "尚不明朗", "存疑", "尚未", "有待", " caveat"
    };

    private static int EvaluateRiskExpression(string content, List<string> issues)
    {
        int found = RiskKeywords.Count(k => content.Contains(k, StringComparison.OrdinalIgnoreCase));

        int score;
        if (found == 0)
        {
            score = 0;
        }
        else if (found <= 2)
        {
            score = 60;
        }
        else
        {
            score = 100;
        }

        if (found == 0)
        {
            issues.Add("风险/不确定性表达：报告中未检测到风险或不确定性相关表述。");
        }

        return score;
    }

    // ====================================================================
    // Dimension 7: 格式规范 (Format Compliance) - 5%
    // Markdown 标题层级、引用格式是否规范。
    // ====================================================================

    private static int EvaluateFormat(string content, int citationCount, List<string> issues)
    {
        var headings = ExtractHeadings(content);

        int score = 0;

        // (a) Has a top-level title (#)
        if (headings.Count > 0)
        {
            score += 30;
        }
        else
        {
            issues.Add("格式规范：报告缺少 Markdown 标题。");
        }

        // (b) Has section headings (##)
        if (headings.Any(h => h.level >= 2))
        {
            score += 30;
        }

        // (c) Heading levels are consistent (no skipping, e.g. # -> ###)
        if (headings.Count > 0)
        {
            if (HeadingsConsistent(headings))
            {
                score += 20;
            }
            else
            {
                issues.Add("格式规范：Markdown 标题层级存在跳跃。");
            }
        }
        else
        {
            score += 10; // neutral when no headings (already penalized above)
        }

        // (d) Citation markers use valid [n] format
        if (citationCount > 0)
        {
            var markers = CitationMarkerRegex.Matches(content);
            if (markers.Count > 0)
            {
                score += 20;
            }
            else
            {
                issues.Add("格式规范：系统已生成引用但报告中未使用 [n] 引用标记。");
            }
        }
        else
        {
            score += 20; // no citations expected
        }

        return Math.Min(100, score);
    }

    // ====================================================================
    // Helper methods
    // ====================================================================

    private static List<(int level, string text)> ExtractHeadings(string content)
    {
        var result = new List<(int level, string text)>();
        foreach (var line in content.Split('\n'))
        {
            var match = HeadingRegex.Match(line.Trim());
            if (match.Success)
            {
                var level = match.Groups[1].Value.Length;
                var text = match.Groups[2].Value.Trim();
                result.Add((level, text));
            }
        }
        return result;
    }

    private static bool IsHeadingLine(string line)
    {
        var trimmed = line.TrimStart();
        return trimmed.StartsWith('#');
    }

    private static bool HeadingsConsistent(List<(int level, string text)> headings)
    {
        if (headings.Count == 0) return true;
        int prevLevel = 0;
        foreach (var (level, _) in headings)
        {
            if (prevLevel == 0)
            {
                prevLevel = level;
                continue;
            }
            // Allow same level, increase by 1, or decrease to any lower level.
            // Flag skipping up by more than 1 (e.g. ## -> ####).
            if (level > prevLevel && level - prevLevel > 1)
            {
                return false;
            }
            prevLevel = level;
        }
        return true;
    }

    private static HashSet<int> ExtractCitationIndices(string content)
    {
        var indices = new HashSet<int>();
        foreach (Match match in CitationMarkerRegex.Matches(content))
        {
            if (int.TryParse(match.Groups[1].Value, out var idx))
            {
                indices.Add(idx);
            }
        }
        return indices;
    }

    /// <summary>
    /// Parses Markdown content into sections keyed by their heading.
    /// Each section contains the heading text and the body paragraphs.
    /// Content before the first heading is treated as a preamble section.
    /// </summary>
    private static List<(string Heading, List<string> Paragraphs)> ParseSections(string content)
    {
        var sections = new List<(string Heading, List<string> Paragraphs)>();
        var currentHeading = string.Empty;
        var currentParagraphs = new List<string>();
        var buffer = new List<string>();

        void FlushBuffer()
        {
            if (buffer.Count > 0)
            {
                var para = string.Join('\n', buffer).Trim();
                if (!string.IsNullOrWhiteSpace(para))
                {
                    currentParagraphs.Add(para);
                }
                buffer.Clear();
            }
        }

        void FlushSection()
        {
            FlushBuffer();
            if (currentParagraphs.Count > 0 || !string.IsNullOrEmpty(currentHeading))
            {
                sections.Add((currentHeading, new List<string>(currentParagraphs)));
                currentParagraphs.Clear();
            }
        }

        foreach (var line in content.Split('\n'))
        {
            var trimmed = line.Trim();
            var match = HeadingRegex.Match(trimmed);
            if (match.Success)
            {
                FlushSection();
                currentHeading = match.Groups[2].Value.Trim();
            }
            else if (string.IsNullOrWhiteSpace(trimmed))
            {
                FlushBuffer();
            }
            else
            {
                buffer.Add(line);
            }
        }

        FlushSection();
        return sections;
    }

    private static void AddDateVariants(HashSet<string> variants, DateTime date)
    {
        variants.Add(date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        variants.Add(date.ToString("yyyy/M/d", CultureInfo.InvariantCulture));
        variants.Add(date.ToString("MM-dd", CultureInfo.InvariantCulture));
        variants.Add(date.ToString("M-d", CultureInfo.InvariantCulture));
        variants.Add($"{date.Month}月{date.Day}日");
        variants.Add(date.ToString("MM月dd日", CultureInfo.InvariantCulture));
        variants.Add(date.ToString("yyyy年MM月dd日", CultureInfo.InvariantCulture));
        variants.Add(date.ToString("yyyy年M月d日", CultureInfo.InvariantCulture));
    }

    private static bool AnyDateInRangeMentioned(string content, DateTime start, DateTime end)
    {
        var current = start.Date;
        while (current <= end.Date)
        {
            var variants = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AddDateVariants(variants, current);
            if (variants.Any(d => content.Contains(d, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
            current = current.AddDays(1);
        }
        return false;
    }
}

/// <summary>
/// Result of a report quality evaluation.
/// </summary>
public class QualityEvaluation
{
    /// <summary>Overall weighted quality score, 0-100.</summary>
    public int QualityScore { get; set; }

    /// <summary>Citation coverage ratio, 0-1 (cited key paragraphs / total key paragraphs).</summary>
    public double CitationCoverage { get; set; }

    /// <summary>Number of distinct citation markers found in the report content.</summary>
    public int EvidenceCount { get; set; }

    /// <summary>Per-dimension sub-scores (0-100). Keys: structure, citation_coverage,
    /// source_diversity, information_density, time_range, risk_expression, format.</summary>
    public Dictionary<string, int> DimensionScores { get; set; } = new();

    /// <summary>Human-readable list of quality issues detected.</summary>
    public List<string> Issues { get; set; } = new();
}
