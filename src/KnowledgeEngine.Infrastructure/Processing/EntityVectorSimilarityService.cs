using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Application.Settings;
using KnowledgeEngine.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KnowledgeEngine.Infrastructure.Processing;

public sealed class EntityVectorSimilarityService(
    IAppDbContext db,
    IEmbeddingService embeddingService,
    IOptions<EmbeddingSettings> embeddingOptions,
    ILogger<EntityVectorSimilarityService> logger) : IEntityVectorSimilarityService
{
    private readonly EmbeddingSettings _settings = embeddingOptions.Value;

    public async Task<IReadOnlyDictionary<Guid, EntityVectorScores>> ScoreAsync(
        string workspaceId,
        string queryName,
        string? queryContext,
        IReadOnlyCollection<Entity> candidates,
        CancellationToken ct = default)
    {
        if (candidates.Count == 0 || string.IsNullOrWhiteSpace(queryName)) return
            new Dictionary<Guid, EntityVectorScores>();

        var provider = ResolveProvider(_settings.Endpoint);
        var model = string.IsNullOrWhiteSpace(_settings.Model)
            ? "unknown"
            : _settings.Model.Trim();
        var candidateIds = candidates.Select(x => x.Id).ToList();
        var existing = await db.EntityEmbeddings
            .Where(x => candidateIds.Contains(x.EntityId)
                && x.WorkspaceId == workspaceId
                && x.Provider == provider
                && x.Model == model)
            .ToListAsync(ct);
        var rows = existing.ToDictionary(x => (x.EntityId, x.EmbeddingType));
        var targets = BuildTargets(candidates);
        var staleTargets = targets
            .Where(x => !rows.TryGetValue((x.EntityId, x.Type), out var row)
                || row.Status != "done"
                || row.ContentHash != x.ContentHash
                || string.IsNullOrWhiteSpace(row.EmbeddingJson))
            .ToList();

        var inputs = new List<string> { queryName.Trim() };
        var contextIndex = -1;
        if (!string.IsNullOrWhiteSpace(queryContext))
        {
            contextIndex = inputs.Count;
            inputs.Add(queryContext.Trim());
        }
        var targetStart = inputs.Count;
        inputs.AddRange(staleTargets.Select(x => x.Text));

        List<float[]> vectors;
        try
        {
            vectors = await embeddingService.EmbedBatchAsync(inputs, ct);
            if (vectors.Count != inputs.Count || vectors.Any(x => x.Length == 0))
                throw new InvalidOperationException(
                    $"Embedding provider returned {vectors.Count} vectors for {inputs.Count} inputs.");

            var now = DateTime.UtcNow;
            for (var i = 0; i < staleTargets.Count; i++)
            {
                var target = staleTargets[i];
                var vector = vectors[targetStart + i];
                if (!rows.TryGetValue((target.EntityId, target.Type), out var row))
                {
                    row = new EntityEmbedding
                    {
                        Id = Guid.NewGuid(),
                        EntityId = target.EntityId,
                        WorkspaceId = workspaceId,
                        Provider = provider,
                        Model = model,
                        EmbeddingType = target.Type,
                        CreatedAt = now
                    };
                    db.EntityEmbeddings.Add(row);
                    rows[(target.EntityId, target.Type)] = row;
                }

                row.Dimension = vector.Length;
                row.EmbeddingJson = JsonSerializer.Serialize(vector);
                row.ContentHash = target.ContentHash;
                row.Status = "done";
                row.ErrorMessage = null;
                row.UpdatedAt = now;
            }
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Entity vector scoring degraded to deterministic channels for workspace {WorkspaceId}",
                workspaceId);
            await RecordFailuresAsync(
                workspaceId, provider, model, staleTargets, rows, ex.Message, ct);
            return new Dictionary<Guid, EntityVectorScores>();
        }

        var queryNameVector = vectors[0];
        var queryContextVector = contextIndex >= 0 ? vectors[contextIndex] : null;
        var result = new Dictionary<Guid, EntityVectorScores>();
        foreach (var candidate in candidates)
        {
            var nameVector = ReadVector(rows.GetValueOrDefault((candidate.Id, "name")));
            var descriptionVector = ReadVector(rows.GetValueOrDefault((candidate.Id, "description")));
            result[candidate.Id] = new EntityVectorScores
            {
                NameScore = Similarity(queryNameVector, nameVector),
                DescriptionScore = queryContextVector == null
                    ? 0m
                    : Similarity(queryContextVector, descriptionVector)
            };
        }
        return result;
    }

    private async Task RecordFailuresAsync(
        string workspaceId,
        string provider,
        string model,
        IReadOnlyCollection<VectorTarget> targets,
        IDictionary<(Guid EntityId, string Type), EntityEmbedding> rows,
        string error,
        CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        foreach (var target in targets)
        {
            if (!rows.TryGetValue((target.EntityId, target.Type), out var row))
            {
                row = new EntityEmbedding
                {
                    Id = Guid.NewGuid(),
                    EntityId = target.EntityId,
                    WorkspaceId = workspaceId,
                    Provider = provider,
                    Model = model,
                    EmbeddingType = target.Type,
                    ContentHash = target.ContentHash,
                    CreatedAt = now
                };
                db.EntityEmbeddings.Add(row);
                rows[(target.EntityId, target.Type)] = row;
            }
            row.Status = "failed";
            row.ErrorMessage = Truncate(error, 2000);
            row.RetryCount++;
            row.UpdatedAt = now;
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (Exception saveError)
        {
            logger.LogWarning(saveError, "Could not persist entity embedding diagnostics");
        }
    }

    private static List<VectorTarget> BuildTargets(IEnumerable<Entity> candidates)
    {
        var targets = new List<VectorTarget>();
        foreach (var entity in candidates)
        {
            var name = string.Join(" ", new[]
            {
                entity.CanonicalName, entity.PreferredNameZh,
                entity.PreferredNameEn, entity.Abbreviation, entity.Name
            }.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase));
            targets.Add(new VectorTarget(entity.Id, "name", name, Hash(name)));
            if (!string.IsNullOrWhiteSpace(entity.Description))
            {
                var description = entity.Description.Trim();
                targets.Add(new VectorTarget(
                    entity.Id, "description", description, Hash(description)));
            }
        }
        return targets;
    }

    private static float[]? ReadVector(EntityEmbedding? row)
    {
        if (row?.Status != "done" || string.IsNullOrWhiteSpace(row.EmbeddingJson)) return null;
        try
        {
            return JsonSerializer.Deserialize<float[]>(row.EmbeddingJson);
        }
        catch
        {
            return null;
        }
    }

    internal static decimal Similarity(float[]? left, float[]? right)
    {
        if (left == null || right == null || left.Length == 0 || left.Length != right.Length)
            return 0m;
        double dot = 0, leftNorm = 0, rightNorm = 0;
        for (var i = 0; i < left.Length; i++)
        {
            dot += left[i] * right[i];
            leftNorm += left[i] * left[i];
            rightNorm += right[i] * right[i];
        }
        if (leftNorm <= 0 || rightNorm <= 0) return 0m;
        var cosine = dot / (Math.Sqrt(leftNorm) * Math.Sqrt(rightNorm));
        return Math.Clamp((decimal)cosine, 0m, 1m);
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static string ResolveProvider(string endpoint)
    {
        if (Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)) return uri.Host;
        return "configured";
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];

    private sealed record VectorTarget(
        Guid EntityId, string Type, string Text, string ContentHash);
}
