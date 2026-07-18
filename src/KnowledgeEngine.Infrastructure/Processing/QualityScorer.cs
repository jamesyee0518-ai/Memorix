using KnowledgeEngine.Application.Interfaces;

namespace KnowledgeEngine.Infrastructure.Processing;

/// <summary>
/// Calculates quality and value scores (0-100) for documents using the
/// §16.2 deduction-based system: start at 100 and subtract points for
/// detected quality issues. The quality_score is computed purely by system
/// rules; value_score may be supplied by the AI or estimated from source
/// type and content depth.
/// </summary>
public class QualityScorer : IQualityScorer
{
    /// <inheritdoc />
    public (int qualityScore, int valueScore, string? qualityReason) CalculateScores(
        string contentText,
        string? title,
        int wordCount,
        string sourceType,
        int? aiValueScore)
    {
        var reasons = new List<string>();
        var qualityScore = 100;

        // ===== §16.2 Deduction-based quality score =====

        // Content length deductions
        if (wordCount < 200)
        {
            qualityScore -= 40;
            reasons.Add("正文少于 200 字 (-40)");
        }
        else if (wordCount < 500)
        {
            qualityScore -= 20;
            reasons.Add("正文少于 500 字 (-20)");
        }

        // Duplicate paragraph ratio deduction
        var dupRatio = CalculateDuplicateParagraphRatio(contentText);
        if (dupRatio > 0.5)
        {
            qualityScore -= 40;
            reasons.Add($"重复段落比例 {dupRatio:P0} > 50% (-40)");
        }
        else if (dupRatio > 0.3)
        {
            qualityScore -= 20;
            reasons.Add($"重复段落比例 {dupRatio:P0} > 30% (-20)");
        }

        // Missing title deduction
        if (string.IsNullOrWhiteSpace(title))
        {
            qualityScore -= 10;
            reasons.Add("标题缺失 (-10)");
        }

        // Noise (ads / navigation) detection deduction
        if (HasObviousNoise(contentText))
        {
            qualityScore -= 20;
            reasons.Add("广告或导航噪音明显 (-20)");
        }

        // Clamp to [0, 100]
        qualityScore = Math.Max(0, Math.Min(100, qualityScore));

        var qualityReason = reasons.Count > 0
            ? string.Join("; ", reasons)
            : "内容质量良好，无扣分项";

        // ===== Value score =====
        // If AI provided a value_score, trust it; otherwise estimate from
        // source type authority and content depth.
        int valueScore;
        if (aiValueScore.HasValue)
        {
            valueScore = Math.Max(0, Math.Min(100, aiValueScore.Value));
        }
        else
        {
            valueScore = EstimateValueScore(wordCount, sourceType);
        }

        return (qualityScore, valueScore, qualityReason);
    }

    /// <summary>
    /// Calculates the ratio of duplicate paragraphs (by count) in the content.
    /// Returns a value between 0 and 1.
    /// </summary>
    private static double CalculateDuplicateParagraphRatio(string? contentText)
    {
        if (string.IsNullOrWhiteSpace(contentText))
            return 0;

        var paragraphs = contentText
            .Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Trim())
            .Where(p => p.Length > 0)
            .ToList();

        if (paragraphs.Count == 0)
            return 0;

        // Need at least 2 paragraphs for duplication to be meaningful
        if (paragraphs.Count < 2)
            return 0;

        var groups = paragraphs
            .GroupBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var duplicateCount = groups.Sum(g => g.Count() > 1 ? g.Count() : 0);
        return (double)duplicateCount / paragraphs.Count;
    }

    /// <summary>
    /// Heuristic detection of obvious advertising or navigation noise in the
    /// content (§16.2). Looks for common boilerplate markers.
    /// </summary>
    private static bool HasObviousNoise(string? contentText)
    {
        if (string.IsNullOrWhiteSpace(contentText))
            return false;

        var noiseMarkers = new[]
        {
            "cookie", "我们使用 cookie", "accept all", "reject all",
            "subscribe now", "sign up for our newsletter", "newsletter",
            "版权所有", "all rights reserved", "免责声明",
            "点击这里", "click here to", "下载我们的app", "扫码关注",
            "advertisement", "sponsored content", "广告",
            "相关推荐", "猜你喜欢", "大家都在看",
            "登录后查看", "登录注册", "app下载",
            "javascript is required", "enable javascript"
        };

        var lower = contentText.ToLowerInvariant();
        var hits = 0;
        foreach (var marker in noiseMarkers)
        {
            if (lower.Contains(marker.ToLowerInvariant()))
                hits++;
        }

        // If multiple distinct noise markers are present, treat as obvious noise
        return hits >= 2;
    }

    /// <summary>
    /// Estimates a value score (0-100) from source type authority and content
    /// depth when the AI did not provide one.
    /// </summary>
    private static int EstimateValueScore(int wordCount, string? sourceType)
    {
        var score = 0;

        // Source type authority contribution (up to 40 points)
        score += (sourceType ?? string.Empty).ToLowerInvariant() switch
        {
            "pdf" => 40,   // Research papers / reports — high value
            "url" => 30,   // Web articles — medium-high value
            "text" => 20,  // Raw text — medium value
            _ => 15        // Unknown — base value
        };

        // Content depth contribution (up to 60 points)
        if (wordCount >= 3000)
            score += 60;
        else if (wordCount >= 1500)
            score += 48;
        else if (wordCount >= 500)
            score += 35;
        else if (wordCount >= 200)
            score += 20;
        else if (wordCount > 0)
            score += 8;

        return Math.Max(0, Math.Min(100, score));
    }
}
