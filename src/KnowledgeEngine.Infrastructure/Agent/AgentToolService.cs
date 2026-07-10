using System.Diagnostics;
using System.Text.Json;
using KnowledgeEngine.Application.DTOs;
using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KnowledgeEngine.Infrastructure.Agent;

public class AgentToolService : IAgentToolService
{
    private readonly IAppDbContext _db;
    private readonly ISearchService _searchService;
    private readonly IQaService _qaService;
    private readonly IAgentPermissionGuard _permissionGuard;
    private readonly IUsageService _usageService;
    private readonly ILogger<AgentToolService> _logger;

    // Phase 7: Sensitivity levels that require AllowSensitiveDocuments permission
    private static readonly HashSet<string> SensitiveDocLevels = new(StringComparer.OrdinalIgnoreCase)
    {
        "private",
        "sensitive",
        "restricted"
    };

    private static readonly List<AgentToolDefinition> AllTools = new()
    {
        new AgentToolDefinition
        {
            Name = "list_topics",
            Description = "List all active knowledge topics for the user, including document and report counts.",
            InputSchema = new Dictionary<string, object>
            {
                ["type"] = "object",
                ["properties"] = new Dictionary<string, object>(),
                ["required"] = Array.Empty<string>()
            }
        },
        new AgentToolDefinition
        {
            Name = "search_memory",
            Description = "Search the knowledge base using hybrid (keyword + vector) search. Returns matching document chunks with snippets and relevance scores.",
            InputSchema = new Dictionary<string, object>
            {
                ["type"] = "object",
                ["properties"] = new Dictionary<string, object>
                {
                    ["query"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "The search query" },
                    ["topic_id"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Optional topic UUID to filter" },
                    ["search_type"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Search type: keyword, vector, or hybrid (default)" },
                    ["limit"] = new Dictionary<string, object> { ["type"] = "integer", ["description"] = "Max results (default 10, max 20)" }
                },
                ["required"] = new[] { "query" }
            }
        },
        new AgentToolDefinition
        {
            Name = "ask_memory",
            Description = "Ask a question against the knowledge base. Uses RAG to retrieve relevant chunks and generate an answer with citations.",
            InputSchema = new Dictionary<string, object>
            {
                ["type"] = "object",
                ["properties"] = new Dictionary<string, object>
                {
                    ["question"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "The question to ask" },
                    ["topic_id"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Optional topic UUID to scope the search" }
                },
                ["required"] = new[] { "question" }
            }
        },
        new AgentToolDefinition
        {
            Name = "get_document",
            Description = "Get full details of a specific document by ID, including summary, key points, signals, and content.",
            InputSchema = new Dictionary<string, object>
            {
                ["type"] = "object",
                ["properties"] = new Dictionary<string, object>
                {
                    ["document_id"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "The document UUID" }
                },
                ["required"] = new[] { "document_id" }
            }
        },
        new AgentToolDefinition
        {
            Name = "get_report",
            Description = "Get report details by ID, or list completed reports filtered by topic and type.",
            InputSchema = new Dictionary<string, object>
            {
                ["type"] = "object",
                ["properties"] = new Dictionary<string, object>
                {
                    ["report_id"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Optional report UUID. If omitted, returns a list." },
                    ["topic_id"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Optional topic UUID filter (list mode)" },
                    ["report_type"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Optional report type filter (list mode)" }
                },
                ["required"] = Array.Empty<string>()
            }
        },
        new AgentToolDefinition
        {
            Name = "create_inbox_item",
            Description = "将 URL 或文本内容添加到知识库的收件箱（Inbox）中，系统将自动处理和导入。",
            InputSchema = new Dictionary<string, object>
            {
                ["type"] = "object",
                ["properties"] = new Dictionary<string, object>
                {
                    ["source_type"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "来源类型：url 或 text", ["enum"] = new[] { "url", "text" } },
                    ["source_url"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "URL 地址（source_type 为 url 时必填）" },
                    ["content"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "文本内容（source_type 为 text 时必填）" },
                    ["title"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "标题（可选）" },
                    ["topic_id"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "归属专题 ID（可选）" }
                },
                ["required"] = new[] { "source_type" }
            }
        },
        new AgentToolDefinition
        {
            Name = "import_url",
            Description = "触发 URL 导入流程，系统将抓取网页内容并导入到知识库中。",
            InputSchema = new Dictionary<string, object>
            {
                ["type"] = "object",
                ["properties"] = new Dictionary<string, object>
                {
                    ["url"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "要导入的网页 URL" },
                    ["topic_id"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "归属专题 ID（可选）" },
                    ["title"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "自定义标题（可选）" }
                },
                ["required"] = new[] { "url" }
            }
        }
    };

    public AgentToolService(
        IAppDbContext db,
        ISearchService searchService,
        IQaService qaService,
        IAgentPermissionGuard permissionGuard,
        IUsageService usageService,
        ILogger<AgentToolService> logger)
    {
        _db = db;
        _searchService = searchService;
        _qaService = qaService;
        _permissionGuard = permissionGuard;
        _usageService = usageService;
        _logger = logger;
    }

    public async Task<List<AgentToolDefinition>> ListToolsAsync(Guid? agentProfileId, CancellationToken ct = default)
    {
        if (!agentProfileId.HasValue)
        {
            return AllTools;
        }

        // Load the agent profile to filter by AllowedToolNames
        var profile = await _db.AgentProfiles
            .FirstOrDefaultAsync(a => a.Id == agentProfileId.Value, ct);

        if (profile == null || string.IsNullOrWhiteSpace(profile.AllowedToolNames))
        {
            return AllTools;
        }

        List<string>? allowedTools = null;
        try
        {
            allowedTools = JsonSerializer.Deserialize<List<string>>(profile.AllowedToolNames);
        }
        catch
        {
            return AllTools;
        }

        if (allowedTools == null || allowedTools.Count == 0)
        {
            return AllTools;
        }

        return AllTools.Where(t => allowedTools.Contains(t.Name)).ToList();
    }

    public async Task<AgentToolResult> InvokeToolAsync(
        Guid userId,
        string toolName,
        Dictionary<string, object> input,
        Guid? agentProfileId = null,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();

        var logEntry = new AgentInvocationLog
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            AgentProfileId = agentProfileId,
            Transport = "mcp_stdio",
            ToolName = toolName,
            InputJson = SafeSerialize(input),
            Status = "success",
            CreatedAt = DateTime.UtcNow
        };

        AgentToolResult result;
        try
        {
            // Check permission
            if (!await _permissionGuard.CanUseToolAsync(userId, agentProfileId, toolName, ct))
            {
                logEntry.Status = "denied";
                logEntry.ErrorCode = "permission_denied";
                logEntry.ErrorMessage = $"Tool '{toolName}' is not allowed for this agent profile.";
                result = new AgentToolResult
                {
                    Success = false,
                    Error = logEntry.ErrorMessage
                };
            }
            else
            {
                result = toolName switch
                {
                    "list_topics" => await InvokeListTopicsAsync(userId, agentProfileId, ct),
                    "search_memory" => await InvokeSearchMemoryAsync(userId, input, agentProfileId, ct),
                    "ask_memory" => await InvokeAskMemoryAsync(userId, input, agentProfileId, ct),
                    "get_document" => await InvokeGetDocumentAsync(userId, input, agentProfileId, ct),
                    "get_report" => await InvokeGetReportAsync(userId, input, agentProfileId, ct),
                    "create_inbox_item" => await InvokeCreateInboxItemAsync(userId, input, ct),
                    "import_url" => await InvokeImportUrlAsync(userId, input, ct),
                    _ => new AgentToolResult { Success = false, Error = $"Unknown tool: {toolName}" }
                };

                if (result.Success)
                {
                    logEntry.Status = "success";
                    logEntry.ResultCount = result.ResultCount;
                    logEntry.PromptTokens = result.PromptTokens;
                    logEntry.CompletionTokens = result.CompletionTokens;
                    logEntry.OutputSummary = SafeSerialize(result.Data);
                    if (logEntry.OutputSummary?.Length > 500)
                    {
                        logEntry.OutputSummary = logEntry.OutputSummary[..500] + "...";
                    }
                }
                else
                {
                    logEntry.Status = "failed";
                    logEntry.ErrorCode = "tool_error";
                    logEntry.ErrorMessage = result.Error;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error invoking tool {ToolName}: {Message}", toolName, ex.Message);
            logEntry.Status = "failed";
            logEntry.ErrorCode = "tool_error";
            logEntry.ErrorMessage = ex.Message;
            result = new AgentToolResult
            {
                Success = false,
                Error = ex.Message
            };
        }
        finally
        {
            sw.Stop();
            logEntry.LatencyMs = (int)sw.ElapsedMilliseconds;
            _logger.LogDebug("Tool {ToolName} invoked in {ElapsedMs}ms", toolName, sw.ElapsedMilliseconds);

            // Persist the invocation log. Use CancellationToken.None to ensure
            // the log is saved even when the original ct has been cancelled.
            try
            {
                _db.AgentInvocationLogs.Add(logEntry);
                await _db.SaveChangesAsync(CancellationToken.None);
            }
            catch (Exception logEx)
            {
                _logger.LogError(logEx, "Failed to save agent invocation log");
            }
        }

        // Record agent usage statistics (best-effort, never breaks tool invocation)
        try
        {
            await _usageService.RecordAgentUsageAsync(userId, toolName, result.Success, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to record agent usage for tool {ToolName}", toolName);
        }

        return result;
    }

    /// <summary>
    /// Safely serializes an object to JSON, returning null on failure.
    /// Used for InputJson / OutputSummary fields in AgentInvocationLog.
    /// </summary>
    private static string? SafeSerialize(object? obj)
    {
        if (obj == null)
            return null;
        try
        {
            return JsonSerializer.Serialize(obj);
        }
        catch
        {
            return obj.ToString();
        }
    }

    // ===== list_topics =====

    private async Task<AgentToolResult> InvokeListTopicsAsync(Guid userId, Guid? agentProfileId, CancellationToken ct)
    {
        var topics = await _db.Topics
            .Where(t => t.UserId == userId && t.Status == "active")
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync(ct);

        // Filter by allowed topic IDs
        var allowedTopicIds = await _permissionGuard.GetAccessibleTopicIdsAsync(userId, agentProfileId, ct);
        if (allowedTopicIds.Count > 0)
        {
            topics = topics.Where(t => allowedTopicIds.Contains(t.Id)).ToList();
        }

        var topicIds = topics.Select(t => t.Id).ToList();

        var docCounts = await _db.Documents
            .Where(d => d.UserId == userId && d.TopicId.HasValue && topicIds.Contains(d.TopicId.Value))
            .GroupBy(d => d.TopicId!.Value)
            .Select(g => new { TopicId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.TopicId, x => x.Count, ct);

        var reportCounts = await _db.Reports
            .Where(r => r.UserId == userId && r.TopicId.HasValue && topicIds.Contains(r.TopicId.Value))
            .GroupBy(r => r.TopicId!.Value)
            .Select(g => new { TopicId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.TopicId, x => x.Count, ct);

        var items = topics.Select(t => new
        {
            id = t.Id,
            name = t.Name,
            description = t.Description,
            domain = t.Domain,
            status = t.Status,
            document_count = docCounts.TryGetValue(t.Id, out var dc) ? dc : 0,
            report_count = reportCounts.TryGetValue(t.Id, out var rc) ? rc : 0,
            created_at = t.CreatedAt
        }).ToList();

        return new AgentToolResult
        {
            Success = true,
            Data = new { items },
            ResultCount = items.Count
        };
    }

    // ===== search_memory =====

    private async Task<AgentToolResult> InvokeSearchMemoryAsync(
        Guid userId,
        Dictionary<string, object> input,
        Guid? agentProfileId,
        CancellationToken ct)
    {
        var query = GetStringValue(input, "query");
        if (string.IsNullOrWhiteSpace(query))
        {
            return new AgentToolResult { Success = false, Error = "Missing required parameter: query" };
        }

        var topicId = GetGuidValue(input, "topic_id");
        var searchType = GetStringValue(input, "search_type") ?? "hybrid";
        var limit = GetIntValue(input, "limit") ?? 10;

        // Apply max results from permission guard
        var maxResults = _permissionGuard.GetMaxResults(agentProfileId);
        if (limit <= 0) limit = 10;
        if (limit > maxResults) limit = maxResults;

        // Check topic permission
        if (topicId.HasValue)
        {
            var allowedTopicIds = await _permissionGuard.GetAccessibleTopicIdsAsync(userId, agentProfileId, ct);
            if (allowedTopicIds.Count > 0 && !allowedTopicIds.Contains(topicId.Value))
            {
                return new AgentToolResult { Success = false, Error = "Access denied to the requested topic." };
            }
        }

        var searchRequest = new SearchRequest
        {
            TopicId = topicId,
            Query = query,
            SearchType = searchType,
            Limit = limit
        };

        var result = await _searchService.SearchAsync(userId, searchRequest, ct);

        if (!result.Success)
        {
            return new AgentToolResult
            {
                Success = false,
                Error = result.Error?.Message ?? "Search failed"
            };
        }

        var searchResult = result.Data!;

        // Phase 7: Filter sensitive documents from search results
        if (searchResult.Items.Count > 0)
        {
            var documentIds = searchResult.Items.Select(s => s.DocumentId).Distinct().ToList();
            var docs = await _db.Documents
                .Where(d => documentIds.Contains(d.Id))
                .ToListAsync(ct);

            List<Document> allowedDocs;
            if (agentProfileId.HasValue)
            {
                // Use the permission guard's filter based on profile settings
                allowedDocs = await _permissionGuard.FilterSensitiveDocumentsAsync(docs, agentProfileId.Value, ct);
            }
            else
            {
                // No profile: only allow public/normal sensitivity levels
                allowedDocs = docs
                    .Where(d => !SensitiveDocLevels.Contains(d.SensitivityLevel ?? "normal"))
                    .ToList();
            }

            var allowedDocIds = allowedDocs.Select(d => d.Id).ToHashSet();
            searchResult.Items = searchResult.Items
                .Where(s => allowedDocIds.Contains(s.DocumentId))
                .ToList();
            searchResult.Total = searchResult.Items.Count;
        }

        var items = searchResult.Items.Select(s => new
        {
            document_id = s.DocumentId,
            chunk_id = s.ChunkId,
            title = s.Title,
            snippet = s.Snippet,
            source_type = s.SourceType,
            source_url = s.SourceUrl,
            source_domain = s.SourceDomain,
            published_at = s.PublishedAt,
            value_score = s.ValueScore,
            score = s.Score
        }).ToList();

        return new AgentToolResult
        {
            Success = true,
            Data = new
            {
                items,
                metadata = new
                {
                    total = searchResult.Total,
                    search_type = searchResult.SearchType
                }
            },
            ResultCount = items.Count
        };
    }

    // ===== ask_memory =====

    private async Task<AgentToolResult> InvokeAskMemoryAsync(
        Guid userId,
        Dictionary<string, object> input,
        Guid? agentProfileId,
        CancellationToken ct)
    {
        var question = GetStringValue(input, "question");
        if (string.IsNullOrWhiteSpace(question))
        {
            return new AgentToolResult { Success = false, Error = "Missing required parameter: question" };
        }

        var topicId = GetGuidValue(input, "topic_id");

        // Check topic permission
        if (topicId.HasValue)
        {
            var allowedTopicIds = await _permissionGuard.GetAccessibleTopicIdsAsync(userId, agentProfileId, ct);
            if (allowedTopicIds.Count > 0 && !allowedTopicIds.Contains(topicId.Value))
            {
                return new AgentToolResult { Success = false, Error = "Access denied to the requested topic." };
            }
        }

        // Create a temporary session (stateless invocation)
        var createSessionRequest = new CreateQaSessionRequest
        {
            TopicId = topicId,
            Title = $"Agent: {Truncate(question, 50)}"
        };

        var sessionResult = await _qaService.CreateSessionAsync(userId, createSessionRequest, ct);
        if (!sessionResult.Success)
        {
            return new AgentToolResult
            {
                Success = false,
                Error = sessionResult.Error?.Message ?? "Failed to create QA session"
            };
        }

        var askRequest = new QaAskRequest
        {
            SessionId = sessionResult.Data!.Id,
            Query = question,
            TopicId = topicId
        };

        var qaResult = await _qaService.AskAsync(userId, askRequest, ct);

        if (!qaResult.Success)
        {
            return new AgentToolResult
            {
                Success = false,
                Error = qaResult.Error?.Message ?? "QA failed"
            };
        }

        var answer = qaResult.Data!;

        var citations = answer.Citations.Select(c => new
        {
            index = c.Index,
            document_id = c.DocumentId,
            chunk_id = c.ChunkId,
            title = c.Title,
            source_url = c.SourceUrl,
            source_domain = c.SourceDomain,
            source_type = c.SourceType,
            snippet = c.Snippet,
            score = c.Score
        }).ToList();

        return new AgentToolResult
        {
            Success = true,
            Data = new
            {
                answer = answer.Answer,
                citations,
                metadata = new
                {
                    topic_id = topicId,
                    retrieved_count = answer.Retrieval?.RetrievedCount ?? 0,
                    used_count = answer.Retrieval?.UsedCount ?? 0,
                    model = answer.Model
                }
            },
            PromptTokens = answer.InputTokens,
            CompletionTokens = answer.OutputTokens
        };
    }

    // ===== get_document =====

    private async Task<AgentToolResult> InvokeGetDocumentAsync(
        Guid userId,
        Dictionary<string, object> input,
        Guid? agentProfileId,
        CancellationToken ct)
    {
        var documentId = GetGuidValue(input, "document_id");
        if (!documentId.HasValue)
        {
            return new AgentToolResult { Success = false, Error = "Missing required parameter: document_id" };
        }

        // Check document access permission (Phase 7: includes SensitivityLevel check)
        if (!await _permissionGuard.CanAccessDocumentAsync(userId, agentProfileId, documentId.Value, ct))
        {
            return new AgentToolResult { Success = false, Error = "Access denied to the requested document." };
        }

        var document = await _db.Documents
            .FirstOrDefaultAsync(d => d.Id == documentId.Value && d.UserId == userId, ct);

        if (document == null)
        {
            return new AgentToolResult { Success = false, Error = "Document not found." };
        }

        // Phase 7: Double-check sensitivity level (defensive: CanAccessDocumentAsync already enforces this)
        if (SensitiveDocLevels.Contains(document.SensitivityLevel ?? "normal") &&
            (!agentProfileId.HasValue || !await HasProfileSensitiveAccessAsync(agentProfileId.Value, ct)))
        {
            return new AgentToolResult { Success = false, Error = "Access denied: this document has a restricted sensitivity level." };
        }

        return new AgentToolResult
        {
            Success = true,
            Data = new
            {
                id = document.Id,
                topic_id = document.TopicId,
                title = document.Title,
                summary = document.Summary,
                one_sentence_conclusion = document.OneSentenceConclusion,
                key_points = document.KeyPoints,
                business_signals = document.BusinessSignals,
                technical_signals = document.TechnicalSignals,
                risks = document.Risks,
                opportunities = document.Opportunities,
                value_score = document.ValueScore,
                ai_status = document.AiStatus,
                sensitivity_level = document.SensitivityLevel,
                content_text = document.ContentText,
                created_at = document.CreatedAt,
                updated_at = document.UpdatedAt
            },
            ResultCount = 1
        };
    }

    // ===== get_report =====

    private async Task<AgentToolResult> InvokeGetReportAsync(
        Guid userId,
        Dictionary<string, object> input,
        Guid? agentProfileId,
        CancellationToken ct)
    {
        var reportId = GetGuidValue(input, "report_id");

        if (reportId.HasValue)
        {
            // Get single report detail
            var report = await _db.Reports
                .FirstOrDefaultAsync(r => r.Id == reportId.Value && r.UserId == userId, ct);

            if (report == null)
            {
                return new AgentToolResult { Success = false, Error = "Report not found." };
            }

            // Check topic permission
            if (report.TopicId.HasValue)
            {
                var allowedTopicIds = await _permissionGuard.GetAccessibleTopicIdsAsync(userId, agentProfileId, ct);
                if (allowedTopicIds.Count > 0 && !allowedTopicIds.Contains(report.TopicId.Value))
                {
                    return new AgentToolResult { Success = false, Error = "Access denied to this report's topic." };
                }
            }

            return new AgentToolResult
            {
                Success = true,
                Data = new
                {
                    id = report.Id,
                    topic_id = report.TopicId,
                    report_type = report.ReportType,
                    title = report.Title,
                    content_markdown = report.ContentMarkdown,
                    query = report.Query,
                    start_date = report.StartDate,
                    end_date = report.EndDate,
                    generated_by_model = report.GeneratedByModel,
                    status = report.Status,
                    quality_score = report.QualityScore,
                    created_at = report.CreatedAt,
                    updated_at = report.UpdatedAt
                },
                ResultCount = 1
            };
        }

        // List reports
        var topicId = GetGuidValue(input, "topic_id");
        var reportType = GetStringValue(input, "report_type");

        // Check topic permission
        if (topicId.HasValue)
        {
            var allowedTopicIds = await _permissionGuard.GetAccessibleTopicIdsAsync(userId, agentProfileId, ct);
            if (allowedTopicIds.Count > 0 && !allowedTopicIds.Contains(topicId.Value))
            {
                return new AgentToolResult { Success = false, Error = "Access denied to the requested topic." };
            }
        }

        var query = _db.Reports
            .Where(r => r.UserId == userId && (r.Status == "done" || r.Status == "completed"));

        if (topicId.HasValue)
        {
            query = query.Where(r => r.TopicId == topicId.Value);
        }

        if (!string.IsNullOrWhiteSpace(reportType))
        {
            query = query.Where(r => r.ReportType == reportType);
        }

        // Filter by allowed topic IDs
        var allowedIds = await _permissionGuard.GetAccessibleTopicIdsAsync(userId, agentProfileId, ct);
        if (allowedIds.Count > 0)
        {
            query = query.Where(r => r.TopicId.HasValue && allowedIds.Contains(r.TopicId.Value));
        }

        var reports = await query
            .OrderByDescending(r => r.CreatedAt)
            .Take(50)
            .ToListAsync(ct);

        var items = reports.Select(r => new
        {
            id = r.Id,
            topic_id = r.TopicId,
            report_type = r.ReportType,
            title = r.Title,
            status = r.Status,
            quality_score = r.QualityScore,
            generated_by_model = r.GeneratedByModel,
            start_date = r.StartDate,
            end_date = r.EndDate,
            created_at = r.CreatedAt
        }).ToList();

        return new AgentToolResult
        {
            Success = true,
            Data = new { items },
            ResultCount = items.Count
        };
    }

    // ===== create_inbox_item =====

    private async Task<AgentToolResult> InvokeCreateInboxItemAsync(
        Guid userId,
        Dictionary<string, object> input,
        CancellationToken ct)
    {
        var sourceType = GetStringValue(input, "source_type") ?? "text";
        var sourceUrl = GetStringValue(input, "source_url");
        var content = GetStringValue(input, "content");
        var title = GetStringValue(input, "title");
        var topicId = GetGuidValue(input, "topic_id");

        // Validate: url type requires source_url; text type requires content
        if (sourceType == "url" && string.IsNullOrWhiteSpace(sourceUrl))
        {
            return new AgentToolResult { Success = false, Error = "source_url is required when source_type is 'url'." };
        }
        if (sourceType == "text" && string.IsNullOrWhiteSpace(content))
        {
            return new AgentToolResult { Success = false, Error = "content is required when source_type is 'text'." };
        }

        // Resolve the user's workspace (InboxItem.WorkspaceId is required)
        var workspaceId = await ResolveWorkspaceIdAsync(userId, ct);
        if (workspaceId == null)
        {
            return new AgentToolResult { Success = false, Error = "No workspace found for the user." };
        }

        var now = DateTime.UtcNow;
        var inboxItem = new InboxItem
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId.Value,
            UserId = userId,
            TopicId = topicId,
            InputType = sourceType,
            ItemType = sourceType,
            Title = title ?? (sourceType == "url" ? sourceUrl : "Agent 导入文本"),
            ContentText = content,
            SourceUrl = sourceType == "url" ? sourceUrl : null,
            Status = "pending",
            CreatedFrom = "api",
            CreatedAt = now,
            UpdatedAt = now
        };

        _db.InboxItems.Add(inboxItem);
        await _db.SaveChangesAsync(ct);

        return new AgentToolResult
        {
            Success = true,
            Data = new { inbox_item_id = inboxItem.Id, status = "pending", message = "已添加到收件箱，系统将自动处理" },
            ResultCount = 1
        };
    }

    // ===== import_url =====

    private async Task<AgentToolResult> InvokeImportUrlAsync(
        Guid userId,
        Dictionary<string, object> input,
        CancellationToken ct)
    {
        var url = GetStringValue(input, "url");
        if (string.IsNullOrWhiteSpace(url))
        {
            return new AgentToolResult { Success = false, Error = "url is required" };
        }

        var title = GetStringValue(input, "title");
        var topicId = GetGuidValue(input, "topic_id");

        // Resolve the user's workspace (InboxItem.WorkspaceId is required)
        var workspaceId = await ResolveWorkspaceIdAsync(userId, ct);
        if (workspaceId == null)
        {
            return new AgentToolResult { Success = false, Error = "No workspace found for the user." };
        }

        var now = DateTime.UtcNow;
        var inboxItem = new InboxItem
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId.Value,
            UserId = userId,
            TopicId = topicId,
            InputType = "url",
            ItemType = "url",
            SourceUrl = url,
            Title = title ?? url,
            Status = "pending",
            CreatedFrom = "api",
            CreatedAt = now,
            UpdatedAt = now
        };

        _db.InboxItems.Add(inboxItem);
        await _db.SaveChangesAsync(ct);

        return new AgentToolResult
        {
            Success = true,
            Data = new { inbox_item_id = inboxItem.Id, status = "pending", message = $"URL {url} 已加入导入队列" },
            ResultCount = 1
        };
    }

    /// <summary>
    /// Resolves the workspace ID for a given user.
    /// The Workspace entity has a UserId field linking it to its owner.
    /// </summary>
    private async Task<Guid?> ResolveWorkspaceIdAsync(Guid userId, CancellationToken ct)
    {
        var workspace = await _db.Workspaces
            .FirstOrDefaultAsync(w => w.UserId == userId, ct);
        return workspace?.Id;
    }

    /// <summary>
    /// Phase 7: Checks if the agent profile allows access to sensitive documents.
    /// </summary>
    private async Task<bool> HasProfileSensitiveAccessAsync(Guid profileId, CancellationToken ct)
    {
        var profile = await _db.AgentProfiles
            .FirstOrDefaultAsync(a => a.Id == profileId, ct);
        return profile?.AllowSensitiveDocuments ?? false;
    }

    // ===== Helper methods for extracting values from input dictionary =====

    private static string? GetStringValue(Dictionary<string, object> input, string key)
    {
        if (!input.TryGetValue(key, out var value))
            return null;

        return value switch
        {
            string s => s,
            JsonElement je => je.ValueKind == JsonValueKind.String ? je.GetString() : je.ToString(),
            _ => value?.ToString()
        };
    }

    private static Guid? GetGuidValue(Dictionary<string, object> input, string key)
    {
        var str = GetStringValue(input, key);
        if (string.IsNullOrWhiteSpace(str))
            return null;

        return Guid.TryParse(str, out var guid) ? guid : null;
    }

    private static int? GetIntValue(Dictionary<string, object> input, string key)
    {
        if (!input.TryGetValue(key, out var value))
            return null;

        return value switch
        {
            int i => i,
            long l => (int)l,
            JsonElement je => je.ValueKind == JsonValueKind.Number ? je.GetInt32() : null,
            _ when int.TryParse(value?.ToString(), out var i) => i,
            _ => null
        };
    }

    private static string Truncate(string s, int maxLen)
    {
        if (string.IsNullOrEmpty(s))
            return string.Empty;
        return s.Length <= maxLen ? s : s.Substring(0, maxLen);
    }
}
