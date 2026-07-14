using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace KnowledgeEngine.Infrastructure.Processing;

public sealed class MultilingualBatchJobService : IMultilingualBatchJobService
{
    private readonly IAppDbContext _db;
    public MultilingualBatchJobService(IAppDbContext db) => _db = db;

    public async Task<MultilingualBatchJob> EnqueueAsync(Guid userId, Guid documentId, string jobType, bool force,
        int maxChunks, CancellationToken ct = default)
    {
        if (jobType is not ("translate" or "enrich" or "multi_vector")) throw new ArgumentException("Unsupported job type");
        if (!await _db.Documents.AsNoTracking().AnyAsync(x => x.Id == documentId && x.UserId == userId, ct))
            throw new KeyNotFoundException("Document not found");
        var active = await _db.MultilingualBatchJobs.FirstOrDefaultAsync(x => x.UserId == userId
            && x.DocumentId == documentId && x.JobType == jobType && (x.Status == "pending" || x.Status == "running" || x.Status == "paused"), ct);
        if (active != null) return active;
        var limit = Math.Clamp(maxChunks, 1, 2000);
        var total = await _db.DocumentChunks.CountAsync(x => x.DocumentId == documentId && x.UserId == userId, ct);
        var now = DateTime.UtcNow;
        var job = new MultilingualBatchJob
        {
            Id = Guid.NewGuid(), UserId = userId, DocumentId = documentId, JobType = jobType,
            Status = "pending", Force = force, MaxChunks = limit, TotalItems = Math.Min(total, limit),
            CreatedAt = now, UpdatedAt = now
        };
        _db.MultilingualBatchJobs.Add(job); await _db.SaveChangesAsync(ct); return job;
    }

    public Task<MultilingualBatchJob?> GetAsync(Guid userId, Guid jobId, CancellationToken ct = default) =>
        _db.MultilingualBatchJobs.AsNoTracking().FirstOrDefaultAsync(x => x.Id == jobId && x.UserId == userId, ct);
    public async Task<IReadOnlyList<MultilingualBatchJob>> ListAsync(Guid userId, Guid? documentId = null, CancellationToken ct = default)
    {
        var query = _db.MultilingualBatchJobs.AsNoTracking().Where(x => x.UserId == userId);
        if (documentId.HasValue) query = query.Where(x => x.DocumentId == documentId);
        return await query.OrderByDescending(x => x.CreatedAt).Take(50).ToListAsync(ct);
    }
    public Task<MultilingualBatchJob> PauseAsync(Guid userId, Guid jobId, CancellationToken ct = default) => SetStatus(userId, jobId, "paused", ct);
    public Task<MultilingualBatchJob> ResumeAsync(Guid userId, Guid jobId, CancellationToken ct = default) => SetStatus(userId, jobId, "pending", ct);
    public async Task<MultilingualBatchJob> RetryAsync(Guid userId, Guid jobId, CancellationToken ct = default)
    {
        var job = await Find(userId, jobId, ct);
        if (job.Status == "running") throw new ArgumentException("Pause the job before retrying");
        job.Status = "pending"; job.ProcessedItems = 0;
        job.SucceededItems = 0; job.FailedItems = 0; job.CurrentChunkId = null; job.ErrorMessage = null;
        job.FinishedAt = null; job.RetryCount++; job.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct); return job;
    }
    private async Task<MultilingualBatchJob> SetStatus(Guid userId, Guid id, string status, CancellationToken ct)
    { var job = await Find(userId, id, ct); job.Status = status; job.UpdatedAt = DateTime.UtcNow; await _db.SaveChangesAsync(ct); return job; }
    private async Task<MultilingualBatchJob> Find(Guid userId, Guid id, CancellationToken ct) =>
        await _db.MultilingualBatchJobs.FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId, ct)
        ?? throw new KeyNotFoundException("Batch job not found");
}

public sealed class MultilingualBatchWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<MultilingualBatchWorker> _logger;
    public MultilingualBatchWorker(IServiceScopeFactory scopes, ILogger<MultilingualBatchWorker> logger)
    { _scopes = scopes; _logger = logger; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await ProcessNext(stoppingToken); }
            catch (Exception ex) { _logger.LogError(ex, "Multilingual batch worker cycle failed"); }
            await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
        }
    }

    private async Task ProcessNext(CancellationToken ct)
    {
        Guid jobId;
        using (var scope = _scopes.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
            var job = await db.MultilingualBatchJobs.Where(x => x.Status == "pending").OrderBy(x => x.CreatedAt).FirstOrDefaultAsync(ct);
            if (job == null) return;
            job.Status = "running"; job.StartedAt ??= DateTime.UtcNow; job.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct); jobId = job.Id;
        }

        while (!ct.IsCancellationRequested)
        {
            using var scope = _scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
            var job = await db.MultilingualBatchJobs.FirstAsync(x => x.Id == jobId, ct);
            if (job.Status == "paused") return;
            if (job.Status != "running") return;
            var chunkId = await db.DocumentChunks.AsNoTracking().Where(x => x.DocumentId == job.DocumentId && x.UserId == job.UserId)
                .OrderBy(x => x.ChunkIndex).Skip(job.ProcessedItems).Select(x => (Guid?)x.Id).FirstOrDefaultAsync(ct);
            if (!chunkId.HasValue || job.ProcessedItems >= job.MaxChunks)
            {
                job.Status = "done"; job.CurrentChunkId = null; job.FinishedAt = DateTime.UtcNow; job.UpdatedAt = DateTime.UtcNow;
                var document = await db.Documents.FirstOrDefaultAsync(x => x.Id == job.DocumentId, ct);
                if (document != null)
                {
                    if (job.JobType == "translate")
                    {
                        document.LocalizationLevel = job.ProcessedItems >= job.TotalItems && job.FailedItems == 0 ? "L3" : "L2";
                        document.LocalizationStatus = job.FailedItems == 0 ? "done" : "partial";
                        document.LocalizedAt = DateTime.UtcNow;
                    }
                    else if (job.JobType == "enrich") document.EnrichmentStatus = job.FailedItems == 0 ? "done" : "partial";
                    document.UpdatedAt = DateTime.UtcNow;
                }
                await db.SaveChangesAsync(ct); return;
            }
            job.CurrentChunkId = chunkId; job.UpdatedAt = DateTime.UtcNow; await db.SaveChangesAsync(ct);
            try
            {
                if (job.JobType == "translate")
                    await scope.ServiceProvider.GetRequiredService<IChunkLocalizationService>()
                        .TranslateAsync(job.UserId, chunkId.Value, new ChunkTranslationRequest("zh-CN", job.Force), ct);
                else if (job.JobType == "enrich")
                    await scope.ServiceProvider.GetRequiredService<IChunkEnrichmentService>().EnrichAsync(job.UserId, chunkId.Value, job.Force, ct);
                else
                    await scope.ServiceProvider.GetRequiredService<IMultiVectorEmbeddingService>().IndexChunkAsync(job.UserId, chunkId.Value, ct);
                job.SucceededItems++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            { job.FailedItems++; job.ErrorMessage = ex.Message; }
            job.ProcessedItems++; job.CurrentChunkId = null; job.UpdatedAt = DateTime.UtcNow; await db.SaveChangesAsync(ct);
        }
    }
}
