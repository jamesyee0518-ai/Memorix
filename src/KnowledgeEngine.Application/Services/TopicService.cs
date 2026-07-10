using KnowledgeEngine.Application.DTOs;
using KnowledgeEngine.Application.Exceptions;
using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Application.Mapping;
using KnowledgeEngine.Application.Validators;
using KnowledgeEngine.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KnowledgeEngine.Application.Services;

public class TopicService
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUserContext _currentUser;
    private readonly ILogger<TopicService> _logger;
    private readonly IKnowledgeRepository _repository;

    public TopicService(
        IAppDbContext db,
        ICurrentUserContext currentUser,
        ILogger<TopicService> logger,
        IKnowledgeRepository repository)
    {
        _db = db;
        _currentUser = currentUser;
        _logger = logger;
        _repository = repository;
    }

    public async Task<ApiResponse<TopicResponse>> CreateAsync(CreateTopicRequest request, CancellationToken ct = default)
    {
        var userId = RequireUserId();

        var validator = new CreateTopicValidator();
        var validationResult = await validator.ValidateAsync(request, ct);
        if (!validationResult.IsValid)
        {
            throw new ValidationException(validationResult.ToDictionary());
        }

        var now = DateTime.UtcNow;
        var topic = new Topic
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Name = request.Name.Trim(),
            Description = request.Description,
            Domain = request.Domain,
            Visibility = string.IsNullOrEmpty(request.Visibility) ? "private" : request.Visibility,
            Status = "active",
            CreatedAt = now,
            UpdatedAt = now
        };

        // Use Repository abstraction for data access (§5.2: business code goes through Repository)
        await _repository.CreateTopicAsync(new CreateTopicInput
        {
            WorkspaceId = userId.ToString(),
            Name = topic.Name,
            Description = topic.Description,
            Domain = topic.Domain
        }, ct);

        // Also save to cloud DB for backward compatibility (cloud mode)
        _db.Topics.Add(topic);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Topic created: {TopicId} by {UserId}", topic.Id, userId);
        return ApiResponse<TopicResponse>.Ok(Mapper.ToTopicResponse(topic));
    }

    public async Task<ApiResponse<PagedResult<TopicListItem>>> GetAllAsync(int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        var userId = RequireUserId();

        if (page < 1) page = 1;
        if (pageSize < 1 || pageSize > 100) pageSize = 20;

        var query = _db.Topics.Where(t => t.UserId == userId && t.Status != "deleted");

        var total = await query.CountAsync(ct);
        var topics = await query
            .OrderByDescending(t => t.UpdatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var topicIds = topics.Select(t => t.Id).ToList();
        var sourceStats = await _db.Sources
            .Where(s => s.UserId == userId && topicIds.Contains(s.TopicId!.Value))
            .GroupBy(s => s.TopicId!.Value)
            .Select(g => new
            {
                TopicId = g.Key,
                DocumentCount = g.Count(),
                PendingCount = g.Count(s => s.Status == "pending" || s.Status == "queued" || s.Status == "fetching" || s.Status == "parsing" || s.Status == "cleaning" || s.Status == "ai_processing" || s.Status == "indexing"),
                FailedCount = g.Count(s => s.Status == "failed")
            })
            .ToDictionaryAsync(x => x.TopicId, ct);

        var items = topics.Select(t =>
        {
            var stats = sourceStats.GetValueOrDefault(t.Id);
            return Mapper.ToTopicListItem(t, stats?.DocumentCount ?? 0, stats?.PendingCount ?? 0, stats?.FailedCount ?? 0);
        }).ToList();

        var result = new PagedResult<TopicListItem>
        {
            Items = items,
            Total = total,
            Page = page,
            PageSize = pageSize
        };

        return ApiResponse<PagedResult<TopicListItem>>.Ok(result);
    }

    public async Task<ApiResponse<TopicDetail>> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var userId = RequireUserId();

        var topic = await _db.Topics.FirstOrDefaultAsync(t => t.Id == id && t.UserId == userId && t.Status != "deleted", ct);
        if (topic == null)
        {
            throw new NotFoundException("Topic", id);
        }

        var sources = _db.Sources.Where(s => s.UserId == userId && s.TopicId == id);
        var stats = new TopicStats
        {
            TotalCount = await sources.CountAsync(ct),
            DocumentCount = await sources.CountAsync(ct),
            PendingCount = await sources.CountAsync(s => s.Status == "pending" || s.Status == "queued" || s.Status == "fetching" || s.Status == "parsing" || s.Status == "cleaning" || s.Status == "ai_processing" || s.Status == "indexing"),
            FailedCount = await sources.CountAsync(s => s.Status == "failed"),
            DoneCount = await sources.CountAsync(s => s.Status == "done")
        };

        return ApiResponse<TopicDetail>.Ok(Mapper.ToTopicDetail(topic, stats));
    }

    public async Task<ApiResponse<TopicResponse>> UpdateAsync(Guid id, UpdateTopicRequest request, CancellationToken ct = default)
    {
        var userId = RequireUserId();

        var validator = new UpdateTopicValidator();
        var validationResult = await validator.ValidateAsync(request, ct);
        if (!validationResult.IsValid)
        {
            throw new ValidationException(validationResult.ToDictionary());
        }

        var topic = await _db.Topics.FirstOrDefaultAsync(t => t.Id == id && t.UserId == userId && t.Status != "deleted", ct);
        if (topic == null)
        {
            throw new NotFoundException("Topic", id);
        }

        if (request.Name != null) topic.Name = request.Name.Trim();
        if (request.Description != null) topic.Description = request.Description;
        if (request.Domain != null) topic.Domain = request.Domain;
        if (request.Visibility != null) topic.Visibility = request.Visibility;
        topic.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Topic updated: {TopicId} by {UserId}", topic.Id, userId);
        return ApiResponse<TopicResponse>.Ok(Mapper.ToTopicResponse(topic));
    }

    public async Task<ApiResponse<object>> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var userId = RequireUserId();

        var topic = await _db.Topics.FirstOrDefaultAsync(t => t.Id == id && t.UserId == userId && t.Status != "deleted", ct);
        if (topic == null)
        {
            throw new NotFoundException("Topic", id);
        }

        topic.Status = "deleted";
        topic.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Topic deleted: {TopicId} by {UserId}", topic.Id, userId);
        return ApiResponse<object>.Ok(new { id = topic.Id, status = "deleted" });
    }

    private Guid RequireUserId()
    {
        if (!_currentUser.IsAuthenticated || _currentUser.UserId == null)
        {
            throw new UnauthorizedException("User is not authenticated");
        }
        return _currentUser.UserId.Value;
    }
}
