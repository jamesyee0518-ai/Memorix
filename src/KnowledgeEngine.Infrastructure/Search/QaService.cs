using System.Text.Json;
using System.Text.Json.Serialization;
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
    private readonly ILogger<QaService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public QaService(
        IAppDbContext db,
        ISearchService searchService,
        ILlmService llmService,
        IOptions<LlmSettings> llmSettings,
        ILogger<QaService> logger)
    {
        _db = db;
        _searchService = searchService;
        _llmService = llmService;
        _llmSettings = llmSettings.Value;
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

            // Step 1.5: Query rewriting (rule-based)
            var rewrittenQuery = RewriteQuery(request.Query);
            if (rewrittenQuery != request.Query)
            {
                _logger.LogInformation("Query rewritten: {Original} → {Rewritten}", request.Query, rewrittenQuery);
            }

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
                var noResultAnswer = "抱歉，我在当前知识库中没有找到与您问题相关的资料。请尝试添加更多资料或调整问题后重试。";

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
                        top_score = 0.0
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
                    Confidence = 0.0
                });
            }

            // Check top similarity threshold
            var topScore = searchResults.First().Score;
            if (topScore < 0.25)
            {
                sw.Stop();
                var lowRelevanceAnswer = "我找到了一些资料，但它们与您的问题相关性较低（最高相关度低于0.25）。以下是我能提供的信息，请谨慎参考：\n\n" +
                    string.Join("\n\n", searchResults.Take(3).Select(r => $"- {r.Title}: {r.Snippet}"));

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
                        top_score = topScore
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
                    Confidence = CalculateConfidence(topScore, lowRelevanceCitations.Count, 3)
                });
            }

            // Step 4: Rerank - take top chunks, max 3 per document
            var rerankedChunks = RerankChunks(searchResults, MaxContextChunks, MaxChunksPerDocument);

            // Step 5: Build RAG context
            var (ragContext, citations) = BuildRagContext(rerankedChunks);

            // Step 6: Build prompts
            var systemPrompt = BuildSystemPrompt();

            // Fetch recent conversation history for multi-turn context
            var recentMessages = await _db.QaMessages
                .Where(m => m.SessionId == session.Id && m.Role == "user" && m.Id != userMessage.Id)
                .OrderByDescending(m => m.CreatedAt)
                .Take(3)
                .OrderBy(m => m.CreatedAt)
                .ToListAsync(ct);

            var recentAssistantMessages = await _db.QaMessages
                .Where(m => m.SessionId == session.Id && m.Role == "assistant" && m.CreatedAt < now)
                .OrderByDescending(m => m.CreatedAt)
                .Take(3)
                .OrderBy(m => m.CreatedAt)
                .ToListAsync(ct);

            // Build conversation history string
            var historySb = new System.Text.StringBuilder();
            var allHistory = recentMessages.Zip(recentAssistantMessages, (u, a) => new { u, a })
                .OrderBy(x => x.u.CreatedAt)
                .ToList();
            foreach (var h in allHistory)
            {
                historySb.AppendLine($"用户：{h.u.Content}");
                historySb.AppendLine($"助手：{h.a.Content}");
                historySb.AppendLine();
            }
            var historyText = historySb.Length > 0 ? historySb.ToString() : null;

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

            sw.Stop();

            // Step 8: Build citations from used chunks (system-generated, not model-generated)
            var citationsJson = JsonSerializer.Serialize(citations, JsonOptions);

            var confidence = modelUsed == "degraded"
                ? Math.Round(CalculateConfidence(topScore, citations.Count, rerankedChunks.Count) * 0.5, 2) // halve confidence for degraded
                : CalculateConfidence(topScore, citations.Count, rerankedChunks.Count);
            var debugInfo = new QaDebugInfo
            {
                QueryPlan = $"hybrid_search(topic={topicId?.ToString() ?? "all"}, top_k={RetrievalTopK})",
                ContextTokens = ragContext.Length / 4, // rough estimate
                RetrievedTitles = searchResults.Take(5).Select(r => r.Title).ToList(),
                SystemPrompt = systemPrompt
            };

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
                        citations = JsonSerializer.Deserialize<List<Citation>>(m.Citations);
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

    // ===== Private Helpers =====

    private static List<SearchResultItem> RerankChunks(
        List<SearchResultItem> results,
        int maxChunks,
        int maxPerDocument)
    {
        var byDocument = new Dictionary<Guid, List<SearchResultItem>>();

        foreach (var item in results.OrderByDescending(r => r.Score))
        {
            if (!byDocument.ContainsKey(item.DocumentId))
            {
                byDocument[item.DocumentId] = new List<SearchResultItem>();
            }

            if (byDocument[item.DocumentId].Count < maxPerDocument)
            {
                byDocument[item.DocumentId].Add(item);
            }
        }

        return byDocument
            .SelectMany(kv => kv.Value)
            .OrderByDescending(r => r.Score)
            .Take(maxChunks)
            .ToList();
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

            sb.AppendLine($"[{citationIndex}] Document: {chunk.Title}");
            if (!string.IsNullOrEmpty(chunk.SourceUrl))
            {
                sb.AppendLine($"    Source: {chunk.SourceUrl}");
            }
            sb.AppendLine($"    Document ID: {chunk.DocumentId}");
            sb.AppendLine($"    Chunk ID: {chunk.ChunkId}");
            sb.AppendLine($"    Content: {chunk.Snippet}");
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
                Score = chunk.Score
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
