using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using KnowledgeEngine.Application.DTOs;
using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Application.Settings;
using KnowledgeEngine.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KnowledgeEngine.Infrastructure.Reports;

public class ReportWorker : BackgroundService
{
    private static readonly TimeSpan PollingInterval = TimeSpan.FromSeconds(10);
    private const int MaxRetries = 3;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ReportWorker> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public ReportWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<ReportWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ReportWorker started. Polling every {Interval}s.", PollingInterval.TotalSeconds);

        await Task.Delay(TimeSpan.FromSeconds(8), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PollAndProcessAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in ReportWorker polling cycle");
            }

            try
            {
                await Task.Delay(PollingInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("ReportWorker stopped.");
    }

    private async Task PollAndProcessAsync(CancellationToken ct)
    {
        List<ReportJob> pendingJobs;

        using (var scope = _scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();

            pendingJobs = await db.ReportJobs
                .Where(j => j.Status == "pending")
                .Where(j => j.StartedAt == null || j.StartedAt <= DateTime.UtcNow)
                .OrderBy(j => j.CreatedAt)
                .Take(5)
                .ToListAsync(ct);
        }

        if (pendingJobs.Count == 0)
        {
            return;
        }

        _logger.LogInformation("ReportWorker found {Count} pending report job(s).", pendingJobs.Count);

        foreach (var job in pendingJobs)
        {
            if (ct.IsCancellationRequested) break;

            try
            {
                await ProcessJobAsync(job.Id, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ReportWorker failed to process job {JobId}", job.Id);
                await HandleJobFailureAsync(job.Id, ex, ct);
            }
        }
    }

    private async Task ProcessJobAsync(Guid jobId, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        var llmService = scope.ServiceProvider.GetRequiredService<ILlmService>();
        var searchService = scope.ServiceProvider.GetRequiredService<ISearchService>();
        var llmSettings = scope.ServiceProvider.GetRequiredService<IOptions<LlmSettings>>().Value;

        // Step 1: Mark job as processing
        var job = await db.ReportJobs.FirstOrDefaultAsync(j => j.Id == jobId, ct);
        if (job == null || job.Status != "pending")
        {
            return;
        }

        job.Status = "processing";
        job.StartedAt = DateTime.UtcNow;
        job.Progress = 10;
        job.CurrentStep = "planning";
        await db.SaveChangesAsync(ct);

        _logger.LogInformation("Processing report job {JobId} (type: {ReportType})", jobId, job.ReportType);

        // Step 2: Parse input params
        var inputParams = ParseInputParams(job.InputParams);

        // Step 2.5: Build report plan (planning phase, §7.2)
        var planBuilder = new ReportPlanBuilder();
        var planStartDate = GetStartDate(job.ReportType, inputParams);
        var planEndDate = GetEndDate(job.ReportType, inputParams);
        var planQuery = job.ReportType.ToLowerInvariant() == "topic" ? inputParams.Question : null;

        var reportPlan = planBuilder.BuildPlan(
            reportType: job.ReportType,
            title: inputParams.Title,
            query: planQuery,
            startDate: planStartDate,
            endDate: planEndDate,
            topicId: job.TopicId ?? inputParams.TopicId,
            depth: inputParams.Depth);

        job.PlanJson = JsonSerializer.Serialize(reportPlan, JsonOptions);
        await db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Report job {JobId} plan built: type={ReportType}, sections={SectionCount}",
            jobId, reportPlan.ReportType, reportPlan.Sections.Count);

        // Step 3: Recall materials based on report type
        List<RecalledMaterial> materials;
        string reportTitle;

        switch (job.ReportType.ToLowerInvariant())
        {
            case "daily":
                (materials, reportTitle) = await RecallDailyMaterialsAsync(db, job, inputParams, ct);
                break;
            case "weekly":
                (materials, reportTitle) = await RecallWeeklyMaterialsAsync(db, job, inputParams, ct);
                break;
            case "topic":
                (materials, reportTitle) = await RecallTopicMaterialsAsync(db, searchService, job, inputParams, ct);
                break;
            default:
                throw new InvalidOperationException($"Unknown report type: {job.ReportType}");
        }

        // Backfill the final report title (determined during recall) into the plan
        reportPlan.Title = reportTitle;

        // Update progress: materials recalled
        job.Progress = 25;
        job.CurrentStep = "retrieving";
        await db.SaveChangesAsync(ct);

        // Step 4: Handle insufficient materials
        if (materials.Count == 0)
        {
            _logger.LogInformation("Report job {JobId}: no materials found, creating empty report.", jobId);

            var emptyReport = new Report
            {
                Id = Guid.NewGuid(),
                UserId = job.UserId,
                TopicId = job.TopicId,
                ReportType = job.ReportType,
                Title = reportTitle,
                ContentMarkdown = "# 报告\n\n## 资料不足\n\n当前时间范围内没有可用的资料。",
                SourceDocumentIds = JsonSerializer.Serialize(new List<Guid>(), JsonOptions),
                SourceChunkIds = JsonSerializer.Serialize(new List<Guid>(), JsonOptions),
                Citations = JsonSerializer.Serialize(new List<CitationItem>(), JsonOptions),
                GeneratedByModel = llmSettings.Model,
                PromptVersion = "1.0",
                Status = "done",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            db.Reports.Add(emptyReport);

            job.Status = "done";
            job.ReportId = emptyReport.Id;
            job.Progress = 100;
            job.CurrentStep = "done";
            job.FinishedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);

            return;
        }

        // Step 5: Get system template
        var template = await db.ReportTemplates
            .FirstOrDefaultAsync(t => t.ReportType == job.ReportType && t.IsSystem && t.IsActive, ct);

        var systemPrompt = template?.SystemPrompt ?? BuildDefaultSystemPrompt(job.ReportType);
        var userPromptTemplate = template?.UserPromptTemplate;

        // Step 6: Build report context (six-layer structure, §7.4)
        var reportContext = BuildReportContext(reportPlan, materials);

        // Update progress: context built
        job.Progress = 40;
        job.CurrentStep = "building_context";
        await db.SaveChangesAsync(ct);

        // Step 7: Build user prompt
        var userPrompt = BuildUserPrompt(userPromptTemplate, job.ReportType, reportContext, materials, inputParams, reportTitle);

        // Step 8: Call LLM
        // Update progress: generating
        job.Progress = 60;
        job.CurrentStep = "generating";
        await db.SaveChangesAsync(ct);

        // Determine whether to use segmented generation (deep topic reports)
        var useSegmentedGeneration =
            job.ReportType.ToLowerInvariant() == "topic" &&
            inputParams.Depth == "deep" &&
            reportPlan.Sections.Count > 0;

        string fullContent;
        string usedModel;

        if (useSegmentedGeneration)
        {
            _logger.LogInformation("Report job {JobId}: using segmented generation with {SectionCount} sections (deep mode)",
                jobId, reportPlan.Sections.Count);

            // Segmented generation: generate each section separately
            var sectionResults = new List<string>();
            var totalSections = reportPlan.Sections.Count;
            var sectionProgressRange = 30; // 60% to 90%

            for (var i = 0; i < totalSections; i++)
            {
                var section = reportPlan.Sections[i];
                var sectionPrompt = BuildSectionPrompt(section, reportContext, job.ReportType, inputParams, reportTitle);
                var sectionResult = await llmService.CompleteAsync(systemPrompt, sectionPrompt, llmSettings.Model, ct);
                sectionResults.Add($"## {section.Title}\n\n{sectionResult.Content}");

                // Update progress proportionally
                job.Progress = 60 + (int)((double)(i + 1) / totalSections * sectionProgressRange);
                job.CurrentStep = $"generating_section_{i + 1}_of_{totalSections}";
                await db.SaveChangesAsync(ct);

                _logger.LogInformation("Report job {JobId}: generated section {SectionIndex}/{TotalSections} ({SectionKey})",
                    jobId, i + 1, totalSections, section.Key);
            }

            fullContent = string.Join("\n\n", sectionResults);
            usedModel = llmSettings.Model;
        }
        else
        {
            // One-shot generation (original logic)
            _logger.LogInformation("Report job {JobId}: using one-shot generation", jobId);
            var llmResult = await llmService.CompleteAsync(systemPrompt, userPrompt, llmSettings.Model, ct);
            fullContent = llmResult.Content;
            usedModel = llmResult.Model;
        }

        // Step 9: Build citations (system-generated)
        var citations = BuildCitations(materials);
        var sourceDocumentIds = materials.Select(m => m.DocumentId).Distinct().ToList();
        var sourceChunkIds = materials.Where(m => m.ChunkId.HasValue).Select(m => m.ChunkId!.Value).ToList();

        // Step 9.5: Quality evaluation + citation validation
        // Update progress: evaluating
        job.Progress = 80;
        job.CurrentStep = "evaluating";
        await db.SaveChangesAsync(ct);

        var evaluator = new ReportQualityEvaluator();
        var reportStartDate = GetStartDate(job.ReportType, inputParams);
        var reportEndDate = GetEndDate(job.ReportType, inputParams);
        var quality = evaluator.Evaluate(
            fullContent,
            sourceDocumentIds.Count,
            citations.Count,
            job.ReportType,
            reportStartDate,
            reportEndDate);

        // Citation validation: ensure LLM-output [n] markers are within the
        // system-generated citations list. Log mismatches but do not block.
        ValidateCitations(fullContent, citations.Count, jobId);

        if (quality.Issues.Count > 0)
        {
            _logger.LogInformation("Report job {JobId} quality evaluation: score={Score}, issues={Issues}",
                jobId, quality.QualityScore, string.Join(" | ", quality.Issues));
        }
        else
        {
            _logger.LogInformation("Report job {JobId} quality evaluation: score={Score}", jobId, quality.QualityScore);
        }

        // Step 10: Create Report record
        // Update progress: saving
        job.Progress = 90;
        job.CurrentStep = "saving";
        await db.SaveChangesAsync(ct);

        var now = DateTime.UtcNow;
        var report = new Report
        {
            Id = Guid.NewGuid(),
            UserId = job.UserId,
            TopicId = job.TopicId,
            ReportType = job.ReportType,
            Title = reportTitle,
            ContentMarkdown = fullContent,
            Query = job.ReportType == "topic" ? inputParams.Question : null,
            StartDate = GetStartDate(job.ReportType, inputParams),
            EndDate = GetEndDate(job.ReportType, inputParams),
            SourceDocumentIds = JsonSerializer.Serialize(sourceDocumentIds, JsonOptions),
            SourceChunkIds = JsonSerializer.Serialize(sourceChunkIds, JsonOptions),
            Citations = JsonSerializer.Serialize(citations, JsonOptions),
            GeneratedByModel = usedModel,
            PromptVersion = "1.0",
            Status = "done",
            QualityScore = quality.QualityScore,
            CitationCoverage = quality.CitationCoverage,
            EvidenceCount = quality.EvidenceCount,
            CreatedAt = now,
            UpdatedAt = now
        };

        db.Reports.Add(report);

        // Step 11: Create ReportSource and ReportCitation records
        for (var i = 0; i < materials.Count; i++)
        {
            var material = materials[i];
            var citationIndex = i + 1;

            var source = new ReportSource
            {
                ReportId = report.Id,
                DocumentId = material.DocumentId,
                ChunkId = material.ChunkId,
                CitationIndex = citationIndex,
                RelevanceScore = (decimal)material.RelevanceScore,
                SourceRole = material.SourceRole,
                CreatedAt = now
            };
            db.ReportSources.Add(source);

            var citation = new ReportCitation
            {
                Id = Guid.NewGuid(),
                ReportId = report.Id,
                DocumentId = material.DocumentId,
                ChunkId = material.ChunkId,
                CitationIndex = citationIndex,
                CitationKey = $"CIT-{citationIndex}",
                Title = material.Title,
                SourceUrl = material.SourceUrl,
                SourceDomain = material.SourceDomain,
                SourceType = material.SourceType,
                RelevanceScore = material.RelevanceScore,
                SourceRole = material.SourceRole,
                CreatedAt = now
            };
            db.ReportCitations.Add(citation);
        }

        // Step 12: Update ReportJob status
        job.Status = "done";
        job.ReportId = report.Id;
        job.Model = usedModel;
        job.PromptVersion = "1.0";
        job.Progress = 100;
        job.CurrentStep = "done";
        job.FinishedAt = now;
        await db.SaveChangesAsync(ct);

        _logger.LogInformation("Report job {JobId} completed. Report ID: {ReportId}", jobId, report.Id);
    }

    // ===== Material Recall: Daily =====

    private static async Task<(List<RecalledMaterial>, string)> RecallDailyMaterialsAsync(
        IAppDbContext db,
        ReportJob job,
        InputParams inputParams,
        CancellationToken ct)
    {
        var date = inputParams.Date?.Date ?? DateTime.UtcNow.Date;
        var startOfDay = date;
        var endOfDay = date.AddDays(1);

        var query = db.Documents
            .Where(d => d.UserId == job.UserId && d.AiStatus == "done")
            .Where(d => d.CreatedAt >= startOfDay && d.CreatedAt < endOfDay);

        if (job.TopicId.HasValue)
        {
            query = query.Where(d => d.TopicId == job.TopicId);
        }

        var documents = await query
            .OrderByDescending(d => d.ValueScore)
            .Take(30)
            .ToListAsync(ct);

        var sources = await GetSourcesForDocumentsAsync(db, documents, ct);

        var materials = documents.Select(d => new RecalledMaterial
        {
            DocumentId = d.Id,
            ChunkId = null,
            Title = d.Title,
            SourceUrl = sources.GetValueOrDefault(d.Id)?.Url,
            SourceDomain = sources.GetValueOrDefault(d.Id)?.Domain,
            SourceType = sources.GetValueOrDefault(d.Id)?.SourceType,
            DocumentDate = d.PublishedAt ?? d.CreatedAt,
            Summary = d.Summary,
            OneSentenceConclusion = d.OneSentenceConclusion,
            KeyPoints = d.KeyPoints,
            RelevanceScore = (d.ValueScore ?? 50) / 100.0,
            SourceRole = "document"
        }).ToList();

        var title = $"知识日报 - {date:yyyy-MM-dd}";
        return (materials, title);
    }

    // ===== Material Recall: Weekly =====

    private static async Task<(List<RecalledMaterial>, string)> RecallWeeklyMaterialsAsync(
        IAppDbContext db,
        ReportJob job,
        InputParams inputParams,
        CancellationToken ct)
    {
        var endDate = inputParams.EndDate?.Date ?? DateTime.UtcNow.Date;
        var startDate = inputParams.StartDate?.Date ?? endDate.AddDays(-6);
        var endOfRange = endDate.AddDays(1);

        var query = db.Documents
            .Where(d => d.UserId == job.UserId && d.AiStatus == "done")
            .Where(d => d.CreatedAt >= startDate && d.CreatedAt < endOfRange);

        if (job.TopicId.HasValue)
        {
            query = query.Where(d => d.TopicId == job.TopicId);
        }

        var documents = await query
            .OrderByDescending(d => d.ValueScore)
            .Take(50)
            .ToListAsync(ct);

        var sources = await GetSourcesForDocumentsAsync(db, documents, ct);

        var materials = documents.Select((d, idx) => new RecalledMaterial
        {
            DocumentId = d.Id,
            ChunkId = null,
            Title = d.Title,
            SourceUrl = sources.GetValueOrDefault(d.Id)?.Url,
            SourceDomain = sources.GetValueOrDefault(d.Id)?.Domain,
            SourceType = sources.GetValueOrDefault(d.Id)?.SourceType,
            DocumentDate = d.PublishedAt ?? d.CreatedAt,
            Summary = d.Summary,
            OneSentenceConclusion = d.OneSentenceConclusion,
            KeyPoints = d.KeyPoints,
            RelevanceScore = (d.ValueScore ?? 50) / 100.0,
            SourceRole = idx < 20 ? "focus" : "document"
        }).ToList();

        var title = $"知识周报 - {startDate:yyyy-MM-dd} 至 {endDate:yyyy-MM-dd}";
        return (materials, title);
    }

    // ===== Material Recall: Topic =====

    private static async Task<(List<RecalledMaterial>, string)> RecallTopicMaterialsAsync(
        IAppDbContext db,
        ISearchService searchService,
        ReportJob job,
        InputParams inputParams,
        CancellationToken ct)
    {
        var question = inputParams.Question ?? "";
        var title = !string.IsNullOrEmpty(inputParams.Title) ? inputParams.Title : $"专题报告 - {question}";

        // Build search request
        var searchRequest = new SearchRequest
        {
            TopicId = job.TopicId ?? inputParams.TopicId,
            Query = question,
            SearchType = "hybrid",
            Filters = new SearchFilters
            {
                DateFrom = inputParams.DateFrom,
                DateTo = inputParams.DateTo,
                MinValueScore = inputParams.MinValueScore
            },
            Limit = 30
        };

        var searchResponse = await searchService.SearchAsync(job.UserId, searchRequest, ct);
        var searchResults = searchResponse.Data?.Items ?? new List<SearchResultItem>();

        // Rerank: take top 15
        var rerankedResults = searchResults
            .OrderByDescending(r => r.Score)
            .Take(15)
            .ToList();

        if (rerankedResults.Count == 0)
        {
            return (new List<RecalledMaterial>(), title);
        }

        // Get full document info for the search results
        var documentIds = rerankedResults.Select(r => r.DocumentId).Distinct().ToList();
        var documents = await db.Documents
            .Where(d => documentIds.Contains(d.Id))
            .ToListAsync(ct);

        var docDict = documents.ToDictionary(d => d.Id);

        var materials = rerankedResults.Select(r =>
        {
            docDict.TryGetValue(r.DocumentId, out var doc);
            return new RecalledMaterial
            {
                DocumentId = r.DocumentId,
                ChunkId = r.ChunkId == Guid.Empty ? null : r.ChunkId,
                Title = doc?.Title ?? r.Title,
                SourceUrl = r.SourceUrl,
                SourceDomain = r.SourceDomain,
                SourceType = r.SourceType,
                DocumentDate = doc?.PublishedAt ?? doc?.CreatedAt,
                Summary = doc?.Summary ?? r.Snippet,
                OneSentenceConclusion = doc?.OneSentenceConclusion,
                KeyPoints = doc?.KeyPoints,
                Snippet = r.Snippet,
                RelevanceScore = r.Score,
                SourceRole = "search_result"
            };
        }).ToList();

        return (materials, title);
    }

    // ===== Helpers: Sources =====

    private static async Task<Dictionary<Guid, Source?>> GetSourcesForDocumentsAsync(
        IAppDbContext db,
        List<Document> documents,
        CancellationToken ct)
    {
        var sourceIds = documents.Select(d => d.SourceId).Distinct().ToList();
        var sources = await db.Sources
            .Where(s => sourceIds.Contains(s.Id))
            .ToListAsync(ct);

        var sourceDict = sources.ToDictionary(s => s.Id);

        var result = new Dictionary<Guid, Source?>();
        foreach (var doc in documents)
        {
            sourceDict.TryGetValue(doc.SourceId, out var source);
            result[doc.Id] = source;
        }
        return result;
    }

    // ===== Build Report Context (six-layer structure, §7.4) =====

    private static string BuildReportContext(ReportPlan plan, List<RecalledMaterial> materials)
    {
        var sb = new StringBuilder();

        // Build document index map (unique documents, in order of first appearance).
        // [DOC-n] numbers unique documents; [CIT-n] numbers each material / citation.
        var docIndexMap = new Dictionary<Guid, int>();
        var docCounter = 0;
        foreach (var m in materials)
        {
            if (!docIndexMap.ContainsKey(m.DocumentId))
            {
                docCounter++;
                docIndexMap[m.DocumentId] = docCounter;
            }
        }

        // ===== 第一层：报告目标和结构（从 ReportPlan 生成） =====
        sb.AppendLine("# 报告任务");
        sb.AppendLine($"报告类型：{plan.ReportType}");
        sb.AppendLine($"报告标题：{plan.Title}");
        sb.AppendLine($"报告目标：{plan.Goal}");
        sb.AppendLine();

        sb.AppendLine("# 报告结构");
        foreach (var section in plan.Sections)
        {
            sb.AppendLine($"- {section.Title}");
        }
        sb.AppendLine();

        // ===== 第二层：资料清单（列出所有文档的标题/来源/日期/摘要） =====
        sb.AppendLine("# 资料清单");
        var listedDocs = new HashSet<Guid>();
        foreach (var m in materials)
        {
            if (!listedDocs.Add(m.DocumentId))
            {
                continue;
            }
            var docIndex = docIndexMap[m.DocumentId];
            sb.AppendLine($"[DOC-{docIndex}] 标题：{m.Title}");

            var sourceParts = new List<string>();
            if (!string.IsNullOrEmpty(m.SourceDomain))
            {
                sourceParts.Add(m.SourceDomain);
            }
            if (!string.IsNullOrEmpty(m.SourceType))
            {
                sourceParts.Add(m.SourceType);
            }
            if (!string.IsNullOrEmpty(m.SourceUrl))
            {
                sourceParts.Add(m.SourceUrl);
            }
            sb.AppendLine($"来源：{(sourceParts.Count > 0 ? string.Join(" | ", sourceParts) : "未知")}");
            sb.AppendLine($"日期：{(m.DocumentDate?.ToString("yyyy-MM-dd") ?? "未知")}");

            if (!string.IsNullOrEmpty(m.Summary))
            {
                sb.AppendLine($"摘要：{m.Summary}");
            }

            sb.AppendLine();
        }

        // ===== 第三层：文档级摘要（每个文档的 AI 摘要） =====
        sb.AppendLine("# 文档级摘要");
        listedDocs.Clear();
        foreach (var m in materials)
        {
            if (!listedDocs.Add(m.DocumentId))
            {
                continue;
            }
            var docIndex = docIndexMap[m.DocumentId];
            sb.AppendLine($"[DOC-{docIndex}] {m.Title}");

            if (!string.IsNullOrEmpty(m.OneSentenceConclusion))
            {
                sb.AppendLine($"一句话结论：{m.OneSentenceConclusion}");
            }
            if (!string.IsNullOrEmpty(m.KeyPoints))
            {
                sb.AppendLine($"关键要点：{m.KeyPoints}");
            }
            // Fallback: if no conclusion / key points, repeat the summary as the doc-level digest
            if (string.IsNullOrEmpty(m.OneSentenceConclusion)
                && string.IsNullOrEmpty(m.KeyPoints)
                && !string.IsNullOrEmpty(m.Summary))
            {
                sb.AppendLine($"摘要：{m.Summary}");
            }

            sb.AppendLine();
        }

        // ===== 第四层：关键 chunk 证据（带 [CIT-x] 编号的 chunk 内容） =====
        sb.AppendLine("# 关键证据");
        for (var i = 0; i < materials.Count; i++)
        {
            var m = materials[i];
            var citIndex = i + 1;
            var docIndex = docIndexMap[m.DocumentId];

            var sourceRef = m.ChunkId.HasValue
                ? $"来自 DOC-{docIndex} / CHUNK-{m.ChunkId.Value}"
                : $"来自 DOC-{docIndex}";

            sb.AppendLine($"[CIT-{citIndex}] {sourceRef}");

            // Prefer the raw chunk snippet; fall back to summary / key points as evidence
            var evidence = !string.IsNullOrEmpty(m.Snippet)
                ? m.Snippet
                : (!string.IsNullOrEmpty(m.Summary) ? m.Summary : m.KeyPoints);

            sb.AppendLine(!string.IsNullOrEmpty(evidence)
                ? $"内容：{evidence}"
                : "内容：（无可用证据文本）");

            sb.AppendLine();
        }

        // ===== 第五层：标签/实体/时间线元数据 =====
        sb.AppendLine("# 元数据");
        AppendContextMetadata(sb, materials, docIndexMap);
        sb.AppendLine();

        // ===== 第六层：引用编号说明 =====
        sb.AppendLine("# 引用编号说明");
        sb.AppendLine($"共 {materials.Count} 条引用（[1] - [{materials.Count}]），每条引用对应「关键证据」中的一个 [CIT-n] 编号。");
        sb.AppendLine("在报告中引用资料时，请使用对应的 [n] 编号标注，编号需与上述引用列表一致。");
        sb.AppendLine("[DOC-n] 为资料清单中的文档编号；[CIT-n] 为关键证据中的引用编号。");

        return sb.ToString();
    }

    /// <summary>
    /// 构建第五层元数据：时间线、来源域名分布、来源类型分布、资料角色分布。
    /// （标签/实体的显式元数据在召回阶段尚未携带，此处基于可用元数据进行汇总。）
    /// </summary>
    private static void AppendContextMetadata(
        StringBuilder sb,
        List<RecalledMaterial> materials,
        Dictionary<Guid, int> docIndexMap)
    {
        // 时间线
        var timeline = materials
            .Where(m => m.DocumentDate.HasValue)
            .GroupBy(m => m.DocumentDate!.Value.Date)
            .OrderBy(g => g.Key)
            .ToList();

        if (timeline.Count > 0)
        {
            sb.AppendLine("时间线：");
            foreach (var group in timeline)
            {
                var docIndices = group
                    .Select(m => docIndexMap[m.DocumentId])
                    .Distinct()
                    .OrderBy(x => x)
                    .Select(x => x.ToString());
                sb.AppendLine($"- {group.Key:yyyy-MM-dd}（{group.Count()} 条资料：DOC-{string.Join("/", docIndices)}）");
            }
        }
        else
        {
            sb.AppendLine("时间线：（无可用日期信息）");
        }

        // 来源域名分布
        var domains = materials
            .Where(m => !string.IsNullOrEmpty(m.SourceDomain))
            .GroupBy(m => m.SourceDomain!)
            .OrderByDescending(g => g.Count())
            .Take(10)
            .ToList();

        if (domains.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("来源域名分布：");
            foreach (var group in domains)
            {
                sb.AppendLine($"- {group.Key}（{group.Count()} 条）");
            }
        }

        // 来源类型分布
        var sourceTypes = materials
            .Where(m => !string.IsNullOrEmpty(m.SourceType))
            .GroupBy(m => m.SourceType!)
            .OrderByDescending(g => g.Count())
            .ToList();

        if (sourceTypes.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("来源类型分布：");
            foreach (var group in sourceTypes)
            {
                sb.AppendLine($"- {group.Key}（{group.Count()} 条）");
            }
        }

        // 资料角色分布
        var roles = materials
            .GroupBy(m => m.SourceRole)
            .OrderByDescending(g => g.Count())
            .ToList();

        if (roles.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("资料角色分布：");
            foreach (var group in roles)
            {
                sb.AppendLine($"- {group.Key}（{group.Count()} 条）");
            }
        }
    }

    // ===== Build User Prompt =====

    private static string BuildUserPrompt(
        string? userPromptTemplate,
        string reportType,
        string reportContext,
        List<RecalledMaterial> materials,
        InputParams inputParams,
        string title)
    {
        var docCount = materials.Count;
        var focusCount = materials.Count(m => m.SourceRole == "focus");

        if (!string.IsNullOrEmpty(userPromptTemplate))
        {
            // Replace placeholders in the template
            var prompt = userPromptTemplate
                .Replace("{doc_count}", docCount.ToString())
                .Replace("{report_context}", reportContext)
                .Replace("{title}", title)
                .Replace("{question}", inputParams.Question ?? "")
                .Replace("{date}", (inputParams.Date?.ToString("yyyy-MM-dd") ?? DateTime.UtcNow.ToString("yyyy-MM-dd")))
                .Replace("{start_date}", (inputParams.StartDate?.ToString("yyyy-MM-dd") ?? ""))
                .Replace("{end_date}", (inputParams.EndDate?.ToString("yyyy-MM-dd") ?? ""))
                .Replace("{focus_count}", focusCount.ToString());

            return prompt;
        }

        // Fallback: build prompt without template
        return reportType.ToLowerInvariant() switch
        {
            "daily" => $@"请基于以下 {docCount} 篇资料，生成 {inputParams.Date?.ToString("yyyy-MM-dd") ?? DateTime.UtcNow.ToString("yyyy-MM-dd")} 的知识日报。

参考资料：
{reportContext}

请按照概述、重要发现、详细分析、趋势与信号、建议关注、参考来源的结构生成日报。",

            "weekly" => $@"请基于以下 {docCount} 篇资料，生成 {inputParams.StartDate?.ToString("yyyy-MM-dd") ?? ""} 至 {inputParams.EndDate?.ToString("yyyy-MM-dd") ?? ""} 的知识周报。

参考资料（重点资料 {focusCount} 篇）：
{reportContext}

请按照本周概述、核心主题、重要进展、趋势分析、风险与机遇、下周建议、参考来源的结构生成周报。",

            "topic" => $@"请基于以下 {docCount} 篇资料，针对以下问题生成专题报告。

报告标题：{title}
研究问题：{inputParams.Question ?? ""}

参考资料：
{reportContext}

请按照摘要、背景与上下文、核心发现、深度分析、多方观点、数据与证据、结论与建议、参考来源的结构生成专题报告。",

            _ => $@"请基于以下资料生成报告。

参考资料：
{reportContext}"
        };
    }

    // ===== Build Section Prompt (for segmented generation) =====

    private static string BuildSectionPrompt(
        ReportSectionPlan section,
        string reportContext,
        string reportType,
        InputParams inputParams,
        string title)
    {
        return $@"你正在生成一份专题报告的其中一个章节。请仅生成当前章节的内容。

报告标题：{title}
研究问题：{inputParams.Question ?? ""}

当前章节：{section.Title}
章节目的：{section.Purpose}
期望证据类型：{section.ExpectedEvidenceType}
最少引用数：{section.MinCitations}

参考资料：
{reportContext}

请仅生成「{section.Title}」这一章节的内容，不要生成其他章节。
在相关信息处标注引用编号，如 [1]、[2] 等，引用编号对应上述参考资料的序号。
使用中文撰写，内容要深入、有分析性。";
    }

    // ===== Build Default System Prompt =====

    private static string BuildDefaultSystemPrompt(string reportType)
    {
        return reportType.ToLowerInvariant() switch
        {
            "daily" => @"你是一个专业的知识管理助手，负责生成每日知识摘要报告。
规则：
1. 仅基于提供的参考资料生成报告，不要编造或使用资料之外的信息
2. 在相关信息处标注引用编号，如 [1]、[2] 等
3. 报告要结构清晰，重点突出
4. 使用中文撰写",

            "weekly" => @"你是一个专业的知识管理助手，负责生成每周知识总结报告。
规则：
1. 仅基于提供的参考资料生成报告，不要编造或使用资料之外的信息
2. 在相关信息处标注引用编号，如 [1]、[2] 等
3. 报告要具有回顾性和前瞻性
4. 使用中文撰写",

            "topic" => @"你是一个专业的知识管理助手，负责生成专题深度分析报告。
规则：
1. 仅基于提供的参考资料生成报告，不要编造或使用资料之外的信息
2. 在相关信息处标注引用编号，如 [1]、[2] 等
3. 报告要深入、全面，具有分析性和洞察力
4. 使用中文撰写
5. 展示不同观点和角度，保持客观中立",

            _ => @"你是一个专业的知识管理助手。仅基于提供的参考资料生成报告，不要编造信息。使用中文撰写。"
        };
    }

    // ===== Build Citations =====

    private static List<CitationItem> BuildCitations(List<RecalledMaterial> materials)
    {
        var citations = new List<CitationItem>();

        for (var i = 0; i < materials.Count; i++)
        {
            var m = materials[i];
            citations.Add(new CitationItem
            {
                Index = i + 1,
                DocumentId = m.DocumentId,
                ChunkId = m.ChunkId,
                Title = m.Title,
                SourceUrl = m.SourceUrl,
                SourceDomain = m.SourceDomain,
                SourceType = m.SourceType,
                RelevanceScore = m.RelevanceScore,
                SourceRole = m.SourceRole
            });
        }

        return citations;
    }

    // ===== Validate Citations =====

    private static readonly Regex CitationMarkerRegex = new(@"\[(\d{1,3})\]", RegexOptions.Compiled);

    /// <summary>
    /// Validates that citation markers ([1], [2], ...) in the LLM output refer
    /// to indices that exist in the system-generated citations list.
    /// Mismatches are logged as warnings but do not block the flow.
    /// </summary>
    private void ValidateCitations(string content, int citationCount, Guid jobId)
    {
        if (citationCount <= 0 || string.IsNullOrEmpty(content))
        {
            return;
        }

        var citedIndices = new HashSet<int>();
        foreach (Match match in CitationMarkerRegex.Matches(content))
        {
            if (int.TryParse(match.Groups[1].Value, out var idx))
            {
                citedIndices.Add(idx);
            }
        }

        var invalid = citedIndices.Where(i => i < 1 || i > citationCount).OrderBy(i => i).ToList();
        if (invalid.Count > 0)
        {
            _logger.LogWarning(
                "Report job {JobId}: LLM output references citation indices [{InvalidIndices}] that are not in the system-generated citations list (valid range: 1-{Max}). This will not block the report.",
                jobId, string.Join(", ", invalid), citationCount);
        }
    }

    // ===== Get Start/End Date =====

    private static DateTime? GetStartDate(string reportType, InputParams inputParams)
    {
        return reportType.ToLowerInvariant() switch
        {
            "daily" => inputParams.Date,
            "weekly" => inputParams.StartDate,
            "topic" => inputParams.DateFrom,
            _ => null
        };
    }

    private static DateTime? GetEndDate(string reportType, InputParams inputParams)
    {
        return reportType.ToLowerInvariant() switch
        {
            "daily" => inputParams.Date,
            "weekly" => inputParams.EndDate,
            "topic" => inputParams.DateTo,
            _ => null
        };
    }

    // ===== Handle Job Failure =====

    private async Task HandleJobFailureAsync(Guid jobId, Exception ex, CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();

            var job = await db.ReportJobs.FirstOrDefaultAsync(j => j.Id == jobId, ct);
            if (job == null) return;

            var truncatedMessage = ex.Message.Length > 1900 ? ex.Message.Substring(0, 1900) : ex.Message;

            if (job.RetryCount < MaxRetries)
            {
                var currentRetry = job.RetryCount; // 0 = first failure, 1 = second, 2 = third
                job.RetryCount++;
                job.Status = "pending";
                job.ErrorCode = ex.GetType().Name;
                job.ErrorMessage = truncatedMessage;

                // Exponential backoff for retries:
                //   first failure  (RetryCount 0 -> 1): immediate retry
                //   second failure (RetryCount 1 -> 2): delay 1 minute
                //   third failure  (RetryCount 2 -> 3): delay 5 minutes
                // PollAndProcessAsync only picks up jobs where StartedAt is null or in the past.
                job.StartedAt = currentRetry switch
                {
                    0 => null,                              // immediate retry
                    1 => DateTime.UtcNow.AddMinutes(1),     // 1 minute backoff
                    2 => DateTime.UtcNow.AddMinutes(5),     // 5 minutes backoff
                    _ => null
                };

                _logger.LogWarning(
                    "Report job {JobId} failed (retry {RetryCount}/{MaxRetries}). Scheduled retry at {RetryAt}.",
                    jobId, job.RetryCount, MaxRetries,
                    job.StartedAt?.ToString("o") ?? "now");
            }
            else
            {
                job.Status = "failed";
                job.ErrorCode = ex.GetType().Name;
                job.ErrorMessage = truncatedMessage;
                job.FinishedAt = DateTime.UtcNow;

                _logger.LogError(
                    "Report job {JobId} permanently failed after {MaxRetries} retries.", jobId, MaxRetries);
            }

            await db.SaveChangesAsync(ct);
        }
        catch (Exception retryEx)
        {
            _logger.LogError(retryEx, "Failed to handle job failure for {JobId}", jobId);
        }
    }

    // ===== Parse Input Params =====

    private static InputParams ParseInputParams(string? inputParamsJson)
    {
        if (string.IsNullOrEmpty(inputParamsJson))
        {
            return new InputParams();
        }

        try
        {
            var dto = JsonSerializer.Deserialize<InputParamsDto>(inputParamsJson, JsonOptions);
            return new InputParams
            {
                TopicId = dto?.TopicId,
                Date = dto?.Date,
                StartDate = dto?.StartDate,
                EndDate = dto?.EndDate,
                Title = dto?.Title,
                Question = dto?.Question,
                DateFrom = dto?.DateFrom,
                DateTo = dto?.DateTo,
                MinValueScore = dto?.MinValueScore,
                Depth = dto?.Depth
            };
        }
        catch
        {
            return new InputParams();
        }
    }

    // ===== Inner Types =====

    private class InputParamsDto
    {
        public Guid? TopicId { get; set; }
        public DateTime? Date { get; set; }
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public string? Title { get; set; }
        public string? Question { get; set; }
        public DateTime? DateFrom { get; set; }
        public DateTime? DateTo { get; set; }
        public int? MinValueScore { get; set; }
        public string? Depth { get; set; }
    }

    private class InputParams
    {
        public Guid? TopicId { get; set; }
        public DateTime? Date { get; set; }
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public string? Title { get; set; }
        public string? Question { get; set; }
        public DateTime? DateFrom { get; set; }
        public DateTime? DateTo { get; set; }
        public int? MinValueScore { get; set; }
        public string? Depth { get; set; }
    }

    private class RecalledMaterial
    {
        public Guid DocumentId { get; set; }
        public Guid? ChunkId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string? SourceUrl { get; set; }
        public string? SourceDomain { get; set; }
        public string? SourceType { get; set; }
        public DateTime? DocumentDate { get; set; }
        public string? Summary { get; set; }
        public string? OneSentenceConclusion { get; set; }
        public string? KeyPoints { get; set; }
        public string? Snippet { get; set; }
        public double RelevanceScore { get; set; }
        public string SourceRole { get; set; } = "document";
    }
}
