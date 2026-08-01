using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace KnowledgeEngine.Infrastructure.Audio;

/// <summary>
/// Startup seeder that populates the <c>prompt_registries</c> table with the
/// default prompt entries defined in the V2.0 development plan.
/// </summary>
/// <remarks>
/// The seeder implements <see cref="IHostedService"/> so it runs exactly once
/// during application startup. It is fully idempotent: existing prompt entries
/// (matched by <see cref="PromptRegistry.PromptKey"/> + <see cref="PromptRegistry.Version"/>)
/// are left untouched and only missing entries are inserted.
/// </remarks>
public sealed class PromptRegistrySeeder : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PromptRegistrySeeder> _logger;

    public PromptRegistrySeeder(
        IServiceScopeFactory scopeFactory,
        ILogger<PromptRegistrySeeder> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await SeedAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Seeding failures must not crash application startup.
            _logger.LogError(ex, "Failed to seed default prompt registry entries");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task SeedAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();

        var prompts = BuildDefaultPrompts();

        // Load all existing keys+versions in a single round-trip to avoid
        // querying the database once per prompt.
        var seedKeys = prompts.Select(p => p.PromptKey).ToHashSet();
        var existing = await db.PromptRegistries
            .Where(p => seedKeys.Contains(p.PromptKey))
            .Select(p => new { p.PromptKey, p.Version })
            .ToListAsync(ct);

        var existingSet = existing
            .Select(e => (e.PromptKey, e.Version))
            .ToHashSet();

        var toAdd = prompts
            .Where(p => !existingSet.Contains((p.PromptKey, p.Version)))
            .ToList();

        if (toAdd.Count == 0)
        {
            _logger.LogInformation(
                "Prompt registry seed skipped: all {Count} default prompts already exist",
                prompts.Count);
            return;
        }

        foreach (var prompt in toAdd)
        {
            db.PromptRegistries.Add(prompt);
        }

        await db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Prompt registry seed completed: inserted {Inserted} of {Total} default prompts",
            toAdd.Count,
            prompts.Count);
    }

    /// <summary>
    /// Builds the list of default prompt entries to seed.
    /// </summary>
    private static List<PromptRegistry> BuildDefaultPrompts()
    {
        var now = DateTime.UtcNow;
        var prompts = new List<PromptRegistry>();

        // 1. summary.default — language-agnostic default summarization
        prompts.Add(new PromptRegistry
        {
            Id = Guid.NewGuid(),
            PromptKey = "summary.default",
            Version = "1.0.0",
            Title = "Default Transcript Summarization",
            Description = "General-purpose summarization prompt for transcripts. " +
                          "Produces a concise summary covering the main topics, key points, " +
                          "and important takeaways. Language-agnostic — works for any language.",
            SystemPrompt =
                "You are an expert summarization assistant. Your task is to read a transcript " +
                "and produce a clear, accurate, and concise summary.\n\n" +
                "Guidelines:\n" +
                "- Capture the main topics and key points discussed.\n" +
                "- Preserve factual accuracy; do not invent information not present in the transcript.\n" +
                "- Use the same language as the source transcript.\n" +
                "- Structure the summary with a brief overview followed by key points.\n" +
                "- Keep the summary proportional to the length and density of the source.\n" +
                "- Omit filler, repetitions, and off-topic chatter.",
            UserPromptTemplate =
                "Please summarize the following transcript.\n\n" +
                "Title: {{title}}\n\n" +
                "Transcript:\n{{content}}\n\n" +
                "Provide a concise summary with the main topics and key points.",
            Language = null,
            ProviderCompatibility = "",
            IsActive = true,
            Status = PromptRegistryStatuses.Published,
            PublishedAt = now,
            CreatedBy = "system-seeder",
            CreatedAt = now,
            UpdatedAt = now,
        });

        // 2. summary.meeting — meeting-specific summarization (zh)
        prompts.Add(new PromptRegistry
        {
            Id = Guid.NewGuid(),
            PromptKey = "summary.meeting",
            Version = "1.0.0",
            Title = "会议纪要总结",
            Description = "面向会议场景的专用总结提示词。提取会议议题、讨论要点、决策结论和待办事项，" +
                          "生成结构化的会议纪要。适用于中文会议录音转写文本。",
            SystemPrompt =
                "你是一位专业的会议纪要助手。你的任务是阅读会议转写文本，生成结构化的会议纪要。\n\n" +
                "要求：\n" +
                "- 提炼会议的核心议题和讨论要点。\n" +
                "- 明确记录已达成的决策和结论。\n" +
                "- 列出待办事项（包括负责人和截止时间，如果转写中提及）。\n" +
                "- 标注未解决的问题或遗留议题。\n" +
                "- 保持客观准确，不要添加转写中未出现的信息。\n" +
                "- 使用中文输出。\n" +
                "- 按以下结构组织：一、会议概述；二、讨论要点；三、决策结论；四、待办事项；五、遗留议题。",
            UserPromptTemplate =
                "请为以下会议转写文本生成会议纪要。\n\n" +
                "标题：{{title}}\n\n" +
                "转写文本：\n{{content}}\n\n" +
                "请按结构化格式输出会议纪要。",
            Language = "zh",
            ProviderCompatibility = "",
            IsActive = true,
            Status = PromptRegistryStatuses.Published,
            PublishedAt = now,
            CreatedBy = "system-seeder",
            CreatedAt = now,
            UpdatedAt = now,
        });

        // 3. summary.lecture — lecture-specific summarization (zh)
        prompts.Add(new PromptRegistry
        {
            Id = Guid.NewGuid(),
            PromptKey = "summary.lecture",
            Version = "1.0.0",
            Title = "课程讲座总结",
            Description = "面向课程/讲座场景的专用总结提示词。提取知识框架、核心概念、关键论点和例证，" +
                          "生成适合复习和回顾的结构化笔记。适用于中文课程或讲座录音转写文本。",
            SystemPrompt =
                "你是一位专业的学术笔记助手。你的任务是阅读课程或讲座的转写文本，" +
                "生成结构化的学习笔记。\n\n" +
                "要求：\n" +
                "- 提取课程的知识框架和章节结构。\n" +
                "- 列出核心概念及其定义或解释。\n" +
                "- 总结关键论点、推导过程和结论。\n" +
                "- 保留重要的例证、案例和数据。\n" +
                "- 标注需要重点复习的内容。\n" +
                "- 保持学术准确性，不要添加转写中未出现的信息。\n" +
                "- 使用中文输出。\n" +
                "- 按以下结构组织：一、课程概述；二、知识框架；三、核心概念；四、关键论点；五、例证与案例；六、复习要点。",
            UserPromptTemplate =
                "请为以下课程/讲座转写文本生成结构化学习笔记。\n\n" +
                "标题：{{title}}\n\n" +
                "转写文本：\n{{content}}\n\n" +
                "请按结构化格式输出学习笔记。",
            Language = "zh",
            ProviderCompatibility = "",
            IsActive = true,
            Status = PromptRegistryStatuses.Published,
            PublishedAt = now,
            CreatedBy = "system-seeder",
            CreatedAt = now,
            UpdatedAt = now,
        });

        // 4. entity.extract — entity extraction from transcripts (zh)
        prompts.Add(new PromptRegistry
        {
            Id = Guid.NewGuid(),
            PromptKey = "entity.extract",
            Version = "1.0.0",
            Title = "实体抽取",
            Description = "从转写文本中抽取命名实体，包括人名、机构名、地名、产品名、术语等。" +
                          "输出结构化的实体列表，包含实体名称、类型和上下文。适用于中文转写文本。",
            SystemPrompt =
                "你是一位专业的命名实体识别助手。你的任务是从转写文本中抽取命名实体。\n\n" +
                "要求：\n" +
                "- 识别以下类型的实体：人名(PERSON)、机构(ORGANIZATION)、地名(LOCATION)、" +
                "产品/项目(PRODUCT)、日期/时间(DATE)、金额/数量(MONEY)、专业术语(TERM)。\n" +
                "- 每个实体需标注其类型和首次出现的上下文句子。\n" +
                "- 合并同一实体的不同表述（如简称和全称）。\n" +
                "- 过滤过于模糊或泛化的词（如\"大家\"、\"这个\"）。\n" +
                "- 保持中文原文表述。\n" +
                "- 以 JSON 数组格式输出，每个元素包含 name、type、context 三个字段。",
            UserPromptTemplate =
                "请从以下转写文本中抽取命名实体。\n\n" +
                "标题：{{title}}\n\n" +
                "转写文本：\n{{content}}\n\n" +
                "请以 JSON 数组格式输出抽取到的实体列表。",
            Language = "zh",
            ProviderCompatibility = "",
            IsActive = true,
            Status = PromptRegistryStatuses.Published,
            PublishedAt = now,
            CreatedBy = "system-seeder",
            CreatedAt = now,
            UpdatedAt = now,
        });

        // 5. todo.extract — todo/action item extraction from transcripts (zh)
        prompts.Add(new PromptRegistry
        {
            Id = Guid.NewGuid(),
            PromptKey = "todo.extract",
            Version = "1.0.0",
            Title = "待办事项提取",
            Description = "从转写文本中提取待办事项和行动项，包括任务内容、负责人、截止时间等。" +
                          "输出结构化的待办列表，适用于会议、访谈等场景的中文转写文本。",
            SystemPrompt =
                "你是一位专业的待办事项提取助手。你的任务是从转写文本中识别并提取待办事项和行动项。\n\n" +
                "要求：\n" +
                "- 识别明确的任务、行动项和后续跟进事项（follow-up）。\n" +
                "- 尽可能标注负责人（如果转写中提及）。\n" +
                "- 尽可能标注截止时间或时间节点（如果转写中提及）。\n" +
                "- 为每个待办事项提供简短的任务描述。\n" +
                "- 标注优先级（高/中/低），基于上下文语气和紧迫程度判断。\n" +
                "- 不要将陈述性或信息性内容误判为待办事项。\n" +
                "- 使用中文输出。\n" +
                "- 以 JSON 数组格式输出，每个元素包含 task、assignee、dueDate、priority 字段。" +
                "assignee、dueDate 可为 null。",
            UserPromptTemplate =
                "请从以下转写文本中提取待办事项和行动项。\n\n" +
                "标题：{{title}}\n\n" +
                "转写文本：\n{{content}}\n\n" +
                "请以 JSON 数组格式输出待办事项列表。",
            Language = "zh",
            ProviderCompatibility = "",
            IsActive = true,
            Status = PromptRegistryStatuses.Published,
            PublishedAt = now,
            CreatedBy = "system-seeder",
            CreatedAt = now,
            UpdatedAt = now,
        });

        return prompts;
    }
}
