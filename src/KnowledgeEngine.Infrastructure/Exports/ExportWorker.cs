using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using KnowledgeEngine.Application.DTOs;
using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Application.Settings;
using KnowledgeEngine.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KnowledgeEngine.Infrastructure.Exports;

public class ExportWorker : BackgroundService
{
    private static readonly TimeSpan PollingInterval = TimeSpan.FromSeconds(5);
    private const int MaxRetries = 3;
    private const string ExportBucket = "knowledge-engine";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ExportWorker> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true
    };

    public ExportWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<ExportWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ExportWorker started. Polling every {Interval}s.", PollingInterval.TotalSeconds);

        await Task.Delay(TimeSpan.FromSeconds(6), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PollAndProcessAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in ExportWorker polling cycle");
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

        _logger.LogInformation("ExportWorker stopped.");
    }

    private async Task PollAndProcessAsync(CancellationToken ct)
    {
        List<ExportJob> pendingJobs;

        using (var scope = _scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();

            pendingJobs = await db.ExportJobs
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

        _logger.LogInformation("ExportWorker found {Count} pending export job(s).", pendingJobs.Count);

        foreach (var job in pendingJobs)
        {
            if (ct.IsCancellationRequested) break;

            try
            {
                await ProcessJobAsync(job.Id, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ExportWorker failed to process job {JobId}", job.Id);
                await HandleJobFailureAsync(job.Id, ex, ct);
            }
        }
    }

    private async Task ProcessJobAsync(Guid jobId, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        var fileStorage = scope.ServiceProvider.GetRequiredService<IFileStorageProvider>();
        var searchService = scope.ServiceProvider.GetRequiredService<ISearchService>();
        var minioSettings = scope.ServiceProvider.GetRequiredService<IOptions<MinioSettings>>().Value;

        // Step 1: Mark job as processing
        var job = await db.ExportJobs.FirstOrDefaultAsync(j => j.Id == jobId, ct);
        if (job == null || job.Status != "pending")
        {
            return;
        }

        job.Status = "processing";
        job.StartedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        _logger.LogInformation("Processing export job {JobId} (type: {ExportType}, target: {TargetType})",
            jobId, job.ExportType, job.TargetType);

        var bucket = !string.IsNullOrEmpty(minioSettings.Bucket) ? minioSettings.Bucket : ExportBucket;
        var now = DateTime.UtcNow;
        var objectKey = $"exports/{job.UserId}/{job.Id}";

        string fileName;
        string contentType;
        byte[] contentBytes;

        // Step 2: Generate content based on export type and target type
        var exportKey = $"{job.ExportType.ToLowerInvariant()}:{job.TargetType.ToLowerInvariant()}";

        switch (exportKey)
        {
            case "markdown:document":
                (fileName, contentType, contentBytes) = await ExportDocumentMarkdownAsync(db, job, ct);
                break;
            case "markdown:report":
                (fileName, contentType, contentBytes) = await ExportReportMarkdownAsync(db, job, ct);
                break;
            case "obsidian:topic":
                (fileName, contentType, contentBytes) = await ExportTopicObsidianAsync(db, job, ct);
                break;
            case "json:search":
                (fileName, contentType, contentBytes) = await ExportSearchJsonAsync(searchService, job, ct);
                break;
            case "json:report":
                (fileName, contentType, contentBytes) = await ExportReportJsonAsync(db, job, ct);
                break;
            default:
                throw new InvalidOperationException($"Unsupported export type/target: {exportKey}");
        }

        // Append extension to object key
        var fileExtension = GetFileExtension(job.ExportType);
        objectKey += fileExtension;

        // Step 3: Upload to MinIO
        using var stream = new MemoryStream(contentBytes);
        await fileStorage.UploadFileAsync(bucket, objectKey, stream, contentType, contentBytes.Length, ct);

        // Step 4: Create FileObject record
        var fileObject = new FileObject
        {
            Id = Guid.NewGuid(),
            WorkspaceId = job.UserId,
            Bucket = bucket,
            ObjectKey = objectKey,
            OriginalFilename = fileName,
            MimeType = contentType,
            SizeBytes = contentBytes.Length,
            Sha256 = ComputeHash(contentBytes),
            StorageProvider = "minio",
            CreatedAt = now
        };

        db.Files.Add(fileObject);

        // Step 5: Update export job
        job.FileId = fileObject.Id;
        job.Status = "done";
        job.FinishedAt = now;
        await db.SaveChangesAsync(ct);

        _logger.LogInformation("Export job {JobId} completed. File ID: {FileId}", jobId, fileObject.Id);
    }

    // ===== Export Document Markdown =====

    private static async Task<(string, string, byte[])> ExportDocumentMarkdownAsync(
        IAppDbContext db,
        ExportJob job,
        CancellationToken ct)
    {
        var doc = await db.Documents
            .FirstOrDefaultAsync(d => d.Id == job.TargetId && d.UserId == job.UserId, ct)
            ?? throw new InvalidOperationException("Document not found");

        var source = await db.Sources.FirstOrDefaultAsync(s => s.Id == doc.SourceId, ct);

        var parameters = ParseParams(job.Params);
        var includeAiSummary = parameters?.IncludeAiSummary ?? true;
        var includeMetadata = parameters?.IncludeMetadata ?? true;

        var sb = new StringBuilder();

        sb.AppendLine($"# {doc.Title}");
        sb.AppendLine();

        if (includeMetadata)
        {
            sb.AppendLine("## 元信息");
            sb.AppendLine($"- 创建时间: {doc.CreatedAt:yyyy-MM-dd HH:mm:ss} UTC");
            sb.AppendLine($"- 更新时间: {doc.UpdatedAt:yyyy-MM-dd HH:mm:ss} UTC");
            sb.AppendLine($"- 语言: {doc.Language ?? "未知"}");
            sb.AppendLine($"- 字数: {doc.WordCount?.ToString() ?? "未知"}");
            sb.AppendLine($"- 阅读时间: {doc.ReadingTimeMinutes?.ToString() ?? "未知"} 分钟");
            sb.AppendLine($"- 价值评分: {doc.ValueScore?.ToString() ?? "未评估"}");
            if (source != null)
            {
                sb.AppendLine($"- 来源: {source.SourceType}");
                sb.AppendLine($"- 来源URL: {source.Url ?? "无"}");
                sb.AppendLine($"- 来源域名: {source.Domain ?? "未知"}");
                if (source.Author != null)
                {
                    sb.AppendLine($"- 作者: {source.Author}");
                }
                if (source.PublishedAt.HasValue)
                {
                    sb.AppendLine($"- 发布时间: {source.PublishedAt:yyyy-MM-dd HH:mm:ss} UTC");
                }
            }
            sb.AppendLine();
        }

        if (includeAiSummary)
        {
            if (!string.IsNullOrEmpty(doc.Summary))
            {
                sb.AppendLine("## 摘要");
                sb.AppendLine(doc.Summary);
                sb.AppendLine();
            }

            if (!string.IsNullOrEmpty(doc.OneSentenceConclusion))
            {
                sb.AppendLine("## 一句话结论");
                sb.AppendLine(doc.OneSentenceConclusion);
                sb.AppendLine();
            }

            if (!string.IsNullOrEmpty(doc.KeyPoints))
            {
                sb.AppendLine("## 关键要点");
                sb.AppendLine(doc.KeyPoints);
                sb.AppendLine();
            }

            if (!string.IsNullOrEmpty(doc.BusinessSignals))
            {
                sb.AppendLine("## 商业信号");
                sb.AppendLine(doc.BusinessSignals);
                sb.AppendLine();
            }

            if (!string.IsNullOrEmpty(doc.TechnicalSignals))
            {
                sb.AppendLine("## 技术信号");
                sb.AppendLine(doc.TechnicalSignals);
                sb.AppendLine();
            }

            if (!string.IsNullOrEmpty(doc.Risks))
            {
                sb.AppendLine("## 风险");
                sb.AppendLine(doc.Risks);
                sb.AppendLine();
            }

            if (!string.IsNullOrEmpty(doc.Opportunities))
            {
                sb.AppendLine("## 机会");
                sb.AppendLine(doc.Opportunities);
                sb.AppendLine();
            }

            if (!string.IsNullOrEmpty(doc.ReusableMaterials))
            {
                sb.AppendLine("## 可复用材料");
                sb.AppendLine(doc.ReusableMaterials);
                sb.AppendLine();
            }
        }

        if (!string.IsNullOrEmpty(doc.ContentMarkdown))
        {
            sb.AppendLine("## 正文内容");
            sb.AppendLine(doc.ContentMarkdown);
        }

        var fileName = $"{SafeFileName(doc.Title)}.md";
        var content = Encoding.UTF8.GetBytes(sb.ToString());
        return (fileName, "text/markdown; charset=utf-8", content);
    }

    // ===== Export Report Markdown =====

    private static async Task<(string, string, byte[])> ExportReportMarkdownAsync(
        IAppDbContext db,
        ExportJob job,
        CancellationToken ct)
    {
        var report = await db.Reports
            .FirstOrDefaultAsync(r => r.Id == job.TargetId && r.UserId == job.UserId, ct)
            ?? throw new InvalidOperationException("Report not found");

        // Fetch topic name for front matter
        string? topicName = null;
        if (report.TopicId.HasValue)
        {
            var topic = await db.Topics
                .FirstOrDefaultAsync(t => t.Id == report.TopicId.Value, ct);
            topicName = topic?.Name;
        }

        // Fetch source documents via ReportSource
        var reportSources = await db.ReportSources
            .Where(rs => rs.ReportId == report.Id)
            .OrderBy(rs => rs.CitationIndex)
            .ToListAsync(ct);

        var documentIds = reportSources.Select(rs => rs.DocumentId).Distinct().ToList();
        var documents = documentIds.Count > 0
            ? await db.Documents.Where(d => documentIds.Contains(d.Id)).ToListAsync(ct)
            : new List<Document>();
        var docDict = documents.ToDictionary(d => d.Id);

        var sourceIds = documents.Select(d => d.SourceId).Distinct().ToList();
        var sources = sourceIds.Count > 0
            ? await db.Sources.Where(s => sourceIds.Contains(s.Id)).ToListAsync(ct)
            : new List<Source>();
        var sourceDict = sources.ToDictionary(s => s.Id);

        var sb = new StringBuilder();

        // YAML Front Matter
        sb.AppendLine("---");
        sb.AppendLine("type: report");
        sb.AppendLine($"report_id: \"{report.Id}\"");
        sb.AppendLine($"workspace_id: \"{report.UserId}\"");
        sb.AppendLine($"topic: \"{EscapeYaml(topicName ?? string.Empty)}\"");
        sb.AppendLine($"report_type: \"{EscapeYaml(report.ReportType)}\"");
        if (report.StartDate.HasValue)
        {
            sb.AppendLine($"start_date: \"{report.StartDate:yyyy-MM-dd}\"");
        }
        if (report.EndDate.HasValue)
        {
            sb.AppendLine($"end_date: \"{report.EndDate:yyyy-MM-dd}\"");
        }
        sb.AppendLine($"created_at: \"{report.CreatedAt.ToUniversalTime():yyyy-MM-ddTHH:mm:ssZ}\"");
        sb.AppendLine("---");
        sb.AppendLine();

        // Report content
        sb.AppendLine(report.ContentMarkdown);

        // Source list
        if (reportSources.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## 来源列表");
            sb.AppendLine();

            var index = 1;
            var seenDocs = new HashSet<Guid>();
            foreach (var rs in reportSources)
            {
                if (!docDict.TryGetValue(rs.DocumentId, out var doc))
                {
                    continue;
                }

                if (!seenDocs.Add(doc.Id))
                {
                    continue;
                }

                sourceDict.TryGetValue(doc.SourceId, out var source);
                var title = !string.IsNullOrEmpty(doc.Title) ? doc.Title : (source?.Title ?? "未命名资料");
                var url = source?.Url ?? doc.SourceUrl ?? "#";

                sb.AppendLine($"{index}. [{title}]({url})  ");
                sb.AppendLine($"   - document_id: {doc.Id}");

                // Collect citation indices for this document
                var citations = reportSources
                    .Where(r => r.DocumentId == doc.Id && r.CitationIndex.HasValue)
                    .Select(r => $"CIT-{r.CitationIndex}")
                    .Distinct()
                    .ToList();
                if (citations.Count > 0)
                {
                    sb.AppendLine($"   - 引用：{string.Join(", ", citations)}");
                }

                sb.AppendLine();
                index++;
            }
        }

        var fileName = $"{SafeFileName(report.Title)}.md";
        var content = Encoding.UTF8.GetBytes(sb.ToString());
        return (fileName, "text/markdown; charset=utf-8", content);
    }

    // ===== Export Topic Obsidian =====

    private static async Task<(string, string, byte[])> ExportTopicObsidianAsync(
        IAppDbContext db,
        ExportJob job,
        CancellationToken ct)
    {
        var topic = await db.Topics
            .FirstOrDefaultAsync(t => t.Id == job.TargetId && t.UserId == job.UserId, ct)
            ?? throw new InvalidOperationException("Topic not found");

        var parameters = ParseParams(job.Params);
        var includeDocuments = parameters?.IncludeDocuments ?? true;
        var includeReports = parameters?.IncludeReports ?? true;
        var includeAiSummary = parameters?.IncludeAiSummary ?? true;

        var topicSlug = SafeFileName(topic.Name);
        var now = DateTime.UtcNow;
        var reportTagsLine = "#日报 #周报 #专题报告";

        // Fetch documents
        var documents = new List<Document>();
        var sourceDict = new Dictionary<Guid, Source>();
        var docFileNames = new List<(Document Doc, string FileName)>();

        if (includeDocuments)
        {
            documents = await db.Documents
                .Where(d => d.TopicId == topic.Id && d.UserId == job.UserId)
                .OrderByDescending(d => d.CreatedAt)
                .ToListAsync(ct);

            var sourceIds = documents.Select(d => d.SourceId).Distinct().ToList();
            var sources = sourceIds.Count > 0
                ? await db.Sources.Where(s => sourceIds.Contains(s.Id)).ToListAsync(ct)
                : new List<Source>();
            sourceDict = sources.ToDictionary(s => s.Id);

            foreach (var doc in documents)
            {
                var docFileName = $"{doc.CreatedAt:yyyy-MM-dd}-{SafeFileName(doc.Title)}.md";
                docFileNames.Add((doc, docFileName));
            }
        }

        // Fetch reports
        var reports = new List<Report>();
        var reportFileNames = new List<(Report Report, string FileName)>();

        if (includeReports)
        {
            reports = await db.Reports
                .Where(r => r.TopicId == topic.Id && r.UserId == job.UserId)
                .OrderByDescending(r => r.CreatedAt)
                .ToListAsync(ct);

            foreach (var report in reports)
            {
                var reportFileName = $"{report.CreatedAt:yyyy-MM-dd}-{SafeFileName(report.Title)}.md";
                reportFileNames.Add((report, reportFileName));
            }
        }

        using var memoryStream = new MemoryStream();
        using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, true))
        {
            // README.md (ZIP root)
            var readmeSb = new StringBuilder();
            readmeSb.AppendLine($"# {topic.Name} - Obsidian 导出");
            readmeSb.AppendLine();
            readmeSb.AppendLine($"本压缩包为专题 **{topic.Name}** 的 Obsidian 格式导出，导出时间：{now:yyyy-MM-dd HH:mm:ss} UTC。");
            readmeSb.AppendLine();
            readmeSb.AppendLine("## 导出结构");
            readmeSb.AppendLine();
            readmeSb.AppendLine("```");
            readmeSb.AppendLine($"{topicSlug}/");
            readmeSb.AppendLine("├── index.md              # 专题首页，包含文档与报告的双链索引");
            readmeSb.AppendLine("├── documents/            # 文档 Markdown 文件");
            if (includeDocuments)
            {
                foreach (var (doc, fn) in docFileNames)
                {
                    readmeSb.AppendLine($"│   ├── {fn}");
                }
            }
            readmeSb.AppendLine("├── reports/              # 报告 Markdown 文件");
            if (includeReports)
            {
                foreach (var (rep, fn) in reportFileNames)
                {
                    readmeSb.AppendLine($"│   ├── {fn}");
                }
            }
            readmeSb.AppendLine("└── sources/");
            readmeSb.AppendLine("    └── sources.json      # 来源元数据");
            readmeSb.AppendLine("```");
            readmeSb.AppendLine();
            readmeSb.AppendLine("## 说明");
            readmeSb.AppendLine();
            readmeSb.AppendLine("- 所有文档和报告均使用 Obsidian 双链 `[[文件名]]` 进行关联。");
            readmeSb.AppendLine($"- 文档和报告中包含 `相关专题：[[{topicSlug}]]` 双链及标签 `{reportTagsLine}`。");
            readmeSb.AppendLine("- index.md 中为每个文档和报告提供了 `[[文件名]]` 形式的双链。");
            readmeSb.AppendLine("- 可直接将压缩包内容解压到 Obsidian Vault 中使用。");
            readmeSb.AppendLine();
            AddZipEntry(archive, "README.md", readmeSb.ToString());

            // export_manifest.json (ZIP root)
            var manifest = JsonSerializer.Serialize(new
            {
                export_version = "1.0",
                exported_at = now,
                topic = new
                {
                    id = topic.Id,
                    name = topic.Name,
                    domain = topic.Domain
                },
                include_documents = includeDocuments,
                include_reports = includeReports,
                include_ai_summary = includeAiSummary,
                document_count = docFileNames.Count,
                report_count = reportFileNames.Count,
                total_files = docFileNames.Count + reportFileNames.Count + 2
            }, JsonOptions);
            AddZipEntry(archive, "export_manifest.json", manifest);

            // index.md
            var indexSb = new StringBuilder();
            indexSb.AppendLine($"# {topic.Name}");
            indexSb.AppendLine();
            if (!string.IsNullOrEmpty(topic.Description))
            {
                indexSb.AppendLine(topic.Description);
                indexSb.AppendLine();
            }
            indexSb.AppendLine($"- 域名: {topic.Domain ?? "未设置"}");
            indexSb.AppendLine($"- 可见性: {topic.Visibility}");
            indexSb.AppendLine($"- 状态: {topic.Status}");
            indexSb.AppendLine($"- 创建时间: {topic.CreatedAt:yyyy-MM-dd}");
            indexSb.AppendLine();

            if (includeDocuments && docFileNames.Count > 0)
            {
                indexSb.AppendLine("## 文档列表");
                indexSb.AppendLine();
                foreach (var (doc, fn) in docFileNames)
                {
                    var linkName = Path.GetFileNameWithoutExtension(fn);
                    indexSb.AppendLine($"- [[{linkName}|{doc.Title}]]");
                }
                indexSb.AppendLine();
            }

            if (includeReports && reportFileNames.Count > 0)
            {
                indexSb.AppendLine("## 报告列表");
                indexSb.AppendLine();
                foreach (var (rep, fn) in reportFileNames)
                {
                    var linkName = Path.GetFileNameWithoutExtension(fn);
                    indexSb.AppendLine($"- [[{linkName}|{rep.Title}]]");
                }
                indexSb.AppendLine();
            }

            AddZipEntry(archive, $"{topicSlug}/index.md", indexSb.ToString());

            // documents
            if (includeDocuments)
            {
                foreach (var (doc, docFileName) in docFileNames)
                {
                    var docSb = new StringBuilder();
                    docSb.AppendLine($"相关专题：[[{topicSlug}]]");
                    docSb.AppendLine($"相关标签：{reportTagsLine}");
                    docSb.AppendLine();
                    docSb.AppendLine($"# {doc.Title}");
                    docSb.AppendLine();
                    docSb.AppendLine($"- 文档ID: {doc.Id}");
                    docSb.AppendLine($"- 创建时间: {doc.CreatedAt:yyyy-MM-dd}");
                    docSb.AppendLine($"- 价值评分: {doc.ValueScore?.ToString() ?? "未评估"}");

                    sourceDict.TryGetValue(doc.SourceId, out var source);
                    if (source != null)
                    {
                        docSb.AppendLine($"- 来源: {source.SourceType}");
                        if (source.Url != null)
                        {
                            docSb.AppendLine($"- URL: {source.Url}");
                        }
                    }

                    docSb.AppendLine();

                    if (includeAiSummary)
                    {
                        if (!string.IsNullOrEmpty(doc.Summary))
                        {
                            docSb.AppendLine("## 摘要");
                            docSb.AppendLine(doc.Summary);
                            docSb.AppendLine();
                        }

                        if (!string.IsNullOrEmpty(doc.OneSentenceConclusion))
                        {
                            docSb.AppendLine("## 一句话结论");
                            docSb.AppendLine(doc.OneSentenceConclusion);
                            docSb.AppendLine();
                        }

                        if (!string.IsNullOrEmpty(doc.KeyPoints))
                        {
                            docSb.AppendLine("## 关键要点");
                            docSb.AppendLine(doc.KeyPoints);
                            docSb.AppendLine();
                        }
                    }

                    if (!string.IsNullOrEmpty(doc.ContentMarkdown))
                    {
                        docSb.AppendLine("## 正文");
                        docSb.AppendLine(doc.ContentMarkdown);
                    }

                    AddZipEntry(archive, $"{topicSlug}/documents/{docFileName}", docSb.ToString());
                }
            }

            // reports
            if (includeReports)
            {
                foreach (var (report, reportFileName) in reportFileNames)
                {
                    var reportSb = new StringBuilder();
                    reportSb.AppendLine($"相关专题：[[{topicSlug}]]");
                    reportSb.AppendLine($"相关标签：{reportTagsLine}");
                    reportSb.AppendLine();
                    reportSb.AppendLine(report.ContentMarkdown);

                    AddZipEntry(archive, $"{topicSlug}/reports/{reportFileName}", reportSb.ToString());
                }
            }

            // sources.json
            var sourcesJson = JsonSerializer.Serialize(new
            {
                topic = new
                {
                    id = topic.Id,
                    name = topic.Name,
                    description = topic.Description,
                    domain = topic.Domain,
                    created_at = topic.CreatedAt
                },
                exported_at = now,
                include_documents = includeDocuments,
                include_reports = includeReports,
                include_ai_summary = includeAiSummary
            }, JsonOptions);

            AddZipEntry(archive, $"{topicSlug}/sources/sources.json", sourcesJson);
        }

        var fileName = $"{topicSlug}-obsidian.zip";
        var content = memoryStream.ToArray();
        return (fileName, "application/zip", content);
    }

    // ===== Export Search JSON =====

    private static async Task<(string, string, byte[])> ExportSearchJsonAsync(
        ISearchService searchService,
        ExportJob job,
        CancellationToken ct)
    {
        var parameters = ParseSearchParams(job.Params);
        var query = parameters?.Query ?? "";
        var topicId = parameters?.TopicId ?? job.TopicId;
        var filters = parameters?.Filters ?? new SearchFilters();

        var searchRequest = new SearchRequest
        {
            TopicId = topicId,
            Query = query,
            SearchType = "hybrid",
            Filters = filters,
            Limit = 50
        };

        var searchResponse = await searchService.SearchAsync(job.UserId, searchRequest, ct);
        var searchResult = searchResponse.Data ?? new SearchResult();

        var exportData = new
        {
            query = searchResult.Query,
            search_type = searchResult.SearchType,
            total = searchResult.Total,
            exported_at = DateTime.UtcNow,
            items = searchResult.Items
        };

        var json = JsonSerializer.Serialize(exportData, JsonOptions);
        var fileName = $"search-{SafeFileName(query)}.json";
        var content = Encoding.UTF8.GetBytes(json);
        return (fileName, "application/json; charset=utf-8", content);
    }

    // ===== Export Report JSON =====

    private static async Task<(string, string, byte[])> ExportReportJsonAsync(
        IAppDbContext db,
        ExportJob job,
        CancellationToken ct)
    {
        var report = await db.Reports
            .FirstOrDefaultAsync(r => r.Id == job.TargetId && r.UserId == job.UserId, ct)
            ?? throw new InvalidOperationException("Report not found");

        // Parse citations JSONB field if present
        object? citations = null;
        if (!string.IsNullOrEmpty(report.Citations))
        {
            try
            {
                citations = JsonSerializer.Deserialize<JsonElement>(report.Citations);
            }
            catch
            {
                citations = null;
            }
        }

        var exportData = new
        {
            export_version = "1.0",
            report = new
            {
                id = report.Id,
                title = report.Title,
                report_type = report.ReportType,
                content_markdown = report.ContentMarkdown,
                citations
            }
        };

        var json = JsonSerializer.Serialize(exportData, JsonOptions);
        var fileName = $"{SafeFileName(report.Title)}.json";
        var content = Encoding.UTF8.GetBytes(json);
        return (fileName, "application/json; charset=utf-8", content);
    }

    // ===== Helpers =====

    private static void AddZipEntry(ZipArchive archive, string entryName, string content)
    {
        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        using var entryStream = entry.Open();
        using var writer = new StreamWriter(entryStream, Encoding.UTF8);
        writer.Write(content);
    }

    private static string SafeFileName(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return "untitled";
        }

        // Remove special characters, replace spaces with -
        var safe = name.Trim();
        var chars = safe.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            var c = chars[i];
            if (char.IsLetterOrDigit(c) || c == '-' || c == '_')
            {
                // keep
            }
            else if (c == ' ')
            {
                chars[i] = '-';
            }
            else
            {
                chars[i] = '-';
            }
        }

        safe = new string(chars);

        // Collapse consecutive dashes
        while (safe.Contains("--"))
        {
            safe = safe.Replace("--", "-");
        }

        safe = safe.Trim('-');

        // Limit length
        if (safe.Length > 100)
        {
            safe = safe.Substring(0, 100);
        }

        return string.IsNullOrEmpty(safe) ? "untitled" : safe;
    }

    private static string EscapeYaml(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\n", " ")
            .Replace("\r", " ");
    }

    private static string GetFileExtension(string exportType)
    {
        return exportType.ToLowerInvariant() switch
        {
            "markdown" => ".md",
            "obsidian" => ".zip",
            "json" => ".json",
            _ => ".txt"
        };
    }

    private static string ComputeHash(byte[] data)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        var hashBytes = sha.ComputeHash(data);
        return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
    }

    // ===== Parse Params =====

    private static ExportParams? ParseParams(string? json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<ExportParams>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private static SearchExportParams? ParseSearchParams(string? json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<SearchExportParams>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    // ===== Handle Job Failure =====

    private async Task HandleJobFailureAsync(Guid jobId, Exception ex, CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();

            var job = await db.ExportJobs.FirstOrDefaultAsync(j => j.Id == jobId, ct);
            if (job == null) return;

            var truncatedMessage = ex.Message.Length > 1900 ? ex.Message.Substring(0, 1900) : ex.Message;

            if (job.RetryCount < MaxRetries)
            {
                var currentRetry = job.RetryCount; // 0 = first failure, 1 = second, 2 = third
                job.RetryCount++;
                job.Status = "pending";
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
                    "Export job {JobId} failed (retry {RetryCount}/{MaxRetries}). Scheduled retry at {RetryAt}.",
                    jobId, job.RetryCount, MaxRetries,
                    job.StartedAt?.ToString("o") ?? "now");
            }
            else
            {
                job.Status = "failed";
                job.ErrorMessage = truncatedMessage;
                job.FinishedAt = DateTime.UtcNow;

                _logger.LogError(
                    "Export job {JobId} permanently failed after {MaxRetries} retries.", jobId, MaxRetries);
            }

            await db.SaveChangesAsync(ct);
        }
        catch (Exception retryEx)
        {
            _logger.LogError(retryEx, "Failed to handle export job failure for {JobId}", jobId);
        }
    }

    // ===== Inner Types =====

    private class ExportParams
    {
        public bool? IncludeAiSummary { get; set; }
        public bool? IncludeMetadata { get; set; }
        public bool? IncludeDocuments { get; set; }
        public bool? IncludeReports { get; set; }
    }

    private class SearchExportParams
    {
        public Guid? TopicId { get; set; }
        public string? Query { get; set; }
        public SearchFilters? Filters { get; set; }
    }
}
