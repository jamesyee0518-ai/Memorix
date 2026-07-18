using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Application.Settings;
using KnowledgeEngine.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace KnowledgeEngine.Infrastructure.Processing;

public sealed class MultiVectorEmbeddingService : IMultiVectorEmbeddingService
{
    private readonly IAppDbContext _db;
    private readonly IEmbeddingService _embedding;
    private readonly EmbeddingSettings _settings;

    public MultiVectorEmbeddingService(IAppDbContext db, IEmbeddingService embedding, IOptions<EmbeddingSettings> settings)
    { _db = db; _embedding = embedding; _settings = settings.Value; }

    public async Task<int> IndexChunkAsync(Guid userId, Guid chunkId, CancellationToken ct = default)
    {
        var chunk = await _db.DocumentChunks.FirstOrDefaultAsync(x => x.Id == chunkId && x.UserId == userId, ct)
                    ?? throw new KeyNotFoundException($"Chunk {chunkId} was not found");
        var document = await _db.Documents.AsNoTracking().FirstAsync(x => x.Id == chunk.DocumentId, ct);
        var localization = await _db.ChunkLocalizations.AsNoTracking().FirstOrDefaultAsync(x => x.ChunkId == chunkId
            && (x.Status == "done" || x.Status == "review_required"), ct);
        var enrichment = await _db.ChunkEnrichments.AsNoTracking().FirstOrDefaultAsync(x => x.ChunkId == chunkId && x.Status == "done", ct);
        var inputs = new List<(string Type, string Language, string Text, Guid? LocalizationId)>
        {
            ("original", chunk.DetectedLanguage ?? document.PrimaryLanguage ?? "und",
                $"{document.Title}\n{chunk.HeadingPath}\n{chunk.ContentOriginal.DefaultIfBlank(chunk.Content)}", null)
        };
        if (!string.IsNullOrWhiteSpace(localization?.ContentLocalized))
            inputs.Add(("localized", "zh-CN", $"{localization.HeadingLocalized}\n{localization.ContentLocalized}", localization.Id));
        if (!string.IsNullOrWhiteSpace(enrichment?.Summary))
            inputs.Add(("summary", "zh-CN", enrichment.Summary, localization?.Id));
        var questions = ParseList(enrichment?.HypotheticalQuestions);
        if (questions.Count > 0)
            inputs.Add(("hypothetical_question", "zh-CN", string.Join('\n', questions), localization?.Id));

        var saved = 0;
        foreach (var input in inputs)
        {
            var hash = Hash(input.Text);
            var storedModel = $"{_settings.Model}::{input.Type}";
            var existing = await _db.ChunkEmbeddings.FirstOrDefaultAsync(x => x.ChunkId == chunkId
                && x.LanguageCode == input.Language && x.EmbeddingType == input.Type
                && x.Provider == Provider() && x.Model == storedModel, ct);
            if (existing?.SourceContentHash == hash && existing.Status == "done") continue;
            var vector = await _embedding.EmbedAsync(input.Text, ct);
            var now = DateTime.UtcNow;
            if (existing == null)
            {
                existing = new ChunkEmbedding { Id = Guid.NewGuid(), ChunkId = chunkId, CreatedAt = now };
                _db.ChunkEmbeddings.Add(existing);
            }
            existing.WorkspaceId = "default"; existing.Provider = Provider(); existing.Model = storedModel;
            existing.Dimension = vector.Length; existing.EmbeddingJson = JsonSerializer.Serialize(vector);
            existing.LanguageCode = input.Language; existing.EmbeddingType = input.Type;
            existing.LocalizationId = input.LocalizationId; existing.SourceContentHash = hash;
            existing.ChunkContentHash = chunk.ContentHash; existing.Status = "done"; existing.ErrorMessage = null;
            existing.UpdatedAt = now; await _db.SaveChangesAsync(ct); saved++;
        }
        return saved;
    }

    private string Provider() => _settings.Endpoint?.Contains("localhost", StringComparison.OrdinalIgnoreCase) == true ? "local" : "openai";
    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    private static List<string> ParseList(string? json)
    { try { return JsonSerializer.Deserialize<List<string>>(json ?? "[]") ?? []; } catch { return []; } }
}

internal static class MultiVectorStringExtensions
{
    public static string DefaultIfBlank(this string? value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value;
}
