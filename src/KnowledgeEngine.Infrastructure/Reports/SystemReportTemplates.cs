using System.Text.Json;
using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KnowledgeEngine.Infrastructure.Reports;

/// <summary>
/// Defines system report templates (daily, weekly, topic) and provides
/// initialization logic to seed them into the database on startup.
/// </summary>
public class SystemReportTemplates
{
    public static async Task InitializeAsync(IAppDbContext db, ILogger logger, CancellationToken ct = default)
    {
        try
        {
            var hasSystemTemplates = await db.ReportTemplates
                .AnyAsync(t => t.IsSystem, ct);

            if (hasSystemTemplates)
            {
                logger.LogDebug("System report templates already exist, skipping initialization.");
                return;
            }

            var now = DateTime.UtcNow;
            var templates = new List<ReportTemplate>
            {
                CreateDailyTemplate(now),
                CreateWeeklyTemplate(now),
                CreateTopicTemplate(now)
            };

            db.ReportTemplates.AddRange(templates);
            await db.SaveChangesAsync(ct);

            logger.LogInformation("Initialized {Count} system report templates.", templates.Count);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to initialize system report templates. Continuing startup.");
        }
    }

    // ===== Daily Report Template (9.1) =====

    private static ReportTemplate CreateDailyTemplate(DateTime now)
    {
        return new ReportTemplate
        {
            Id = Guid.NewGuid(),
            UserId = null,
            Name = "日报模板（系统）",
            ReportType = "daily",
            Description = "每日知识摘要报告模板，汇总当天收集的资料",
            TemplateMarkdown = @"# 知识日报 - {date}

## 概述
{overview}

## 重要发现
{key_findings}

## 详细分析
{detailed_analysis}

## 趋势与信号
{trends_signals}

## 建议关注
{recommendations}

## 参考来源
{sources}
",
            SystemPrompt = @"你是一个专业的知识管理助手，负责生成每日知识摘要报告。

规则：
1. 仅基于提供的参考资料生成报告，不要编造或使用资料之外的信息
2. 在相关信息处标注引用编号，如 [1]、[2] 等，对应参考资料的编号
3. 报告要结构清晰，重点突出
4. 使用中文撰写
5. 如果资料不足或质量不高，请如实说明
6. 关注当天的新动态、重要发现和值得关注的趋势",
            UserPromptTemplate = @"请基于以下 {doc_count} 篇资料，生成 {date} 的知识日报。

报告日期：{date}

参考资料：
{report_context}

请按照以下结构生成日报：
1. 概述：用2-3句话总结当天最重要的信息
2. 重要发现：列出3-5个关键发现，每个发现标注引用来源
3. 详细分析：对重要发现进行深入分析
4. 趋势与信号：识别可能的发展趋势和信号
5. 建议关注：推荐后续需要关注的方向
6. 参考来源：列出所有引用的资料编号和标题",
            OutputRules = JsonSerializer.Serialize(new
            {
                format = "markdown",
                max_length = 5000,
                citation_style = "numeric",
                sections = new[] { "概述", "重要发现", "详细分析", "趋势与信号", "建议关注", "参考来源" }
            }),
            IsSystem = true,
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    // ===== Weekly Report Template (9.2) =====

    private static ReportTemplate CreateWeeklyTemplate(DateTime now)
    {
        return new ReportTemplate
        {
            Id = Guid.NewGuid(),
            UserId = null,
            Name = "周报模板（系统）",
            ReportType = "weekly",
            Description = "每周知识总结报告模板，回顾一周内的知识积累和变化",
            TemplateMarkdown = @"# 知识周报 - {start_date} 至 {end_date}

## 本周概述
{weekly_overview}

## 核心主题
{core_topics}

## 重要进展
{key_progress}

## 趋势分析
{trend_analysis}

## 风险与机遇
{risks_opportunities}

## 下周建议
{next_week_suggestions}

## 参考来源
{sources}
",
            SystemPrompt = @"你是一个专业的知识管理助手，负责生成每周知识总结报告。

规则：
1. 仅基于提供的参考资料生成报告，不要编造或使用资料之外的信息
2. 在相关信息处标注引用编号，如 [1]、[2] 等，对应参考资料的编号
3. 报告要具有回顾性和前瞻性，既总结过去又展望未来
4. 使用中文撰写
5. 重点突出本周的变化和新趋势
6. 按价值评分排序，优先展示高价值信息",
            UserPromptTemplate = @"请基于以下 {doc_count} 篇资料，生成 {start_date} 至 {end_date} 的知识周报。

时间范围：{start_date} 至 {end_date}

参考资料（按价值评分排序，重点资料 {focus_count} 篇）：
{report_context}

请按照以下结构生成周报：
1. 本周概述：总结本周最重要的3-5个信息点
2. 核心主题：归纳本周的主要知识主题
3. 重要进展：列出关键进展和变化
4. 趋势分析：分析与上周相比的变化趋势
5. 风险与机遇：识别潜在的风险和机遇
6. 下周建议：给出下周需要关注的方向
7. 参考来源：列出所有引用的资料编号和标题",
            OutputRules = JsonSerializer.Serialize(new
            {
                format = "markdown",
                max_length = 8000,
                citation_style = "numeric",
                sections = new[] { "本周概述", "核心主题", "重要进展", "趋势分析", "风险与机遇", "下周建议", "参考来源" }
            }),
            IsSystem = true,
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    // ===== Topic Report Template (9.3) =====

    private static ReportTemplate CreateTopicTemplate(DateTime now)
    {
        return new ReportTemplate
        {
            Id = Guid.NewGuid(),
            UserId = null,
            Name = "专题报告模板（系统）",
            ReportType = "topic",
            Description = "专题深度分析报告模板，针对特定问题进行深入研究",
            TemplateMarkdown = @"# 专题报告 - {title}

## 研究问题
{question}

## 摘要
{abstract}

## 背景与上下文
{background}

## 核心发现
{core_findings}

## 深度分析
{deep_analysis}

## 多方观点
{multiple_perspectives}

## 数据与证据
{data_evidence}

## 结论与建议
{conclusion}

## 参考来源
{sources}
",
            SystemPrompt = @"你是一个专业的知识管理助手，负责生成专题深度分析报告。

规则：
1. 仅基于提供的参考资料生成报告，不要编造或使用资料之外的信息
2. 在相关信息处标注引用编号，如 [1]、[2] 等，对应参考资料的编号
3. 报告要深入、全面，具有分析性和洞察力
4. 使用中文撰写
5. 如果资料不足以完全回答问题，请明确说明哪些部分有资料支持，哪些部分缺少资料
6. 展示不同观点和角度，保持客观中立
7. 重视证据和数据支持",
            UserPromptTemplate = @"请基于以下 {doc_count} 篇资料，针对以下问题生成专题报告。

报告标题：{title}
研究问题：{question}

参考资料（通过混合检索召回，按相关性排序）：
{report_context}

请按照以下结构生成专题报告：
1. 摘要：用3-5句话概括核心发现和结论
2. 背景与上下文：介绍问题的背景和相关上下文
3. 核心发现：列出3-7个关键发现，每个发现标注引用来源
4. 深度分析：对核心发现进行深入分析，探讨因果关系和影响
5. 多方观点：展示不同立场和观点，保持客观
6. 数据与证据：引用具体的数据和证据支持分析
7. 结论与建议：总结主要结论，给出后续行动建议
8. 参考来源：列出所有引用的资料编号和标题",
            OutputRules = JsonSerializer.Serialize(new
            {
                format = "markdown",
                max_length = 10000,
                citation_style = "numeric",
                sections = new[] { "摘要", "背景与上下文", "核心发现", "深度分析", "多方观点", "数据与证据", "结论与建议", "参考来源" }
            }),
            IsSystem = true,
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now
        };
    }
}
