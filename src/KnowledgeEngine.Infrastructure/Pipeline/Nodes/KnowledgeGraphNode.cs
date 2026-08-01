using KnowledgeEngine.Application.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KnowledgeEngine.Infrastructure.Pipeline.Nodes;

/// <summary>
/// Knowledge graph construction pipeline node.
/// <para>
/// NodeId = "knowledge_graph", DependsOn = ["entity"].
/// </para>
/// <para>
/// Reads the entities extracted by the <see cref="EntityNode"/> from the
/// shared pipeline context and constructs or updates the knowledge graph
/// for the document. Entity relations are inferred from co-occurrence in
/// the same transcript and persisted via <see cref="IAppDbContext"/>.
/// </para>
/// </summary>
public class KnowledgeGraphNode : IPipelineNode
{
    /// <inheritdoc/>
    public string NodeId => "knowledge_graph";

    /// <inheritdoc/>
    public string DisplayName => "Knowledge Graph Construction";

    /// <inheritdoc/>
    public List<string> DependsOn => new() { "entity" };

    private readonly IAppDbContext _db;
    private readonly ILogger<KnowledgeGraphNode> _logger;

    public KnowledgeGraphNode(
        IAppDbContext db,
        ILogger<KnowledgeGraphNode> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <inheritdoc/>
    public Task<bool> CanExecuteAsync(PipelineContext context, CancellationToken ct)
    {
        // Only execute if the entity node produced results.
        return Task.FromResult(
            context.Results.TryGetValue("entity", out var entityResult)
            && entityResult is NodeExecutionResult ner
            && ner.Success
            && ner.OutputData.TryGetValue("entities", out var entitiesObj)
            && entitiesObj is List<Dictionary<string, object>> entities
            && entities.Count > 0);
    }

    /// <inheritdoc/>
    public async Task<NodeExecutionResult> ExecuteAsync(PipelineContext context, CancellationToken ct)
    {
        try
        {
            // Extract entity list from the entity node's output.
            if (!context.Results.TryGetValue("entity", out var entityResult)
                || entityResult is not NodeExecutionResult ner
                || !ner.Success
                || !ner.OutputData.TryGetValue("entities", out var entitiesObj)
                || entitiesObj is not List<Dictionary<string, object>> entities
                || entities.Count == 0)
            {
                return NodeExecutionResult.Fail("No entities available from entity node.");
            }

            _logger.LogInformation(
                "Job {JobId}: knowledge graph node processing {EntityCount} entities",
                context.JobId, entities.Count);

            var userId = context.UserId;
            var workspaceId = context.WorkspaceId?.ToString() ?? string.Empty;
            var createdEntities = 0;
            var createdRelations = 0;

            // Find or create entities in the database.
            var entityIds = new List<(string Name, string Type, Guid Id)>();

            foreach (var entity in entities)
            {
                var name = entity.TryGetValue("name", out var n) ? n?.ToString() ?? string.Empty : string.Empty;
                var type = entity.TryGetValue("type", out var t) ? t?.ToString() ?? "other" : "other";

                if (string.IsNullOrWhiteSpace(name))
                    continue;

                // Check if entity already exists for this user.
                var existingEntity = await _db.Entities
                    .FirstOrDefaultAsync(e => e.UserId == userId && e.Name == name, ct);

                if (existingEntity != null)
                {
                    entityIds.Add((name, type, existingEntity.Id));
                }
                else
                {
                    var newEntity = new Domain.Entities.Entity
                    {
                        Id = Guid.NewGuid(),
                        UserId = userId,
                        WorkspaceId = workspaceId,
                        Name = name,
                        EntityType = type,
                        Status = "active",
                        Source = "audio_pipeline",
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    };
                    _db.Entities.Add(newEntity);
                    entityIds.Add((name, type, newEntity.Id));
                    createdEntities++;
                }
            }

            // Create co-occurrence relations between entities mentioned in the same transcript.
            for (var i = 0; i < entityIds.Count; i++)
            {
                for (var j = i + 1; j < entityIds.Count; j++)
                {
                    var (name1, type1, id1) = entityIds[i];
                    var (name2, type2, id2) = entityIds[j];

                    // Check if relation already exists.
                    var existingRelation = await _db.EntityRelations
                        .FirstOrDefaultAsync(r =>
                            (r.SourceEntityId == id1 && r.TargetEntityId == id2)
                            || (r.SourceEntityId == id2 && r.TargetEntityId == id1),
                            ct);

                    if (existingRelation == null)
                    {
                        _db.EntityRelations.Add(new Domain.Entities.EntityRelation
                        {
                            Id = Guid.NewGuid(),
                            UserId = userId,
                            SourceEntityId = id1,
                            TargetEntityId = id2,
                            RelationType = "co_occurrence",
                            Confidence = 0.5m,
                            CreatedAt = DateTime.UtcNow
                        });
                        createdRelations++;
                    }
                }
            }

            await _db.SaveChangesAsync(ct);

            _logger.LogInformation(
                "Job {JobId}: knowledge graph node created {CreatedEntities} entities and {CreatedRelations} relations",
                context.JobId, createdEntities, createdRelations);

            return NodeExecutionResult.Ok(
                new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                {
                    ["createdEntities"] = createdEntities,
                    ["createdRelations"] = createdRelations,
                    ["totalEntities"] = entityIds.Count,
                });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Job {JobId}: knowledge graph node failed", context.JobId);
            return NodeExecutionResult.Fail(ex.Message);
        }
    }
}
