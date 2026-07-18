using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using KnowledgeEngine.Application.DTOs;
using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Application.Mapping;
using KnowledgeEngine.Application.Settings;
using KnowledgeEngine.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KnowledgeEngine.Infrastructure.Search;

public class QaService : IQaService
{
    private const int RetrievalTopK = 20;
    private const int MaxContextChunks = 8;
    private const int MaxChunksPerDocument = 3;

    private readonly IAppDbContext _db;
    private readonly ISearchService _searchService;
    private readonly ILlmService _llmService;
    private readonly LlmSettings _llmSettings;
    private readonly IRerankerService _reranker;
    private readonly ILogger<QaService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly JsonSerializerOptions LegacyJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public QaService(
        IAppDbContext db,
        ISearchService searchService,
        ILlmService llmService,
        IOptions<LlmSettings> llmSettings,
        IRerankerService reranker,
        ILogger<QaService> logger)
    {
        _db = db;
        _searchService = searchService;
        _llmService = llmService;
        _llmSettings = llmSettings.Value;
        _reranker = reranker;
        _logger = logger;
    }

    // ===== Create Session =====

    public async Task<ApiResponse<QaSessionResponse>> CreateSessionAsync(
        Guid userId,
        CreateQaSessionRequest request,
        CancellationToken ct = default)
    {
        try
        {
            var now = DateTime.UtcNow;
            var session = new QaSession
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                TopicId = request.TopicId,
                Title = request.Title ?? "New Session",
                Status = "active",
                CreatedAt = now,
                UpdatedAt = now
            };

            _db.QaSessions.Add(session);
            await _db.SaveChangesAsync(ct);

            return ApiResponse<QaSessionResponse>.Ok(Mapper.ToQaSessionResponse(session));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create QA session");
            return ApiResponse<QaSessionResponse>.Fail("create_session_error", ex.Message);
        }
    }

    // ===== Ask (RAG Flow) =====

    public async Task<ApiResponse<QaAnswerResponse>> AskAsync(
        Guid userId,
        QaAskRequest request,
        CancellationToken ct = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            // Validate session
            var session = await _db.QaSessions
                .FirstOrDefaultAsync(s => s.Id == request.SessionId && s.UserId == userId, ct);

            if (session == null)
            {
                return ApiResponse<QaAnswerResponse>.Fail("session_not_found", "Session not found");
            }

            var topicId = request.TopicId ?? session.TopicId;
            var now = DateTime.UtcNow;

            // Step 1: Save user message
            var userMessage = new QaMessage
            {
                Id = Guid.NewGuid(),
                SessionId = session.Id,
                UserId = userId,
                TopicId = topicId,
                Role = "user",
                Content = request.Query,
                CreatedAt = now
            };
            _db.QaMessages.Add(userMessage);
            await _db.SaveChangesAsync(ct);

            // Step 1.5: Complete follow-up questions from recent history, then normalize.
            var recentHistory = await _db.QaMessages
                .Where(m => m.SessionId == session.Id && m.Id != userMessage.Id)
                .OrderByDescending(m => m.CreatedAt)
                .Take(6)
                .OrderBy(m => m.CreatedAt)
                .ToListAsync(ct);
            var completedQuery = CompleteQueryFromHistory(request.Query, recentHistory);
            var rewrittenQuery = RewriteQuery(completedQuery);
            if (rewrittenQuery != request.Query)
            {
                _logger.LogInformation("Query rewritten: {Original} → {Rewritten}", request.Query, rewrittenQuery);
            }

            var embeddingDiagnostics = await GetEmbeddingDiagnosticsAsync(userId, topicId, ct);

            // Step 2: Retrieve relevant chunks via hybrid search
            var searchRequest = new SearchRequest
            {
                TopicId = topicId,
                Query = rewrittenQuery,  // Use rewritten query for search
                SearchType = "hybrid",
                Limit = RetrievalTopK
            };

            var searchResponse = await _searchService.SearchAsync(userId, searchRequest, ct);
            var searchResults = searchResponse.Data?.Items ?? new List<SearchResultItem>();

            // Step 3: Anti-hallucination: if no results, don't call the model
            if (searchResults.Count == 0)
            {
                sw.Stop();
                var noResultAnswer = BuildNoResultAnswer(embeddingDiagnostics);

                var noResultMessage = new QaMessage
                {
                    Id = Guid.NewGuid(),
                    SessionId = session.Id,
                    UserId = userId,
                    TopicId = topicId,
                    Role = "assistant",
                    Content = noResultAnswer,
                    Citations = JsonSerializer.Serialize(new List<Citation>()),
                    RetrievalSnapshot = JsonSerializer.Serialize(new
                    {
                        search_type = "hybrid",
                        retrieved_count = 0,
                        used_count = 0,
                        top_score = 0.0,
                        completed_query = completedQuery,
                        embedding_diagnostics = embeddingDiagnostics
                    }),
                    Model = _llmSettings.Model,
                    LatencyMs = (int)sw.ElapsedMilliseconds,
                    CreatedAt = DateTime.UtcNow
                };
                _db.QaMessages.Add(noResultMessage);

                session.UpdatedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync(ct);

                return ApiResponse<QaAnswerResponse>.Ok(new QaAnswerResponse
                {
                    Answer = noResultAnswer,
                    Citations = new List<Citation>(),
                    Retrieval = new RetrievalInfo
                    {
                        SearchType = "hybrid",
                        RetrievedCount = 0,
                        UsedCount = 0,
                        TopScore = 0
                    },
                    Model = _llmSettings.Model,
                    LatencyMs = (int)sw.ElapsedMilliseconds,
                    Confidence = 0.0,
                    DebugInfo = BuildDebugInfo(topicId, request.Query, completedQuery, searchResults, null, embeddingDiagnostics, new List<string>())
                });
            }

            // Gate with raw evidence quality, not the normalized RRF rank score.
            var topScore = searchResults.Max(CalculateEvidenceRelevance);
            if (topScore < 0.20)
            {
                sw.Stop();
                var lowRelevanceAnswer = "我找到了一些候选资料，但原始向量相似度、分词命中率和多通道证据均不足，暂不据此生成结论。以下内容仅供核查：\n\n" +
                    string.Join("\n\n", searchResults.Take(3).Select((r, index) => $"- [{index + 1}] {r.Title}: {r.Snippet}"));

                var lowRelevanceCitations = BuildCitations(searchResults.Take(3).ToList());
                var lowRelevanceMessage = new QaMessage
                {
                    Id = Guid.NewGuid(),
                    SessionId = session.Id,
                    UserId = userId,
                    TopicId = topicId,
                    Role = "assistant",
                    Content = lowRelevanceAnswer,
                    Citations = JsonSerializer.Serialize(lowRelevanceCitations),
                    RetrievalSnapshot = JsonSerializer.Serialize(new
                    {
                        search_type = "hybrid",
                        retrieved_count = searchResults.Count,
                        used_count = 3,
                        top_score = topScore,
                        rrf_score = searchResults.First().Score,
                        completed_query = completedQuery,
                        embedding_diagnostics = embeddingDiagnostics
                    }),
                    Model = _llmSettings.Model,
                    LatencyMs = (int)sw.ElapsedMilliseconds,
                    CreatedAt = DateTime.UtcNow
                };
                _db.QaMessages.Add(lowRelevanceMessage);

                session.UpdatedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync(ct);

                return ApiResponse<QaAnswerResponse>.Ok(new QaAnswerResponse
                {
                    Answer = lowRelevanceAnswer,
                    Citations = lowRelevanceCitations,
                    Retrieval = new RetrievalInfo
                    {
                        SearchType = "hybrid",
                        RetrievedCount = searchResults.Count,
                        UsedCount = 3,
                        TopScore = topScore
                    },
                    Model = _llmSettings.Model,
                    LatencyMs = (int)sw.ElapsedMilliseconds,
                    Confidence = CalculateConfidence(topScore, lowRelevanceCitations.Count, 3),
                    DebugInfo = BuildDebugInfo(topicId, request.Query, completedQuery, searchResults, null, embeddingDiagnostics, new List<string>())
                });
            }

            // Step 4: Rerank - take top chunks, max 3 per document
            var rerankedChunks = await _reranker.RerankAsync(completedQuery, searchResults, MaxContextChunks, MaxChunksPerDocument, ct);

            // Step 5: Build RAG context
            var (ragContext, citations) = BuildRagContext(rerankedChunks);

            // Step 6: Build prompts
            var systemPrompt = BuildSystemPrompt();

            var historyText = BuildHistoryText(recentHistory);

            var userPrompt = BuildUserPrompt(request.Query, ragContext, historyText);

            // Step 7: Call LLM
            string answerContent;
            string modelUsed;
            int? inputTokens = null;
            int? outputTokens = null;

            try
            {
                var llmResult = await _llmService.CompleteAsync(systemPrompt, userPrompt, _llmSettings.Model, ct);
                answerContent = llmResult.Content;
                modelUsed = llmResult.Model;
                inputTokens = llmResult.InputTokens;
                outputTokens = llmResult.OutputTokens;
            }
            catch (Exception llmEx)
            {
                _logger.LogWarning(llmEx, "LLM call failed for query: {Query}, using degradation strategy", request.Query);

                // Degradation: generate answer from retrieved chunks directly
                answerContent = BuildDegradedAnswer(request.Query, rerankedChunks);
                modelUsed = "degraded";
            }

            var (validatedAnswer, citationValidationIssues) = ValidateAndRepairCitations(answerContent, citations);
            answerContent = validatedAnswer;

            sw.Stop();

            // Step 8: Build citations from used chunks (system-generated, not model-generated)
            var citationsJson = JsonSerializer.Serialize(citations, JsonOptions);

            var confidence = modelUsed == "degraded"
                ? Math.Round(CalculateConfidence(topScore, citations.Count, rerankedChunks.Count) * 0.5, 2) // halve confidence for degraded
                : CalculateConfidence(topScore, citations.Count, rerankedChunks.Count);
            var debugInfo = BuildDebugInfo(
                topicId, request.Query, completedQuery, searchResults, ragContext,
                embeddingDiagnostics, citationValidationIssues, systemPrompt);

            var retrievalSnapshotJson = JsonSerializer.Serialize(new
            {
                search_type = "hybrid",
                retrieved_count = searchResults.Count,
                used_count = rerankedChunks.Count,
                top_score = topScore,
                confidence = confidence,
                debug_info = debugInfo,
                chunks = rerankedChunks.Select(c => new
                {
                    document_id = c.DocumentId,
                    chunk_id = c.ChunkId,
                    title = c.Title,
                    score = c.Score
                })
            }, JsonOptions);

            // Step 9: Save assistant message
            var assistantMessage = new QaMessage
            {
                Id = Guid.NewGuid(),
                SessionId = session.Id,
                UserId = userId,
                TopicId = topicId,
                Role = "assistant",
                Content = answerContent,
                Citations = citationsJson,
                RetrievalSnapshot = retrievalSnapshotJson,
                Model = modelUsed,
                InputTokens = inputTokens,
                OutputTokens = outputTokens,
                LatencyMs = (int)sw.ElapsedMilliseconds,
                CreatedAt = DateTime.UtcNow
            };
            _db.QaMessages.Add(assistantMessage);

            // Step 10: Save retrieval log
            var retrievalLog = new RetrievalLog
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                TopicId = topicId,
                QaMessageId = assistantMessage.Id,
                Query = request.Query,
                RetrievalType = "hybrid",
                RetrievedChunks = JsonSerializer.Serialize(searchResults.Take(RetrievalTopK).Select(r => new
                {
                    document_id = r.DocumentId,
                    chunk_id = r.ChunkId,
                    title = r.Title,
                    score = r.Score
                }), JsonOptions),
                FinalContext = ragContext,
                LatencyMs = (int)sw.ElapsedMilliseconds,
                CreatedAt = DateTime.UtcNow
            };
            _db.RetrievalLogs.Add(retrievalLog);

            session.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);

            // Step 11: Return answer
            return ApiResponse<QaAnswerResponse>.Ok(new QaAnswerResponse
            {
                Answer = answerContent,
                Citations = citations,
                Retrieval = new RetrievalInfo
                {
                    SearchType = "hybrid",
                    RetrievedCount = searchResults.Count,
                    UsedCount = rerankedChunks.Count,
                    TopScore = topScore
                },
                Model = modelUsed,
                InputTokens = inputTokens,
                OutputTokens = outputTokens,
                LatencyMs = (int)sw.ElapsedMilliseconds,
                Confidence = confidence,
                DebugInfo = debugInfo
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "QA Ask failed for query: {Query}", request.Query);
            return ApiResponse<QaAnswerResponse>.Fail("qa_error", $"QA failed: {ex.Message}");
        }
    }

    // ===== Get Session Messages =====

    public async Task<ApiResponse<List<QaMessageResponse>>> GetSessionMessagesAsync(
        Guid userId,
        Guid sessionId,
        CancellationToken ct = default)
    {
        try
        {
            // Validate session ownership
            var session = await _db.QaSessions
                .FirstOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId, ct);

            if (session == null)
            {
                return ApiResponse<List<QaMessageResponse>>.Fail("session_not_found", "Session not found");
            }

            var messages = await _db.QaMessages
                .Where(m => m.SessionId == sessionId && m.UserId == userId)
                .OrderBy(m => m.CreatedAt)
                .ToListAsync(ct);

            var result = messages.Select(m =>
            {
                List<Citation>? citations = null;
                if (!string.IsNullOrEmpty(m.Citations))
                {
                    try
                    {
                        citations = JsonSerializer.Deserialize<List<Citation>>(m.Citations, JsonOptions);
                        if (citations is { Count: > 0 } && citations.All(citation => citation.DocumentId == Guid.Empty))
                        {
                            citations = JsonSerializer.Deserialize<List<Citation>>(m.Citations, LegacyJsonOptions);
                        }
                    }
                    catch
                    {
                        citations = new List<Citation>();
                    }
                }

                RetrievalInfo? retrieval = null;
                if (!string.IsNullOrEmpty(m.RetrievalSnapshot))
                {
                    try
                    {
                        var snapshot = JsonSerializer.Deserialize<RetrievalSnapshotDto>(m.RetrievalSnapshot, JsonOptions);
                        if (snapshot != null)
                        {
                            retrieval = new RetrievalInfo
                            {
                                SearchType = snapshot.SearchType ?? "hybrid",
                                RetrievedCount = snapshot.RetrievedCount,
                                UsedCount = snapshot.UsedCount,
                                TopScore = snapshot.TopScore
                            };
                        }
                    }
                    catch
                    {
                        // Ignore parse errors
                    }
                }

                return Mapper.ToQaMessageResponse(m, citations, retrieval);
            }).ToList();

            var citationsToEnrich = result
                .SelectMany(message => message.Citations ?? new List<Citation>())
                .Where(citation => citation.DocumentId != Guid.Empty)
                .ToList();
            if (citationsToEnrich.Count > 0)
            {
                var documentIds = citationsToEnrich
                    .Select(citation => citation.DocumentId)
                    .Distinct()
                    .ToList();
                var documentTitles = await _db.Documents
                    .Where(document => documentIds.Contains(document.Id))
                    .Select(document => new { document.Id, document.Title })
                    .ToDictionaryAsync(document => document.Id, document => document.Title, ct);

                foreach (var citation in citationsToEnrich)
                {
                    if (documentTitles.TryGetValue(citation.DocumentId, out var title) && !string.IsNullOrWhiteSpace(title))
                    {
                        citation.Title = title;
                    }
                }
            }

            return ApiResponse<List<QaMessageResponse>>.Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get session messages");
            return ApiResponse<List<QaMessageResponse>>.Fail("get_messages_error", ex.Message);
        }
    }

    // ===== Get Sessions =====

    public async Task<ApiResponse<PagedResult<QaSessionListItem>>> GetSessionsAsync(
        Guid userId,
        Guid? topicId,
        CancellationToken ct = default)
    {
        try
        {
            var query = _db.QaSessions
                .Where(s => s.UserId == userId && s.Status == "active");

            if (topicId.HasValue)
            {
                query = query.Where(s => s.TopicId == topicId);
            }

            var total = await query.CountAsync(ct);
            var sessions = await query
                .OrderByDescending(s => s.UpdatedAt)
                .Take(100)
                .ToListAsync(ct);

            var items = sessions.Select(Mapper.ToQaSessionListItem).ToList();

            var result = new PagedResult<QaSessionListItem>
            {
                Items = items,
                Total = total,
                Page = 1,
                PageSize = 100
            };

            return ApiResponse<PagedResult<QaSessionListItem>>.Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get QA sessions");
            return ApiResponse<PagedResult<QaSessionListItem>>.Fail("get_sessions_error", ex.Message);
        }
    }

    public async Task<ApiResponse<object>> DeleteSessionAsync(
        Guid userId,
        Guid sessionId,
        CancellationToken ct = default)
    {
        try
        {
            var session = await _db.QaSessions
                .FirstOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId, ct);
            if (session == null)
            {
                return ApiResponse<object>.Fail("session_not_found", "Session not found");
            }

            var messages = await _db.QaMessages
                .Where(m => m.SessionId == sessionId && m.UserId == userId)
                .ToListAsync(ct);
            var messageIds = messages.Select(m => m.Id).ToList();
            var retrievalLogs = await _db.RetrievalLogs
                .Where(log => log.UserId == userId && log.QaMessageId.HasValue && messageIds.Contains(log.QaMessageId.Value))
                .ToListAsync(ct);

            _db.RetrievalLogs.RemoveRange(retrievalLogs);
            _db.QaMessages.RemoveRange(messages);
            _db.QaSessions.Remove(session);
            await _db.SaveChangesAsync(ct);

            return ApiResponse<object>.Ok(new { deleted = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete QA session {SessionId}", sessionId);
            return ApiResponse<object>.Fail("delete_session_error", ex.Message);
        }
    }

    // ===== Private Helpers =====

    private static readonly Regex FollowUpQueryRegex = new(
        @"(它|他|她|这个|该|上述|前面|其中|这些|那些|其|对此|然后|后来|什么时候|怎么样|为什么|呢)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex CitationMarkerRegex = new(@"\[(\d+)\]", RegexOptions.Compiled);

    private static string CompleteQueryFromHistory(string query, IReadOnlyList<QaMessage> history)
    {
        var current = query.Trim();
        if (history.Count == 0 || string.IsNullOrWhiteSpace(current)) return current;
        var requiresContext = current.Length <= 24 || FollowUpQueryRegex.IsMatch(current);
        if (!requiresContext) return current;
        var previousQuestion = history.LastOrDefault(message => message.Role == "user")?.Content?.Trim();
        if (string.IsNullOrWhiteSpace(previousQuestion)) return current;
        return $"{previousQuestion}；追问：{current}";
    }

    private static string? BuildHistoryText(IReadOnlyList<QaMessage> history)
    {
        if (history.Count == 0) return null;
        var sb = new System.Text.StringBuilder();
        foreach (var message in history.OrderBy(message => message.CreatedAt))
        {
            var role = message.Role == "user" ? "用户" : "助手";
            var content = message.Content.Length > 1200 ? message.Content[..1200] + "…" : message.Content;
            sb.AppendLine($"{role}：{content}");
        }
        return sb.ToString();
    }

    private async Task<EmbeddingDiagnostics> GetEmbeddingDiagnosticsAsync(
        Guid userId, Guid? topicId, CancellationToken ct)
    {
        var chunksQuery =
            from chunk in _db.DocumentChunks.AsNoTracking()
            join document in _db.Documents.AsNoTracking() on chunk.DocumentId equals document.Id
            where chunk.UserId == userId && (!topicId.HasValue || document.TopicId == topicId)
            select chunk.Id;
        var chunkIds = await chunksQuery.ToListAsync(ct);
        var rows = chunkIds.Count == 0
            ? new List<(Guid ChunkId, string Status)>()
            : (await _db.ChunkEmbeddings.AsNoTracking()
                .Where(embedding => chunkIds.Contains(embedding.ChunkId))
                .Select(embedding => new { embedding.ChunkId, embedding.Status })
                .ToListAsync(ct))
                .Select(row => (row.ChunkId, row.Status))
                .ToList();

        var doneChunks = rows.Where(row => row.Status == "done").Select(row => row.ChunkId).Distinct().Count();
        var diagnostics = new EmbeddingDiagnostics
        {
            EligibleChunkCount = chunkIds.Count,
            TotalEmbeddingCount = rows.Count,
            DoneCount = rows.Count(row => row.Status == "done"),
            PendingCount = rows.Count(row => row.Status is "pending" or "processing"),
            FailedCount = rows.Count(row => row.Status == "failed"),
            StaleCount = rows.Count(row => row.Status == "stale"),
            Coverage = chunkIds.Count == 0 ? 0 : Math.Round((double)doneChunks / chunkIds.Count, 3)
        };
        (diagnostics.Status, diagnostics.Message) = diagnostics switch
        {
            { EligibleChunkCount: 0 } => ("empty", "当前范围内没有可检索的文档分块。"),
            { DoneCount: 0, FailedCount: > 0 } => ("failed", "Embedding 全部不可用，请检查向量模型连接并重试失败任务。"),
            { DoneCount: 0 } => ("pending", "Embedding 尚未完成，当前只能使用关键词与中文全文索引。"),
            { Coverage: < 0.5 } => ("degraded", "Embedding 覆盖率低于 50%，向量召回可能不完整。"),
            { FailedCount: > 0 } => ("warning", "部分 Embedding 生成失败，建议在文档诊断中重试。"),
            _ => ("healthy", "Embedding 状态正常。")
        };
        return diagnostics;
    }

    private static string BuildNoResultAnswer(EmbeddingDiagnostics diagnostics)
    {
        var diagnosis = diagnostics.Status == "healthy" ? string.Empty : $"\n\n诊断：{diagnostics.Message}";
        return "抱歉，我在当前知识库中没有找到足够相关的资料。请尝试调整关键词、切换专题或补充资料。" + diagnosis;
    }

    private static double CalculateEvidenceRelevance(SearchResultItem item)
    {
        var vector = item.ScoreDetail?.VectorScore ?? 0;
        var keyword = item.ScoreDetail?.KeywordScore ?? 0;
        var multiChannel = item.MatchChannels.Distinct(StringComparer.OrdinalIgnoreCase).Count() >= 2 ? 0.30 : 0;
        return Math.Clamp(Math.Max(Math.Max(vector, keyword), multiChannel), 0, 1);
    }

    private static (string Answer, List<string> Issues) ValidateAndRepairCitations(
        string answer, IReadOnlyList<Citation> citations)
    {
        var issues = new List<string>();
        var validIndices = citations.Select(citation => citation.Index).ToHashSet();
        var repaired = CitationMarkerRegex.Replace(answer, match =>
        {
            var index = int.Parse(match.Groups[1].Value);
            if (validIndices.Contains(index)) return match.Value;
            issues.Add($"移除不存在的引用编号 [{index}]");
            return string.Empty;
        });
        var used = CitationMarkerRegex.Matches(repaired)
            .Select(match => int.Parse(match.Groups[1].Value))
            .Where(validIndices.Contains)
            .Distinct()
            .ToList();
        if (citations.Count > 0 && used.Count == 0)
        {
            var markers = string.Join(" ", citations.Take(3).Select(citation => $"[{citation.Index}]"));
            repaired = repaired.TrimEnd() + $"\n\n参考来源：{markers}";
            issues.Add("回答未包含有效引用，已补充来源编号");
        }
        return (repaired, issues);
    }

    private static QaDebugInfo BuildDebugInfo(
        Guid? topicId,
        string originalQuery,
        string completedQuery,
        IReadOnlyList<SearchResultItem> searchResults,
        string? ragContext,
        EmbeddingDiagnostics embeddingDiagnostics,
        List<string> citationIssues,
        string? systemPrompt = null)
    {
        return new QaDebugInfo
        {
            QueryPlan = $"history_completion -> tokenized_keyword + fts_zh + multi_vector -> rrf -> evidence_gate(topic={topicId?.ToString() ?? "all"}, top_k={RetrievalTopK})",
            OriginalQuery = originalQuery,
            CompletedQuery = completedQuery,
            ContextTokens = ragContext == null ? null : ragContext.Length / 4,
            RetrievedTitles = searchResults.Take(5).Select(result => result.Title).ToList(),
            SystemPrompt = systemPrompt,
            EmbeddingDiagnostics = embeddingDiagnostics,
            CitationValidationIssues = citationIssues
        };
    }

    private static (string context, List<Citation> citations) BuildRagContext(
        List<SearchResultItem> chunks)
    {
        var sb = new System.Text.StringBuilder();
        var citations = new List<Citation>();

        for (var i = 0; i < chunks.Count; i++)
        {
            var chunk = chunks[i];
            var citationIndex = i + 1;

            sb.AppendLine($"[{citationIndex}] 中文标题: {chunk.TitleZh ?? chunk.Title}");
            if (!string.IsNullOrWhiteSpace(chunk.TitleOriginal))
                sb.AppendLine($"    原文标题: {chunk.TitleOriginal}");
            if (!string.IsNullOrEmpty(chunk.SourceUrl))
            {
                sb.AppendLine($"    Source: {chunk.SourceUrl}");
            }
            sb.AppendLine($"    Document ID: {chunk.DocumentId}");
            sb.AppendLine($"    Chunk ID: {chunk.ChunkId}");
            if (!string.IsNullOrWhiteSpace(chunk.LocalizedSnippet))
                sb.AppendLine($"    中文元数据: {chunk.LocalizedSnippet}");
            sb.AppendLine($"    原文证据: {chunk.OriginalSnippet ?? chunk.Snippet}");
            sb.AppendLine();

            citations.Add(new Citation
            {
                Index = citationIndex,
                DocumentId = chunk.DocumentId,
                ChunkId = chunk.ChunkId,
                Title = chunk.Title,
                SourceUrl = chunk.SourceUrl,
                SourceDomain = chunk.SourceDomain,
                SourceType = chunk.SourceType,
                Snippet = chunk.Snippet,
                Score = chunk.Score,
                TitleOriginal = chunk.TitleOriginal,
                TitleZh = chunk.TitleZh,
                DisplaySnippet = chunk.LocalizedSnippet ?? chunk.Snippet,
                OriginalSnippet = chunk.OriginalSnippet ?? chunk.Snippet,
                ContentLanguage = chunk.ContentLanguage,
                DisplayContentSource = chunk.DisplayContentSource,
                ChunkGroupId = chunk.ChunkGroupId,
                Section = chunk.Section,
                PageStart = chunk.PageStart,
                PageEnd = chunk.PageEnd,
                LocalizationId = chunk.LocalizationId,
                TranslationType = chunk.TranslationType,
                ReviewStatus = chunk.ReviewStatus
            });
        }

        return (sb.ToString(), citations);
    }

    private static List<Citation> BuildCitations(List<SearchResultItem> chunks)
    {
        var citations = new List<Citation>();
        for (var i = 0; i < chunks.Count; i++)
        {
            var chunk = chunks[i];
            citations.Add(new Citation
            {
                Index = i + 1,
                DocumentId = chunk.DocumentId,
                ChunkId = chunk.ChunkId,
                Title = chunk.Title,
                SourceUrl = chunk.SourceUrl,
                SourceDomain = chunk.SourceDomain,
                SourceType = chunk.SourceType,
                Snippet = chunk.Snippet,
                Score = chunk.Score
            });
        }
        return citations;
    }

    private static double CalculateConfidence(double topScore, int citationCount, int usedCount)
    {
        // Retrieval quality (0-0.5)
        var retrievalConfidence = Math.Min(topScore / 0.8, 1.0) * 0.5;

        // Citation coverage (0-0.3)
        var citationConfidence = Math.Min(citationCount / 5.0, 1.0) * 0.3;

        // Threshold bonus (0-0.2)
        var thresholdConfidence = topScore > 0.5 ? 0.2 : (topScore > 0.35 ? 0.1 : 0.0);

        return Math.Round(retrievalConfidence + citationConfidence + thresholdConfidence, 2);
    }

    private static string BuildDegradedAnswer(string query, List<SearchResultItem> chunks)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("⚠️ AI 模型暂时不可用，以下是基于检索资料生成的摘要回答：\n");
        sb.AppendLine($"问题：{query}\n");
        sb.AppendLine("相关资料：\n");

        for (var i = 0; i < chunks.Count; i++)
        {
            var chunk = chunks[i];
            sb.AppendLine($"[{i + 1}] {chunk.Title}");
            if (!string.IsNullOrEmpty(chunk.Snippet))
            {
                sb.AppendLine($"   {chunk.Snippet}");
            }
            sb.AppendLine();
        }

        sb.AppendLine("（此回答由系统自动生成，未经 AI 模型处理，仅供参考。模型恢复后可重新提问获取更精准的回答。）");
        return sb.ToString();
    }

    private static string BuildSystemPrompt()
    {
        return @"你是一个专业的知识库问答助手。请根据提供的参考资料回答用户的问题。

规则：
1. 只能基于提供的参考资料回答问题，不要编造或使用资料之外的信息
2. 如果参考资料不足以完全回答问题，请明确说明哪些部分有资料支持，哪些部分缺少资料
3. 回答时请在相关信息处标注引用编号，如 [1]、[2] 等，对应参考资料的编号
3.1 中文元数据用于理解与展示，但事实核验必须以“原文证据”为准；引用必须能回溯到原文文档、分块和页码
4. 回答要简洁、准确、有条理
5. 如果参考资料与问题完全无关，请说明资料相关性不足
6. 使用中文回答

回答风格模板：
- 定义类问题（什么是X）：先给出简洁定义，再补充关键特征和示例
- 对比类问题（A和B的区别）：使用表格或分点对比
- 流程类问题（如何做X）：按步骤编号说明
- 列举类问题（有哪些X）：使用编号列表
- 分析类问题（为什么X）：先给结论，再列理由

格式要求：
- 使用 Markdown 格式
- 关键信息加粗
- 长回答使用分段和标题";
    }

    private static string BuildUserPrompt(string query, string ragContext, string? history = null)
    {
        var sb = new System.Text.StringBuilder();
        if (!string.IsNullOrEmpty(history))
        {
            sb.AppendLine("之前的对话历史：");
            sb.AppendLine(history);
            sb.AppendLine("---");
            sb.AppendLine();
        }
        sb.AppendLine("参考资料：");
        sb.AppendLine();
        sb.AppendLine(ragContext);
        sb.AppendLine();
        sb.AppendLine($"用户问题：{query}");
        sb.AppendLine();
        sb.AppendLine("请基于以上参考资料回答问题，并在相关信息处标注引用编号 [1]、[2] 等。");
        return sb.ToString();
    }

    private static string RewriteQuery(string query)
    {
        var trimmed = query.Trim();
        if (string.IsNullOrEmpty(trimmed)) return trimmed;

        // Remove common question prefixes for better search matching
        var prefixes = new[] { "什么是", "什么是 ", "请解释", "请问", "解释一下", "如何理解", "什么叫", "什么是" };
        foreach (var prefix in prefixes)
        {
            if (trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return trimmed[prefix.Length..].Trim();
            }
        }

        // Remove trailing question marks
        return trimmed.TrimEnd('？', '?', '。', '.');
    }

    // DTO for deserializing retrieval snapshot
    private class RetrievalSnapshotDto
    {
        [JsonPropertyName("search_type")]
        public string? SearchType { get; set; }

        [JsonPropertyName("retrieved_count")]
        public int RetrievedCount { get; set; }

        [JsonPropertyName("used_count")]
        public int UsedCount { get; set; }

        [JsonPropertyName("top_score")]
        public double TopScore { get; set; }
    }
}
