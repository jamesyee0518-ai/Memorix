using System.Data;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
using KnowledgeEngine.Application.DTOs;
using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Domain.Entities;
using KnowledgeEngine.Infrastructure.Db;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KnowledgeEngine.Infrastructure.Processing;

public sealed class ChineseTokenizer : IChineseTokenizer
{
    private static readonly Regex LatinToken = new(@"[\p{L}\p{N}][\p{L}\p{N}._+#-]*", RegexOptions.Compiled);

    public string Tokenize(string? text, IEnumerable<string>? protectedTerms = null)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var input = text.Normalize(NormalizationForm.FormKC).ToLowerInvariant();

        foreach (var term in protectedTerms ?? Array.Empty<string>())
        {
            var normalized = term.Trim().ToLowerInvariant();
            if (normalized.Length > 0 && input.Contains(normalized, StringComparison.Ordinal))
                tokens.Add(normalized);
        }

        foreach (Match match in LatinToken.Matches(input))
        {
            var token = match.Value.Trim('.', '-', '_');
            if (token.Length > 1) tokens.Add(token);
        }

        var cjk = input.Where(IsCjk).ToArray();
        for (var i = 0; i < cjk.Length; i++)
        {
            tokens.Add(cjk[i].ToString());
            if (i + 1 < cjk.Length) tokens.Add(new string(new[] { cjk[i], cjk[i + 1] }));
            if (i + 2 < cjk.Length) tokens.Add(new string(new[] { cjk[i], cjk[i + 1], cjk[i + 2] }));
        }

        return string.Join(' ', tokens.OrderBy(t => t, StringComparer.Ordinal));
    }

    private static bool IsCjk(char c) =>
        c is >= '\u3400' and <= '\u4DBF' or >= '\u4E00' and <= '\u9FFF' or >= '\uF900' and <= '\uFAFF';
}

public sealed class LocalizationQualityService : ILocalizationQualityService
{
    private static readonly Regex NumberPattern = new(@"(?<![\p{L}\p{N}])[-+]?\d[\d,]*(?:\.\d+)?%?", RegexOptions.Compiled);
    private static readonly Regex UnitPattern = new(@"\b(?:kg|g|mg|km|m|cm|mm|gb|mb|kb|tb|ms|s|hz|khz|mhz|ghz|usd|eur|cny|rmb)\b|[℃°%￥$€]", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex EnglishNegation = new(@"\b(?:not|never|no|without|neither|nor|cannot|can't|won't|isn't|aren't|doesn't|didn't)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex ChineseNegation = new(@"(?:不|未|无|非|否|没有|不可|不能|从未)", RegexOptions.Compiled);

    public LocalizationQualityResult Validate(string? sourceText, string? localizedText, IEnumerable<Terminology>? terminology = null)
    {
        sourceText ??= string.Empty;
        localizedText ??= string.Empty;
        var issues = new List<string>();
        var score = 100;

        var sourceNumbers = Extract(NumberPattern, sourceText);
        var targetNumbers = Extract(NumberPattern, localizedText);
        foreach (var missing in sourceNumbers.Except(targetNumbers, StringComparer.OrdinalIgnoreCase).Take(5))
        { issues.Add($"数字缺失或变化：{missing}"); score -= 12; }

        var sourceUnits = Extract(UnitPattern, sourceText);
        var targetUnits = Extract(UnitPattern, localizedText);
        foreach (var missing in sourceUnits.Except(targetUnits, StringComparer.OrdinalIgnoreCase).Take(4))
        { issues.Add($"单位缺失或变化：{missing}"); score -= 8; }

        if (EnglishNegation.IsMatch(sourceText) && !ChineseNegation.IsMatch(localizedText))
        { issues.Add("原文包含否定语义，中文元数据未检测到否定词"); score -= 15; }

        foreach (var term in (terminology ?? Array.Empty<Terminology>())
                     .Where(t => string.Equals(t.ReviewStatus, "approved", StringComparison.OrdinalIgnoreCase)))
        {
            if (TerminologyService.Variants(term).Any(v => sourceText.Contains(v, StringComparison.OrdinalIgnoreCase))
                && !localizedText.Contains(term.TargetTerm, StringComparison.OrdinalIgnoreCase))
            { issues.Add($"术语未按词库映射：{term.SourceTerm} → {term.TargetTerm}"); score -= 10; }
        }

        score = Math.Clamp(score, 0, 100);
        return new LocalizationQualityResult(score, issues.Distinct().Take(20).ToList(), score < 85);
    }

    private static HashSet<string> Extract(Regex regex, string text) => regex.Matches(text)
        .Select(m => m.Value.Replace(",", string.Empty, StringComparison.Ordinal).ToLowerInvariant())
        .ToHashSet(StringComparer.OrdinalIgnoreCase);
}

public sealed class TerminologyService : ITerminologyService
{
    private static readonly HashSet<string> ReviewStatuses = new(StringComparer.OrdinalIgnoreCase)
        { "draft", "pending", "approved", "rejected" };
    private static readonly Regex CandidatePattern = new(
        @"\b(?:[A-Z]{2,}(?:[-/.][A-Z0-9]+)*|[A-Z][A-Za-z0-9+#.-]{2,}(?:\s+[A-Z][A-Za-z0-9+#.-]{2,}){0,3})\b",
        RegexOptions.Compiled);
    private readonly IAppDbContext _db;

    public TerminologyService(IAppDbContext db) => _db = db;

    public async Task<PagedResult<Terminology>> ListAsync(
        Guid userId, Guid workspaceId, TerminologyQuery request, CancellationToken ct = default)
    {
        var query = _db.Terminology.AsNoTracking()
            .Where(t => t.UserId == userId && t.WorkspaceId == workspaceId);
        if (!string.IsNullOrWhiteSpace(request.SourceLanguage))
            query = query.Where(t => t.SourceLanguage == request.SourceLanguage);
        if (!string.IsNullOrWhiteSpace(request.TargetLanguage))
            query = query.Where(t => t.TargetLanguage == request.TargetLanguage);
        if (!string.IsNullOrWhiteSpace(request.Domain))
            query = query.Where(t => t.Domain == request.Domain);
        if (!string.IsNullOrWhiteSpace(request.ReviewStatus))
            query = query.Where(t => t.ReviewStatus == request.ReviewStatus);
        if (!string.IsNullOrWhiteSpace(request.Query))
        {
            var pattern = $"%{request.Query.Trim()}%";
            query = query.Where(t => EF.Functions.Like(t.SourceTerm, pattern)
                                  || EF.Functions.Like(t.TargetTerm, pattern)
                                  || (t.Aliases != null && EF.Functions.Like(t.Aliases, pattern)));
        }

        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, 200);
        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(t => t.Priority).ThenBy(t => t.SourceTerm)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new PagedResult<Terminology> { Items = items, Total = total, Page = page, PageSize = pageSize };
    }

    public async Task<IReadOnlyList<Terminology>> SelectForContentAsync(
        Guid userId, Guid? workspaceId, string? sourceLanguage, string? targetLanguage,
        string? domain, string? content, int maxPromptCharacters = 12000, CancellationToken ct = default)
    {
        var query = _db.Terminology.AsNoTracking()
            .Where(t => t.UserId == userId && t.ReviewStatus == "approved");
        if (workspaceId.HasValue)
            query = query.Where(t => t.WorkspaceId == workspaceId);
        if (!string.IsNullOrWhiteSpace(sourceLanguage) && sourceLanguage != "und")
            query = query.Where(t => t.SourceLanguage == sourceLanguage || t.SourceLanguage == "und");
        if (!string.IsNullOrWhiteSpace(targetLanguage))
            query = query.Where(t => t.TargetLanguage == targetLanguage);
        if (!string.IsNullOrWhiteSpace(domain))
            query = query.Where(t => t.Domain == null || t.Domain == "" || t.Domain == domain);

        var terms = await query.OrderByDescending(t => t.Priority).ThenByDescending(t => t.UpdatedAt).ToListAsync(ct);
        if (!string.IsNullOrWhiteSpace(content))
        {
            terms = terms.OrderByDescending(t => MatchesAny(content, Variants(t)))
                .ThenByDescending(t => t.Priority).ThenByDescending(t => t.UpdatedAt).ToList();
        }

        var selected = new List<Terminology>();
        var used = 0;
        foreach (var term in terms)
        {
            var lineLength = term.SourceTerm.Length + term.TargetTerm.Length
                + ParseAliases(term.Aliases).Sum(x => x.Length) + 16;
            if (selected.Count > 0 && used + lineLength > Math.Clamp(maxPromptCharacters, 1000, 50000)) break;
            selected.Add(term);
            used += lineLength;
        }
        return selected;
    }

    public async Task<Terminology> UpsertAsync(
        Guid userId, Guid workspaceId, Terminology term, bool queueReprocess = true, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(term.SourceTerm) || string.IsNullOrWhiteSpace(term.TargetTerm))
            throw new ArgumentException("SourceTerm and TargetTerm are required");

        Normalize(term);
        if (!ReviewStatuses.Contains(term.ReviewStatus))
            throw new ArgumentException("ReviewStatus must be draft, pending, approved, or rejected");

        var current = term.Id == Guid.Empty ? null : await _db.Terminology
            .FirstOrDefaultAsync(t => t.Id == term.Id && t.UserId == userId && t.WorkspaceId == workspaceId, ct);
        current ??= await _db.Terminology.FirstOrDefaultAsync(t => t.UserId == userId && t.WorkspaceId == workspaceId
            && t.SourceLanguage == term.SourceLanguage && t.SourceTerm.ToLower() == term.SourceTerm.ToLower()
            && t.TargetLanguage == term.TargetLanguage && t.TargetTerm.ToLower() == term.TargetTerm.ToLower(), ct);

        var conflict = await _db.Terminology.AsNoTracking().FirstOrDefaultAsync(t =>
            t.UserId == userId && t.WorkspaceId == workspaceId && t.Id != (current == null ? Guid.Empty : current.Id)
            && t.SourceLanguage == term.SourceLanguage && t.TargetLanguage == term.TargetLanguage
            && t.SourceTerm.ToLower() == term.SourceTerm.ToLower()
            && t.TargetTerm.ToLower() != term.TargetTerm.ToLower(), ct);
        if (conflict != null)
            throw new InvalidOperationException(
                $"术语冲突：{term.SourceTerm} 已映射为“{conflict.TargetTerm}”，请先处理冲突");

        var changedVariants = current == null ? Variants(term).ToList() : Variants(current).Concat(Variants(term)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (current == null)
        {
            current = term;
            current.Id = current.Id == Guid.Empty ? Guid.NewGuid() : current.Id;
            current.UserId = userId;
            current.WorkspaceId = workspaceId;
            current.CreatedAt = DateTime.UtcNow;
            _db.Terminology.Add(current);
        }
        else
        {
            current.SourceLanguage = term.SourceLanguage;
            current.SourceTerm = term.SourceTerm.Trim();
            current.TargetLanguage = term.TargetLanguage;
            current.TargetTerm = term.TargetTerm.Trim();
            current.Aliases = term.Aliases;
            current.Domain = term.Domain;
            current.Priority = term.Priority;
            current.ReviewStatus = term.ReviewStatus;
            current.Version = term.Version;
        }
        current.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        if (queueReprocess)
            await QueueAffectedDocumentsAsync(userId, workspaceId, changedVariants, ct);
        return current;
    }

    public async Task<bool> DeleteAsync(Guid userId, Guid workspaceId, Guid id, CancellationToken ct = default)
    {
        var term = await _db.Terminology.FirstOrDefaultAsync(
            t => t.Id == id && t.UserId == userId && t.WorkspaceId == workspaceId, ct);
        if (term == null) return false;
        var variants = Variants(term).ToList();
        _db.Terminology.Remove(term);
        await _db.SaveChangesAsync(ct);
        await QueueAffectedDocumentsAsync(userId, workspaceId, variants, ct);
        return true;
    }

    public async Task<IReadOnlyList<string>> ExpandQueryAsync(
        Guid userId, Guid? workspaceId, string query, string? sourceLanguage = null,
        string? targetLanguage = null, string? domain = null, CancellationToken ct = default)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { query.Trim() };
        var terms = await SelectForContentAsync(
            userId, workspaceId, sourceLanguage, targetLanguage, domain, query, 50000, ct);
        foreach (var term in terms)
        {
            var variants = Variants(term).ToList();
            if (variants.Any(v => ContainsTerm(query, v)))
                foreach (var variant in variants.Where(v => !string.IsNullOrWhiteSpace(v))) result.Add(variant.Trim());
        }
        return result.ToList();
    }

    public async Task<TerminologyBulkResult> BulkUpsertAsync(
        Guid userId, Guid workspaceId, TerminologyBulkRequest request, CancellationToken ct = default)
    {
        if (request.Items.Count > 5000) throw new ArgumentException("A bulk request can contain at most 5000 terms");
        var result = new TerminologyBulkResult();
        var variants = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var term in request.Items)
        {
            try
            {
                Normalize(term);
                var exists = term.Id != Guid.Empty && await _db.Terminology.AsNoTracking()
                    .AnyAsync(t => t.Id == term.Id && t.UserId == userId && t.WorkspaceId == workspaceId, ct);
                await UpsertAsync(userId, workspaceId, term, false, ct);
                if (exists) result.Updated++; else result.Created++;
                foreach (var variant in Variants(term)) variants.Add(variant);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                result.Skipped++;
                if (result.Errors.Count < 100) result.Errors.Add($"{term.SourceTerm}: {ex.Message}");
                if (!request.SkipConflicts) throw;
            }
        }
        result.ReprocessJobsQueued = await QueueAffectedDocumentsAsync(userId, workspaceId, variants, ct);
        return result;
    }

    public async Task<Terminology> ReviewAsync(
        Guid userId, Guid workspaceId, Guid id, string status, CancellationToken ct = default)
    {
        if (!ReviewStatuses.Contains(status)) throw new ArgumentException("Unsupported review status");
        var term = await _db.Terminology.FirstOrDefaultAsync(
            t => t.Id == id && t.UserId == userId && t.WorkspaceId == workspaceId, ct)
            ?? throw new KeyNotFoundException("Terminology was not found");
        var changed = !string.Equals(term.ReviewStatus, status, StringComparison.OrdinalIgnoreCase);
        term.ReviewStatus = status.ToLowerInvariant();
        term.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        if (changed) await QueueAffectedDocumentsAsync(userId, workspaceId, Variants(term), ct);
        return term;
    }

    public async Task<IReadOnlyList<TerminologyConflict>> ListConflictsAsync(
        Guid userId, Guid workspaceId, CancellationToken ct = default)
    {
        var terms = await _db.Terminology.AsNoTracking()
            .Where(t => t.UserId == userId && t.WorkspaceId == workspaceId).ToListAsync(ct);
        return terms.GroupBy(t => new
            {
                SourceLanguage = t.SourceLanguage.ToLowerInvariant(),
                SourceTerm = t.SourceTerm.Trim().ToLowerInvariant(),
                TargetLanguage = t.TargetLanguage.ToLowerInvariant()
            })
            .Where(g => g.Select(x => x.TargetTerm.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
            .Select(g => new TerminologyConflict
            {
                SourceLanguage = g.First().SourceLanguage,
                SourceTerm = g.First().SourceTerm,
                TargetLanguage = g.First().TargetLanguage,
                Terms = g.OrderByDescending(x => x.Priority).ToList()
            }).ToList();
    }

    public async Task<TerminologyStats> GetStatsAsync(Guid userId, Guid workspaceId, CancellationToken ct = default)
    {
        var terms = await _db.Terminology.AsNoTracking()
            .Where(t => t.UserId == userId && t.WorkspaceId == workspaceId).ToListAsync(ct);
        var conflicts = await ListConflictsAsync(userId, workspaceId, ct);
        var documentIds = await _db.Documents.AsNoTracking()
            .Where(d => d.UserId == userId && d.WorkspaceId == workspaceId).Select(d => d.Id).ToListAsync(ct);
        var pendingJobs = await _db.MultilingualBatchJobs.AsNoTracking().CountAsync(j =>
            j.UserId == userId && documentIds.Contains(j.DocumentId)
            && j.JobType == "glossary_reprocess"
            && (j.Status == "pending" || j.Status == "running" || j.Status == "paused"), ct);
        return new TerminologyStats
        {
            Total = terms.Count,
            Approved = terms.Count(t => t.ReviewStatus == "approved"),
            PendingReview = terms.Count(t => t.ReviewStatus is "pending" or "draft"),
            Rejected = terms.Count(t => t.ReviewStatus == "rejected"),
            Conflicts = conflicts.Count,
            PendingReprocessJobs = pendingJobs,
            Domains = terms.GroupBy(t => string.IsNullOrWhiteSpace(t.Domain) ? "通用" : t.Domain!)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase),
            LanguagePairs = terms.GroupBy(t => $"{t.SourceLanguage}→{t.TargetLanguage}")
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase)
        };
    }

    public async Task<IReadOnlyList<TerminologyUsage>> GetUsageAsync(
        Guid userId, Guid workspaceId, IReadOnlyCollection<Guid> terminologyIds, CancellationToken ct = default)
    {
        var ids = terminologyIds.Distinct().Take(200).ToList();
        var terms = await _db.Terminology.AsNoTracking()
            .Where(t => t.UserId == userId && t.WorkspaceId == workspaceId && ids.Contains(t.Id)).ToListAsync(ct);
        var documents = await _db.Documents.AsNoTracking()
            .Where(d => d.UserId == userId && d.WorkspaceId == workspaceId)
            .Select(d => new { d.Id, d.Title, d.TitleOriginal, d.TitleZh, d.Summary, d.SummaryZh, d.ContentText })
            .Take(5000).ToListAsync(ct);
        var docIds = documents.Select(d => d.Id).ToList();
        var chunks = await _db.DocumentChunks.AsNoTracking().Where(c => docIds.Contains(c.DocumentId))
            .Select(c => new { c.DocumentId, c.Content, c.ContentOriginal, c.ContentNormalized })
            .Take(20000).ToListAsync(ct);
        return terms.Select(term =>
        {
            var variants = Variants(term).ToList();
            return new TerminologyUsage
            {
                TerminologyId = term.Id,
                DocumentCount = documents.Count(d => MatchesAny(
                    $"{d.Title}\n{d.TitleOriginal}\n{d.TitleZh}\n{d.Summary}\n{d.SummaryZh}\n{d.ContentText}", variants)),
                ChunkCount = chunks.Count(c => MatchesAny(
                    $"{c.ContentOriginal}\n{c.ContentNormalized}\n{c.Content}", variants))
            };
        }).ToList();
    }

    public async Task<IReadOnlyList<TerminologyCandidate>> ExtractCandidatesAsync(
        Guid userId, Guid workspaceId, TerminologyExtractionRequest request, CancellationToken ct = default)
    {
        var limit = Math.Clamp(request.DocumentLimit, 1, 500);
        var query = _db.Documents.AsNoTracking()
            .Where(d => d.UserId == userId && d.WorkspaceId == workspaceId);
        if (request.TopicId.HasValue) query = query.Where(d => d.TopicId == request.TopicId);
        var docs = await query.OrderByDescending(d => d.UpdatedAt).Take(limit)
            .Select(d => new { d.Id, d.Title, d.ContentText, d.Summary, d.SourceDomain }).ToListAsync(ct);
        var existing = (await _db.Terminology.AsNoTracking()
                .Where(t => t.UserId == userId && t.WorkspaceId == workspaceId)
                .Select(t => t.SourceTerm).ToListAsync(ct))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var candidates = new Dictionary<string, TerminologyCandidate>(StringComparer.OrdinalIgnoreCase);
        foreach (var doc in docs)
        {
            var text = $"{doc.Title}\n{doc.Summary}\n{doc.ContentText}";
            foreach (Match match in CandidatePattern.Matches(text))
            {
                var value = Regex.Replace(match.Value.Trim(), @"\s+", " ");
                if (value.Length < 3 || value.Length > 100 || existing.Contains(value)) continue;
                if (!candidates.TryGetValue(value, out var candidate))
                {
                    candidate = new TerminologyCandidate
                    {
                        SourceTerm = value, Domain = doc.SourceDomain, Occurrences = 0
                    };
                    candidates[value] = candidate;
                }
                candidate.Occurrences++;
                if (candidate.DocumentIds.Count < 10 && !candidate.DocumentIds.Contains(doc.Id))
                    candidate.DocumentIds.Add(doc.Id);
            }
        }
        return candidates.Values.OrderByDescending(x => x.Occurrences).ThenBy(x => x.SourceTerm)
            .Take(Math.Clamp(request.CandidateLimit, 1, 200)).ToList();
    }

    private async Task<int> QueueAffectedDocumentsAsync(
        Guid userId, Guid workspaceId, IEnumerable<string> variants, CancellationToken ct)
    {
        var terms = variants.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(100).ToList();
        if (terms.Count == 0) return 0;
        var documents = await _db.Documents.AsNoTracking()
            .Where(d => d.UserId == userId && d.WorkspaceId == workspaceId)
            .Select(d => new { d.Id, d.Title, d.TitleOriginal, d.ContentText, d.Summary })
            .Take(5000).ToListAsync(ct);
        var candidateIds = documents.Where(d => MatchesAny(
                $"{d.Title}\n{d.TitleOriginal}\n{d.Summary}\n{d.ContentText}", terms))
            .Select(d => d.Id).ToHashSet();
        if (candidateIds.Count < 500)
        {
            var chunkMatches = await _db.DocumentChunks.AsNoTracking()
                .Where(c => c.UserId == userId && documents.Select(d => d.Id).Contains(c.DocumentId))
                .Select(c => new { c.DocumentId, c.ContentOriginal, c.Content }).Take(20000).ToListAsync(ct);
            foreach (var chunk in chunkMatches.Where(c => MatchesAny($"{c.ContentOriginal}\n{c.Content}", terms)))
                candidateIds.Add(chunk.DocumentId);
        }

        var activeIds = await _db.MultilingualBatchJobs.AsNoTracking()
            .Where(j => j.UserId == userId && candidateIds.Contains(j.DocumentId)
                && j.JobType == "glossary_reprocess"
                && (j.Status == "pending" || j.Status == "running" || j.Status == "paused"))
            .Select(j => j.DocumentId).ToListAsync(ct);
        candidateIds.ExceptWith(activeIds);
        var chunkCounts = await _db.DocumentChunks.AsNoTracking().Where(c => candidateIds.Contains(c.DocumentId))
            .GroupBy(c => c.DocumentId).Select(g => new { DocumentId = g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.DocumentId, x => x.Count, ct);
        var now = DateTime.UtcNow;
        foreach (var documentId in candidateIds.Take(500))
        {
            var count = chunkCounts.GetValueOrDefault(documentId);
            _db.MultilingualBatchJobs.Add(new MultilingualBatchJob
            {
                Id = Guid.NewGuid(), UserId = userId, DocumentId = documentId,
                JobType = "glossary_reprocess", Status = "pending", Force = true,
                MaxChunks = Math.Max(1, Math.Min(count, 2000)), TotalItems = Math.Max(1, Math.Min(count, 2000)),
                CreatedAt = now, UpdatedAt = now
            });
        }
        if (candidateIds.Count > 0) await _db.SaveChangesAsync(ct);
        return Math.Min(candidateIds.Count, 500);
    }

    private static void Normalize(Terminology term)
    {
        term.SourceLanguage = string.IsNullOrWhiteSpace(term.SourceLanguage) ? "en" : term.SourceLanguage.Trim().ToLowerInvariant();
        term.TargetLanguage = string.IsNullOrWhiteSpace(term.TargetLanguage) ? "zh-CN" : term.TargetLanguage.Trim();
        term.SourceTerm = term.SourceTerm.Trim().Normalize(NormalizationForm.FormKC);
        term.TargetTerm = term.TargetTerm.Trim().Normalize(NormalizationForm.FormKC);
        term.Domain = string.IsNullOrWhiteSpace(term.Domain) ? null : term.Domain.Trim();
        term.Aliases = string.IsNullOrWhiteSpace(term.Aliases)
            ? null
            : JsonSerializer.Serialize(ParseAliases(term.Aliases).Select(x => x.Trim())
                .Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase));
        term.ReviewStatus = string.IsNullOrWhiteSpace(term.ReviewStatus) ? "pending" : term.ReviewStatus.Trim().ToLowerInvariant();
        term.Version = string.IsNullOrWhiteSpace(term.Version) ? "v1" : term.Version.Trim();
    }

    internal static IEnumerable<string> Variants(Terminology term) =>
        new[] { term.SourceTerm, term.TargetTerm }.Concat(ParseAliases(term.Aliases))
            .Where(v => !string.IsNullOrWhiteSpace(v)).Distinct(StringComparer.OrdinalIgnoreCase);

    internal static string FormatPromptLine(Terminology term)
    {
        var aliases = ParseAliases(term.Aliases).ToList();
        return aliases.Count == 0
            ? $"- {term.SourceTerm} => {term.TargetTerm}"
            : $"- {term.SourceTerm}（别名：{string.Join("、", aliases)}）=> {term.TargetTerm}";
    }

    private static bool MatchesAny(string? content, IEnumerable<string> terms) =>
        !string.IsNullOrWhiteSpace(content) && terms.Any(term => ContainsTerm(content, term));

    private static bool ContainsTerm(string content, string term)
    {
        if (string.IsNullOrWhiteSpace(term)) return false;
        var index = content.IndexOf(term, StringComparison.OrdinalIgnoreCase);
        while (index >= 0)
        {
            var leftOk = index == 0 || !char.IsLetterOrDigit(content[index - 1]) || IsCjk(term[0]);
            var end = index + term.Length;
            var rightOk = end >= content.Length || !char.IsLetterOrDigit(content[end]) || IsCjk(term[^1]);
            if (leftOk && rightOk) return true;
            index = content.IndexOf(term, index + 1, StringComparison.OrdinalIgnoreCase);
        }
        return false;
    }

    private static bool IsCjk(char c) => c is >= '\u3400' and <= '\u9fff';

    internal static IEnumerable<string> ParseAliases(string? aliases)
    {
        if (string.IsNullOrWhiteSpace(aliases)) return Array.Empty<string>();
        try { return JsonSerializer.Deserialize<string[]>(aliases) ?? Array.Empty<string>(); }
        catch { return aliases.Split(new[] { ',', '，', ';', '；' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries); }
    }
}

public sealed class L1LocalizationService : IL1LocalizationService
{
    private const string PromptVersion = "l1-zh-v2";
    private readonly IAppDbContext _db;
    private readonly ILlmService _llm;
    private readonly ITerminologyService _terminology;
    private readonly IChineseNormalizationService _normalizer;
    private readonly IChineseFullTextIndexService _fullText;
    private readonly ILocalizationQualityService _quality;
    private readonly IProcessingLogService _processingLog;

    public L1LocalizationService(IAppDbContext db, ILlmService llm, ITerminologyService terminology,
        IChineseNormalizationService normalizer, IChineseFullTextIndexService fullText, ILocalizationQualityService quality,
        IProcessingLogService processingLog)
    {
        _db = db; _llm = llm; _terminology = terminology; _normalizer = normalizer; _fullText = fullText;
        _quality = quality; _processingLog = processingLog;
    }

    public async Task<L1LocalizationResult> LocalizeDocumentAsync(Guid documentId, CancellationToken ct = default)
    {
        var doc = await _db.Documents.FirstOrDefaultAsync(d => d.Id == documentId, ct)
                  ?? throw new KeyNotFoundException($"Document {documentId} was not found");
        var started = DateTime.UtcNow;
        await _processingLog.LogAsync("default", doc.SourceId, doc.Id, "metadata_localization", "started", ct: ct);
        doc.LocalizationStatus = "processing";
        await _db.SaveChangesAsync(ct);

        try
        {
            L1LocalizationResult result;
            var sourceContent = string.Join('\n', new[] { doc.TitleOriginal ?? doc.Title, doc.Summary, doc.ContentText }
                .Where(x => !string.IsNullOrWhiteSpace(x)));
            var glossary = await _terminology.SelectForContentAsync(
                doc.UserId, doc.WorkspaceId, doc.PrimaryLanguage ?? doc.Language, "zh-CN",
                doc.SourceDomain, sourceContent, ct: ct);
            if ((doc.PrimaryLanguage ?? doc.Language)?.StartsWith("zh", StringComparison.OrdinalIgnoreCase) == true)
            {
                result = new L1LocalizationResult(
                    _normalizer.Normalize(doc.Title), _normalizer.Normalize(doc.Summary ?? doc.ContentText?.Substring(0, Math.Min(doc.ContentText.Length, 500))),
                    ExtractKeywords(doc.Title + " " + doc.Summary), "local-normalizer", PromptVersion);
            }
            else
            {
                var glossaryText = string.Join('\n', glossary.Select(TerminologyService.FormatPromptLine));
                var system = "你是严谨的中文知识库本地化助手。只输出 JSON，不虚构信息；保留专有名词、数字、版本号、URL 与代码。";
                var content = (doc.ContentText ?? string.Empty);
                if (content.Length > 8000) content = content[..8000];
                var user = $$"""
                    为文档生成 L1 中文元数据。术语表优先级最高，不得自行替换已规定译法。
                    输出：{"title_zh":"","summary_zh":"","keywords_zh":[""]}
                    摘要 120-300 个中文字符，关键词 5-12 个。

                    术语表：
                    {{glossaryText}}

                    原标题：{{doc.TitleOriginal ?? doc.Title}}
                    原摘要：{{doc.Summary}}
                    正文摘录：{{content}}
                    """;
                var llm = await _llm.CompleteAsync(system, user, ct: ct);
                var payload = ParseJson(llm.Content);
                result = new L1LocalizationResult(
                    payload.TitleZh, payload.SummaryZh, payload.KeywordsZh ?? Array.Empty<string>(), llm.Model, PromptVersion);
            }

            doc.TitleOriginal ??= doc.Title;
            doc.TitleZh = result.TitleZh;
            doc.SummaryZh = result.SummaryZh;
            doc.KeywordsZh = JsonSerializer.Serialize(result.KeywordsZh);
            doc.LocalizationStrategy = "metadata-only";
            doc.LocalizationLevel = "L1";
            doc.LocalizationStatus = "done";
            doc.LocalizationModel = result.Model;
            doc.LocalizationPromptVersion = result.PromptVersion;
            doc.LocalizedAt = DateTime.UtcNow;
            var qualitySource = string.Join(' ', new[]
            {
                doc.TitleOriginal ?? doc.Title,
                !string.IsNullOrWhiteSpace(doc.Summary) ? doc.Summary : doc.ContentText?[..Math.Min(doc.ContentText.Length, 1000)]
            }.Where(x => !string.IsNullOrWhiteSpace(x)));
            var quality = _quality.Validate(
                qualitySource,
                string.Join(' ', new[] { result.TitleZh, result.SummaryZh }.Concat(result.KeywordsZh)), glossary);
            doc.LocalizationQualityScore = quality.Score;
            doc.LocalizationQualityIssues = JsonSerializer.Serialize(quality.Issues);
            doc.GlossaryVersion = ComputeGlossaryVersion(glossary);
            doc.LocalizationStatus = quality.RequiresReview ? "review_required" : "done";
            doc.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            await _fullText.IndexDocumentAsync(documentId, ct);
            await _processingLog.LogAsync("default", doc.SourceId, doc.Id, "metadata_localization", "success",
                $"quality={quality.Score}; status={doc.LocalizationStatus}; model={result.Model}; glossary={doc.GlossaryVersion}",
                durationMs: (int)(DateTime.UtcNow - started).TotalMilliseconds, ct: ct);
            return result;
        }
        catch (Exception ex)
        {
            doc.LocalizationStatus = "failed";
            doc.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            await _processingLog.LogAsync("default", doc.SourceId, doc.Id, "metadata_localization", "failed",
                ex.Message, "LOCALIZATION_FAILED", ex.StackTrace,
                (int)(DateTime.UtcNow - started).TotalMilliseconds, ct);
            throw;
        }
    }

    private static LocalizationPayload ParseJson(string raw)
    {
        var clean = Regex.Replace(raw.Trim(), @"^```(?:json)?\s*|\s*```$", string.Empty, RegexOptions.IgnoreCase);
        var result = JsonSerializer.Deserialize<LocalizationPayload>(clean, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (result == null || string.IsNullOrWhiteSpace(result.TitleZh) || string.IsNullOrWhiteSpace(result.SummaryZh))
            throw new InvalidOperationException("L1 localization returned invalid JSON");
        return result;
    }

    private static IReadOnlyList<string> ExtractKeywords(string? text) =>
        Regex.Matches(text ?? string.Empty, @"[\p{L}\p{N}+#.-]{2,}").Select(m => m.Value).Distinct().Take(10).ToList();

    private static string ComputeGlossaryVersion(IEnumerable<Terminology> terms)
    {
        var value = string.Join('\n', terms.OrderBy(t => t.SourceTerm).ThenBy(t => t.TargetTerm)
            .Select(t => $"{t.SourceTerm}|{t.TargetTerm}|{t.Version}|{t.UpdatedAt:O}"));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant()[..16];
    }

    private sealed class LocalizationPayload
    {
        [System.Text.Json.Serialization.JsonPropertyName("title_zh")] public string TitleZh { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("summary_zh")] public string SummaryZh { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("keywords_zh")] public string[]? KeywordsZh { get; set; }
    }
}

public sealed class ChunkLocalizationService : IChunkLocalizationService
{
    private const string PromptVersion = "chunk-zh-v2";
    private readonly IAppDbContext _db;
    private readonly ILlmService _llm;
    private readonly ITerminologyService _terminology;
    private readonly ILocalizationQualityService _quality;
    private readonly IChineseNormalizationService _normalizer;
    private readonly IChineseFullTextIndexService _fullText;
    private readonly IProcessingLogService _processingLog;
    private readonly IMultiVectorEmbeddingService _multiVector;

    public ChunkLocalizationService(IAppDbContext db, ILlmService llm, ITerminologyService terminology,
        ILocalizationQualityService quality, IChineseNormalizationService normalizer,
        IChineseFullTextIndexService fullText, IProcessingLogService processingLog, IMultiVectorEmbeddingService multiVector)
    {
        _db = db; _llm = llm; _terminology = terminology; _quality = quality;
        _normalizer = normalizer; _fullText = fullText; _processingLog = processingLog; _multiVector = multiVector;
    }

    public async Task<ChunkLocalization> TranslateAsync(Guid userId, Guid chunkId, ChunkTranslationRequest request, CancellationToken ct = default)
    {
        var chunk = await _db.DocumentChunks.FirstOrDefaultAsync(c => c.Id == chunkId && c.UserId == userId, ct)
                    ?? throw new KeyNotFoundException($"Chunk {chunkId} was not found");
        var document = await _db.Documents.FirstAsync(d => d.Id == chunk.DocumentId && d.UserId == userId, ct);
        var language = string.IsNullOrWhiteSpace(request.LanguageCode) ? "zh-CN" : request.LanguageCode.Trim();
        var source = string.IsNullOrWhiteSpace(chunk.ContentOriginal) ? chunk.Content : chunk.ContentOriginal;
        var sourceHash = chunk.ContentHash ?? Hash(source);
        var glossary = await _terminology.SelectForContentAsync(
            userId, document.WorkspaceId, chunk.DetectedLanguage ?? document.PrimaryLanguage ?? document.Language,
            language, document.SourceDomain, $"{chunk.ChunkTitle}\n{source}", ct: ct);
        var glossaryVersion = GlossaryVersion(glossary);
        var idempotency = Hash($"{chunk.Id}|{sourceHash}|{language}|{PromptVersion}|{glossaryVersion}");

        var localization = await _db.ChunkLocalizations.FirstOrDefaultAsync(x => x.ChunkId == chunkId && x.LanguageCode == language, ct);
        if (localization != null && !request.Force && localization.IdempotencyKey == idempotency
            && localization.Status is "done" or "review_required" or "processing") return localization;

        var now = DateTime.UtcNow;
        if (localization == null)
        {
            localization = new ChunkLocalization
            {
                Id = Guid.NewGuid(), ChunkId = chunk.Id, UserId = userId, WorkspaceId = document.WorkspaceId, LanguageCode = language,
                ContentLocalized = string.Empty, TranslationType = request.TranslationType,
                PromptVersion = PromptVersion, SourceContentHash = sourceHash, IdempotencyKey = idempotency,
                Status = "processing", ReviewStatus = "unreviewed", CreatedAt = now, UpdatedAt = now
            };
            _db.ChunkLocalizations.Add(localization);
        }
        else
        {
            localization.Status = "processing"; localization.SourceContentHash = sourceHash;
            localization.IdempotencyKey = idempotency; localization.PromptVersion = PromptVersion;
            localization.GlossaryVersion = glossaryVersion; localization.UpdatedAt = now;
        }
        try { await _db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            var concurrent = await _db.ChunkLocalizations.AsNoTracking()
                .FirstOrDefaultAsync(x => x.ChunkId == chunkId && x.LanguageCode == language, ct);
            if (concurrent != null) return concurrent;
            throw;
        }

        var started = DateTime.UtcNow;
        await _processingLog.LogAsync("default", document.SourceId, document.Id, "chunk_localization", "started",
            $"chunk={chunk.Id}; language={language}; idempotency={idempotency}", ct: ct);
        try
        {
            string heading;
            string translated;
            string model;
            if ((chunk.DetectedLanguage ?? document.PrimaryLanguage)?.StartsWith("zh", StringComparison.OrdinalIgnoreCase) == true)
            {
                heading = _normalizer.Normalize(chunk.ChunkTitle);
                translated = _normalizer.Normalize(source);
                model = "local-normalizer";
                localization.TranslationType = "normalized";
            }
            else
            {
                var glossaryText = string.Join('\n', glossary.Select(TerminologyService.FormatPromptLine));
                var system = "你是知识库分块翻译器。忠实翻译为简体中文，只输出 JSON；保留数字、单位、日期、URL、代码和专有名词，不添加原文没有的事实。";
                var user = $$"""
                    输出格式：{"heading_zh":"","content_zh":""}
                    术语表优先级最高：
                    {{glossaryText}}

                    原文标题：{{chunk.ChunkTitle ?? chunk.HeadingPath}}
                    原文内容：
                    {{source}}
                    """;
                var response = await _llm.CompleteAsync(system, user, ct: ct);
                var payload = ParseTranslation(response.Content);
                heading = payload.HeadingZh;
                translated = payload.ContentZh;
                model = response.Model;
            }

            var quality = _quality.Validate(source, translated, glossary);
            localization.HeadingLocalized = heading;
            localization.ContentLocalized = translated;
            localization.Model = model;
            localization.GlossaryVersion = glossaryVersion;
            localization.QualityScore = quality.Score;
            localization.QualityIssues = JsonSerializer.Serialize(quality.Issues);
            localization.Status = quality.RequiresReview ? "review_required" : "done";
            localization.ReviewStatus = quality.RequiresReview ? "needs_review" : "unreviewed";
            localization.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            await _fullText.IndexDocumentAsync(document.Id, ct);
            await _multiVector.IndexChunkAsync(userId, chunk.Id, ct);
            await _processingLog.LogAsync("default", document.SourceId, document.Id, "chunk_localization", "success",
                $"chunk={chunk.Id}; quality={quality.Score}; status={localization.Status}; model={model}",
                durationMs: (int)(DateTime.UtcNow - started).TotalMilliseconds, ct: ct);
            return localization;
        }
        catch (Exception ex)
        {
            localization.Status = "failed"; localization.QualityIssues = JsonSerializer.Serialize(new[] { ex.Message });
            localization.UpdatedAt = DateTime.UtcNow; await _db.SaveChangesAsync(ct);
            await _processingLog.LogAsync("default", document.SourceId, document.Id, "chunk_localization", "failed",
                ex.Message, "CHUNK_LOCALIZATION_FAILED", ex.StackTrace, (int)(DateTime.UtcNow - started).TotalMilliseconds, ct);
            throw;
        }
    }

    public async Task<IReadOnlyList<ChunkLocalization>> ListAsync(Guid userId, Guid chunkId, CancellationToken ct = default) =>
        await _db.ChunkLocalizations.AsNoTracking().Where(x => x.UserId == userId && x.ChunkId == chunkId)
            .OrderByDescending(x => x.UpdatedAt).ToListAsync(ct);

    public async Task<ChunkLocalization> ReviewAsync(Guid userId, Guid chunkId, Guid localizationId, string headingLocalized,
        string contentLocalized, bool approved, CancellationToken ct = default)
    {
        var item = await _db.ChunkLocalizations.FirstOrDefaultAsync(
                       x => x.Id == localizationId && x.ChunkId == chunkId && x.UserId == userId, ct)
                   ?? throw new KeyNotFoundException($"Localization {localizationId} was not found");
        if (string.IsNullOrWhiteSpace(contentLocalized)) throw new ArgumentException("Localized content is required");
        item.HeadingLocalized = headingLocalized?.Trim(); item.ContentLocalized = contentLocalized.Trim();
        item.TranslationType = "human_reviewed"; item.ReviewStatus = approved ? "approved" : "rejected";
        item.Status = approved ? "done" : "review_required"; item.QualityScore = approved ? 100 : item.QualityScore;
        item.QualityIssues = approved ? "[]" : item.QualityIssues; item.ReviewedAt = DateTime.UtcNow; item.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        var documentId = await _db.DocumentChunks.Where(c => c.Id == item.ChunkId).Select(c => c.DocumentId).FirstAsync(ct);
        await _fullText.IndexDocumentAsync(documentId, ct);
        return item;
    }

    public async Task<ChunkBatchResult> TranslateDocumentAsync(Guid userId, Guid documentId, bool force = false,
        int maxChunks = 500, CancellationToken ct = default)
    {
        var exists = await _db.Documents.AsNoTracking().AnyAsync(d => d.Id == documentId && d.UserId == userId, ct);
        if (!exists) throw new KeyNotFoundException($"Document {documentId} was not found");
        var limit = Math.Clamp(maxChunks, 1, 2000);
        var totalAvailable = await _db.DocumentChunks.AsNoTracking()
            .CountAsync(c => c.DocumentId == documentId && c.UserId == userId, ct);
        var chunkIds = await _db.DocumentChunks.AsNoTracking().Where(c => c.DocumentId == documentId && c.UserId == userId)
            .OrderBy(c => c.ChunkIndex).Select(c => c.Id).Take(limit).ToListAsync(ct);
        var succeeded = 0;
        var errors = new List<string>();
        foreach (var id in chunkIds)
        {
            try
            {
                await TranslateAsync(userId, id, new ChunkTranslationRequest("zh-CN", force, "machine"), ct);
                succeeded++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                if (errors.Count < 20) errors.Add($"{id}: {ex.Message}");
            }
        }
        var trackedDocument = await _db.Documents.FirstAsync(d => d.Id == documentId && d.UserId == userId, ct);
        var fullyLocalized = totalAvailable > 0 && chunkIds.Count == totalAvailable && succeeded == chunkIds.Count;
        trackedDocument.LocalizationLevel = fullyLocalized ? "L3" : "L2";
        trackedDocument.LocalizationStatus = fullyLocalized ? "done" : "partial";
        trackedDocument.LocalizedAt = DateTime.UtcNow;
        trackedDocument.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return new ChunkBatchResult(chunkIds.Count, succeeded, chunkIds.Count - succeeded, errors);
    }

    private static TranslationPayload ParseTranslation(string raw)
    {
        var clean = Regex.Replace(raw.Trim(), @"^```(?:json)?\s*|\s*```$", string.Empty, RegexOptions.IgnoreCase);
        var result = JsonSerializer.Deserialize<TranslationPayload>(clean, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (result == null || string.IsNullOrWhiteSpace(result.ContentZh)) throw new InvalidOperationException("Chunk translation returned invalid JSON");
        return result;
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static string GlossaryVersion(IEnumerable<Terminology> terms) => Hash(string.Join('\n', terms.OrderBy(t => t.SourceTerm)
        .Select(t => $"{t.SourceTerm}|{t.TargetTerm}|{t.Version}|{t.UpdatedAt:O}")))[..16];

    private sealed class TranslationPayload
    {
        [System.Text.Json.Serialization.JsonPropertyName("heading_zh")] public string HeadingZh { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("content_zh")] public string ContentZh { get; set; } = string.Empty;
    }
}

public sealed class ChunkEnrichmentService : IChunkEnrichmentService
{
    private const string PromptVersion = "chunk-enrichment-zh-v1";
    private readonly IAppDbContext _db;
    private readonly ILlmService _llm;
    private readonly IChineseFullTextIndexService _fullText;
    private readonly IProcessingLogService _processingLog;
    private readonly IMultiVectorEmbeddingService _multiVector;

    public ChunkEnrichmentService(IAppDbContext db, ILlmService llm, IChineseFullTextIndexService fullText,
        IProcessingLogService processingLog, IMultiVectorEmbeddingService multiVector)
    {
        _db = db; _llm = llm; _fullText = fullText; _processingLog = processingLog; _multiVector = multiVector;
    }

    public async Task<ChunkEnrichment> EnrichAsync(Guid userId, Guid chunkId, bool force = false, CancellationToken ct = default)
    {
        var chunk = await _db.DocumentChunks.FirstOrDefaultAsync(c => c.Id == chunkId && c.UserId == userId, ct)
                    ?? throw new KeyNotFoundException($"Chunk {chunkId} was not found");
        var document = await _db.Documents.AsNoTracking().FirstAsync(d => d.Id == chunk.DocumentId && d.UserId == userId, ct);
        var localization = await _db.ChunkLocalizations.AsNoTracking()
            .FirstOrDefaultAsync(x => x.ChunkId == chunkId && x.LanguageCode == "zh-CN"
                && (x.Status == "done" || x.Status == "review_required"), ct);
        var source = !string.IsNullOrWhiteSpace(localization?.ContentLocalized)
            ? localization.ContentLocalized
            : (!string.IsNullOrWhiteSpace(chunk.ContentNormalized) ? chunk.ContentNormalized :
                (!string.IsNullOrWhiteSpace(chunk.ContentOriginal) ? chunk.ContentOriginal : chunk.Content));
        var sourceHash = Hash(source);
        var item = await _db.ChunkEnrichments.FirstOrDefaultAsync(
            x => x.ChunkId == chunkId && x.LanguageCode == "zh-CN", ct);
        if (item != null && !force && item.SourceContentHash == sourceHash
            && item.PromptVersion == PromptVersion && item.Status is "done" or "processing") return item;

        var now = DateTime.UtcNow;
        if (item == null)
        {
            item = new ChunkEnrichment
            {
                Id = Guid.NewGuid(), ChunkId = chunkId, UserId = userId, LanguageCode = "zh-CN",
                LocalizationId = localization?.Id, SourceContentHash = sourceHash, PromptVersion = PromptVersion,
                Status = "processing", CreatedAt = now, UpdatedAt = now
            };
            _db.ChunkEnrichments.Add(item);
        }
        else
        {
            item.LocalizationId = localization?.Id; item.SourceContentHash = sourceHash;
            item.PromptVersion = PromptVersion; item.Status = "processing"; item.UpdatedAt = now;
        }
        try { await _db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            var concurrent = await _db.ChunkEnrichments.AsNoTracking()
                .FirstOrDefaultAsync(x => x.ChunkId == chunkId && x.LanguageCode == "zh-CN", ct);
            if (concurrent != null) return concurrent;
            throw;
        }

        var started = DateTime.UtcNow;
        await _processingLog.LogAsync("default", document.SourceId, document.Id, "chunk_enrichment", "started",
            $"chunk={chunkId}; sourceHash={sourceHash}; prompt={PromptVersion}", ct: ct);
        try
        {
            var response = await _llm.CompleteAsync(
                "你是中文知识库结构化提炼器。仅依据输入内容输出严格 JSON，不补充外部事实；专有名词、数字、日期、单位必须忠实保留。",
                $$"""
                输出格式：
                {"summary":"不超过120字的中文摘要","keywords":["3至8个中文关键词"],"entities":["实体名称"],"facts":["可核验事实"],"hypothetical_questions":["3至5个可由本段回答的中文问题"]}

                标题：{{chunk.ChunkTitle ?? chunk.HeadingPath ?? document.TitleZh ?? document.Title}}
                内容：
                {{source}}
                """, ct: ct);
            var payload = ParsePayload(response.Content);
            item.Summary = payload.Summary.Trim();
            item.Keywords = JsonSerializer.Serialize(Clean(payload.Keywords, 8));
            item.Entities = JsonSerializer.Serialize(Clean(payload.Entities, 20));
            item.Facts = JsonSerializer.Serialize(Clean(payload.Facts, 12));
            item.HypotheticalQuestions = JsonSerializer.Serialize(Clean(payload.HypotheticalQuestions, 5));
            item.Model = response.Model; item.Status = "done"; item.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            await _fullText.IndexDocumentAsync(document.Id, ct);
            await _multiVector.IndexChunkAsync(userId, chunk.Id, ct);
            await _processingLog.LogAsync("default", document.SourceId, document.Id, "chunk_enrichment", "success",
                $"chunk={chunkId}; model={response.Model}; questions={payload.HypotheticalQuestions.Count}",
                durationMs: (int)(DateTime.UtcNow - started).TotalMilliseconds, ct: ct);
            return item;
        }
        catch (Exception ex)
        {
            item.Status = "failed"; item.UpdatedAt = DateTime.UtcNow; await _db.SaveChangesAsync(ct);
            await _processingLog.LogAsync("default", document.SourceId, document.Id, "chunk_enrichment", "failed",
                ex.Message, "CHUNK_ENRICHMENT_FAILED", ex.StackTrace,
                (int)(DateTime.UtcNow - started).TotalMilliseconds, ct);
            throw;
        }
    }

    public async Task<IReadOnlyList<ChunkEnrichment>> ListAsync(Guid userId, Guid chunkId, CancellationToken ct = default) =>
        await _db.ChunkEnrichments.AsNoTracking().Where(x => x.UserId == userId && x.ChunkId == chunkId)
            .OrderByDescending(x => x.UpdatedAt).ToListAsync(ct);

    public async Task<ChunkBatchResult> EnrichDocumentAsync(Guid userId, Guid documentId, bool force = false,
        int maxChunks = 500, CancellationToken ct = default)
    {
        var exists = await _db.Documents.AsNoTracking().AnyAsync(d => d.Id == documentId && d.UserId == userId, ct);
        if (!exists) throw new KeyNotFoundException($"Document {documentId} was not found");
        var ids = await _db.DocumentChunks.AsNoTracking().Where(c => c.DocumentId == documentId && c.UserId == userId)
            .OrderBy(c => c.ChunkIndex).Select(c => c.Id).Take(Math.Clamp(maxChunks, 1, 2000)).ToListAsync(ct);
        var succeeded = 0;
        var errors = new List<string>();
        foreach (var id in ids)
        {
            try { await EnrichAsync(userId, id, force, ct); succeeded++; }
            catch (Exception ex) when (ex is not OperationCanceledException)
            { if (errors.Count < 20) errors.Add($"{id}: {ex.Message}"); }
        }
        var trackedDocument = await _db.Documents.FirstAsync(d => d.Id == documentId && d.UserId == userId, ct);
        trackedDocument.EnrichmentStatus = succeeded == ids.Count && ids.Count > 0 ? "done" : "partial";
        trackedDocument.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return new ChunkBatchResult(ids.Count, succeeded, ids.Count - succeeded, errors);
    }

    private static EnrichmentPayload ParsePayload(string raw)
    {
        var clean = Regex.Replace(raw.Trim(), @"^```(?:json)?\s*|\s*```$", string.Empty, RegexOptions.IgnoreCase);
        var payload = JsonSerializer.Deserialize<EnrichmentPayload>(clean,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("Chunk enrichment returned invalid JSON");
        if (string.IsNullOrWhiteSpace(payload.Summary))
            throw new InvalidOperationException("Chunk enrichment did not return a summary");
        return payload;
    }

    private static List<string> Clean(IEnumerable<string>? values, int max) => (values ?? Array.Empty<string>())
        .Select(x => x?.Trim()).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase)
        .Take(max).Select(x => x!).ToList();
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private sealed class EnrichmentPayload
    {
        public string Summary { get; set; } = string.Empty;
        public List<string> Keywords { get; set; } = [];
        public List<string> Entities { get; set; } = [];
        public List<string> Facts { get; set; } = [];
        [System.Text.Json.Serialization.JsonPropertyName("hypothetical_questions")]
        public List<string> HypotheticalQuestions { get; set; } = [];
    }
}

public sealed class ChineseFullTextIndexService : IChineseFullTextIndexService
{
    private readonly AppDbContext _db;
    private readonly IChineseTokenizer _tokenizer;
    private readonly ITerminologyService _terminology;
    private readonly IWorkspaceService _workspaces;
    private readonly ILogger<ChineseFullTextIndexService> _logger;

    public ChineseFullTextIndexService(AppDbContext db, IChineseTokenizer tokenizer, ITerminologyService terminology,
        IWorkspaceService workspaces, ILogger<ChineseFullTextIndexService> logger)
    { _db = db; _tokenizer = tokenizer; _terminology = terminology; _workspaces = workspaces; _logger = logger; }

    public async Task EnsureCreatedAsync(CancellationToken ct = default)
    {
        if (!IsSqlite) return;
        await _db.Database.ExecuteSqlRawAsync("""
            CREATE VIRTUAL TABLE IF NOT EXISTS document_chunks_fts USING fts5(
                chunk_id UNINDEXED, document_id UNINDEXED, user_id UNINDEXED, search_text,
                tokenize='unicode61 remove_diacritics 2'
            )
            """, ct);
    }

    public async Task IndexDocumentAsync(Guid documentId, CancellationToken ct = default)
    {
        if (!IsSqlite) return;
        await EnsureCreatedAsync(ct);
        var doc = await _db.Documents.AsNoTracking().FirstOrDefaultAsync(d => d.Id == documentId, ct);
        if (doc == null) return;
        var chunks = await _db.DocumentChunks.AsNoTracking().Where(c => c.DocumentId == documentId).OrderBy(c => c.ChunkIndex).ToListAsync(ct);
        var chunkIds = chunks.Select(c => c.Id).ToList();
        var localizations = await _db.ChunkLocalizations.AsNoTracking()
            .Where(x => chunkIds.Contains(x.ChunkId) && (x.Status == "done" || x.Status == "review_required"))
            .ToDictionaryAsync(x => x.ChunkId, ct);
        var enrichments = await _db.ChunkEnrichments.AsNoTracking()
            .Where(x => chunkIds.Contains(x.ChunkId) && x.Status == "done")
            .ToDictionaryAsync(x => x.ChunkId, ct);
        var entities = await (from de in _db.DocumentEntities.AsNoTracking() join e in _db.Entities.AsNoTracking() on de.EntityId equals e.Id
                              where de.DocumentId == documentId select e.Name).ToListAsync(ct);
        var terms = await _terminology.SelectForContentAsync(
            doc.UserId, doc.WorkspaceId, doc.PrimaryLanguage ?? doc.Language, "zh-CN",
            doc.SourceDomain, $"{doc.Title}\n{doc.Summary}\n{doc.ContentText}", 50000, ct);
        var protectedTerms = terms.SelectMany(TerminologyService.Variants).ToList();

        var connection = _db.Database.GetDbConnection();
        var close = connection.State != ConnectionState.Open;
        if (close) await connection.OpenAsync(ct);
        try
        {
            await using var tx = await connection.BeginTransactionAsync(ct);
            await using (var delete = connection.CreateCommand())
            {
                delete.Transaction = tx; delete.CommandText = "DELETE FROM document_chunks_fts WHERE document_id = $documentId";
                Add(delete, "$documentId", documentId.ToString()); await delete.ExecuteNonQueryAsync(ct);
            }
            if (chunks.Count == 0)
                chunks.Add(new DocumentChunk { Id = Guid.Empty, DocumentId = doc.Id, Content = doc.ContentText ?? doc.Summary ?? string.Empty });
            foreach (var chunk in chunks)
            {
                localizations.TryGetValue(chunk.Id, out var localization);
                enrichments.TryGetValue(chunk.Id, out var enrichment);
                var raw = string.Join(' ', new[] { doc.Title, doc.TitleZh, doc.Summary, doc.SummaryZh, doc.KeywordsZh, chunk.ChunkTitle,
                    chunk.HeadingPath, chunk.ContentNormalized, localization?.HeadingLocalized, localization?.ContentLocalized,
                    enrichment?.Summary, enrichment?.Keywords, enrichment?.Entities, enrichment?.Facts,
                    enrichment?.HypotheticalQuestions, chunk.ContentOriginal, chunk.Content,
                    string.Join(' ', entities) }.Where(s => !string.IsNullOrWhiteSpace(s)));
                await using var insert = connection.CreateCommand();
                insert.Transaction = tx;
                insert.CommandText = "INSERT INTO document_chunks_fts(chunk_id, document_id, user_id, search_text) VALUES($chunkId,$documentId,$userId,$text)";
                Add(insert, "$chunkId", chunk.Id.ToString()); Add(insert, "$documentId", doc.Id.ToString()); Add(insert, "$userId", doc.UserId.ToString());
                Add(insert, "$text", _tokenizer.Tokenize(raw, protectedTerms)); await insert.ExecuteNonQueryAsync(ct);
            }
            await tx.CommitAsync(ct);
            var tracked = await _db.Documents.FirstOrDefaultAsync(d => d.Id == documentId, ct);
            if (tracked != null) { tracked.FulltextIndexStatus = "done"; await _db.SaveChangesAsync(ct); }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to update Chinese FTS5 for document {DocumentId}", documentId);
            throw;
        }
        finally { if (close) await connection.CloseAsync(); }
    }

    public async Task<IReadOnlyList<FullTextSearchHit>> SearchAsync(Guid userId, string query, int limit, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query)) return Array.Empty<FullTextSearchHit>();
        var workspaceId = (await _workspaces.GetCurrentWorkspaceAsync(userId, ct))?.Id;
        var expanded = await _terminology.ExpandQueryAsync(userId, workspaceId, query, targetLanguage: "zh-CN", ct: ct);
        var terms = _tokenizer.Tokenize(string.Join(' ', expanded)).Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(64).ToList();
        if (!IsSqlite)
            return await PortableSearchAsync(userId, workspaceId, expanded, terms, limit, ct);
        await EnsureCreatedAsync(ct);
        var match = string.Join(" OR ", terms.Select(t => $"\"{t.Replace("\"", "\"\"")}\""));
        if (match.Length == 0) return Array.Empty<FullTextSearchHit>();

        var hits = new List<FullTextSearchHit>();
        var connection = _db.Database.GetDbConnection(); var close = connection.State != ConnectionState.Open;
        if (close) await connection.OpenAsync(ct);
        try
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = workspaceId.HasValue
                ? """
                  SELECT document_chunks_fts.document_id, document_chunks_fts.chunk_id, bm25(document_chunks_fts)
                  FROM document_chunks_fts
                  JOIN documents d ON CAST(d.id AS TEXT) = document_chunks_fts.document_id
                  WHERE document_chunks_fts MATCH $match AND document_chunks_fts.user_id = $userId
                    AND CAST(d.workspace_id AS TEXT) = $workspaceId
                  ORDER BY bm25(document_chunks_fts) LIMIT $limit
                  """
                : "SELECT document_id, chunk_id, bm25(document_chunks_fts) FROM document_chunks_fts WHERE document_chunks_fts MATCH $match AND user_id = $userId ORDER BY bm25(document_chunks_fts) LIMIT $limit";
            Add(cmd, "$match", match); Add(cmd, "$userId", userId.ToString()); Add(cmd, "$limit", Math.Clamp(limit, 1, 100));
            if (workspaceId.HasValue) Add(cmd, "$workspaceId", workspaceId.Value.ToString());
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                if (Guid.TryParse(reader.GetString(0), out var docId) && Guid.TryParse(reader.GetString(1), out var chunkId))
                    hits.Add(new FullTextSearchHit(docId, chunkId, 1d / (1d + Math.Abs(reader.GetDouble(2))), "fts_zh"));
        }
        finally { if (close) await connection.CloseAsync(); }
        return hits;
    }

    private async Task<IReadOnlyList<FullTextSearchHit>> PortableSearchAsync(
        Guid userId, Guid? workspaceId, IReadOnlyList<string> expanded, IReadOnlyList<string> tokens,
        int limit, CancellationToken ct)
    {
        var documentQuery = _db.Documents.AsNoTracking().Where(d => d.UserId == userId);
        if (workspaceId.HasValue) documentQuery = documentQuery.Where(d => d.WorkspaceId == workspaceId);
        var documents = await documentQuery.OrderByDescending(d => d.UpdatedAt).Take(5000)
            .Select(d => new { d.Id, d.Title, d.TitleZh, d.Summary, d.SummaryZh, d.KeywordsZh, d.ContentText })
            .ToListAsync(ct);
        if (documents.Count == 0) return Array.Empty<FullTextSearchHit>();
        var docIds = documents.Select(d => d.Id).ToList();
        var chunks = await _db.DocumentChunks.AsNoTracking().Where(c => docIds.Contains(c.DocumentId))
            .OrderByDescending(c => c.CreatedAt).Take(20000)
            .Select(c => new { c.Id, c.DocumentId, c.ChunkTitle, c.Content, c.ContentOriginal, c.ContentNormalized })
            .ToListAsync(ct);
        var chunkIds = chunks.Select(c => c.Id).ToList();
        var localized = await _db.ChunkLocalizations.AsNoTracking()
            .Where(x => chunkIds.Contains(x.ChunkId) && (x.Status == "done" || x.Status == "review_required"))
            .Select(x => new { x.ChunkId, x.HeadingLocalized, x.ContentLocalized }).ToDictionaryAsync(x => x.ChunkId, ct);
        var documentMap = documents.ToDictionary(d => d.Id);
        var scored = new List<FullTextSearchHit>();
        foreach (var chunk in chunks)
        {
            documentMap.TryGetValue(chunk.DocumentId, out var doc);
            localized.TryGetValue(chunk.Id, out var translation);
            var text = string.Join(' ', new[]
            {
                doc?.Title, doc?.TitleZh, doc?.Summary, doc?.SummaryZh, doc?.KeywordsZh, doc?.ContentText,
                chunk.ChunkTitle, chunk.ContentNormalized, chunk.ContentOriginal, chunk.Content,
                translation?.HeadingLocalized, translation?.ContentLocalized
            }.Where(x => !string.IsNullOrWhiteSpace(x)));
            if (text.Length == 0) continue;
            var exact = expanded.Count(term => text.Contains(term, StringComparison.OrdinalIgnoreCase));
            var lexical = tokens.Count(token => text.Contains(token, StringComparison.OrdinalIgnoreCase));
            if (exact == 0 && lexical == 0) continue;
            var rank = Math.Min(1d, (exact * 3d + lexical) / Math.Max(4d, tokens.Count + 3d));
            scored.Add(new FullTextSearchHit(chunk.DocumentId, chunk.Id, rank, "fts_portable"));
        }
        return scored.OrderByDescending(x => x.Rank).Take(Math.Clamp(limit, 1, 100)).ToList();
    }

    private bool IsSqlite => _db.Database.ProviderName?.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) == true;
    private static void Add(System.Data.Common.DbCommand command, string name, object value)
    { var parameter = command.CreateParameter(); parameter.ParameterName = name; parameter.Value = value; command.Parameters.Add(parameter); }
}
