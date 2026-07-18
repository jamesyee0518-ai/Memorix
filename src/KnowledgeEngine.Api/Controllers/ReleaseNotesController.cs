using System.Text.Json;
using KnowledgeEngine.Application.DTOs;
using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using KnowledgeEngine.Application.Security;

namespace KnowledgeEngine.Api.Controllers;

[Authorize]
[Route("api/release-notes")]
public class ReleaseNotesController : BaseController
{
    private readonly IAppDbContext _db;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public ReleaseNotesController(IAppDbContext db)
    {
        _db = db;
    }

    // ===== GET /api/release-notes — 获取已发布版本列表（按发布时间倒序）=====
    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var notes = await _db.ReleaseNotes
            .Where(r => r.IsPublished)
            .OrderByDescending(r => r.PublishedAt)
            .ToListAsync(ct);

        var items = notes.Select(MapToListItem).ToList();
        return Ok(ApiResponse<List<ReleaseNoteListItem>>.Ok(items, GetTraceId()));
    }

    // ===== GET /api/release-notes/{id} — 获取详情 =====
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById([FromRoute] Guid id, CancellationToken ct)
    {
        var canManage = User.IsInRole(PlatformRoles.PlatformAdmin) || User.IsInRole(PlatformRoles.Operator);
        var note = await _db.ReleaseNotes.FirstOrDefaultAsync(
            r => r.Id == id && (r.IsPublished || canManage), ct);
        if (note == null)
        {
            return Ok(ApiResponse<ReleaseNoteResponse>.Fail("not_found", "Release note not found", GetTraceId()));
        }

        return Ok(ApiResponse<ReleaseNoteResponse>.Ok(MapToResponse(note), GetTraceId()));
    }

    // ===== POST /api/release-notes — 创建（管理端）=====
    [HttpPost]
    [Authorize(Policy = AuthorizationPolicies.PlatformOperator)]
    public async Task<IActionResult> Create([FromBody] CreateReleaseNoteRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Version))
        {
            return Ok(ApiResponse<ReleaseNoteResponse>.Fail("validation_error", "Version is required", GetTraceId()));
        }
        if (string.IsNullOrWhiteSpace(request.Title))
        {
            return Ok(ApiResponse<ReleaseNoteResponse>.Fail("validation_error", "Title is required", GetTraceId()));
        }

        var now = DateTime.UtcNow;
        var note = new ReleaseNote
        {
            Id = Guid.NewGuid(),
            Version = request.Version,
            Title = request.Title,
            Channel = string.IsNullOrWhiteSpace(request.Channel) ? "alpha" : request.Channel,
            ContentMarkdown = request.ContentMarkdown ?? string.Empty,
            HighlightsJson = request.Highlights != null
                ? JsonSerializer.Serialize(request.Highlights, JsonOptions)
                : null,
            KnownIssuesJson = request.KnownIssues != null
                ? JsonSerializer.Serialize(request.KnownIssues, JsonOptions)
                : null,
            IsPublished = false,
            CreatedAt = now,
            UpdatedAt = now
        };

        _db.ReleaseNotes.Add(note);
        await _db.SaveChangesAsync(ct);

        return StatusCode(201, ApiResponse<ReleaseNoteResponse>.Ok(MapToResponse(note), GetTraceId()));
    }

    // ===== PUT /api/release-notes/{id} — 更新（管理端）=====
    [HttpPut("{id:guid}")]
    [Authorize(Policy = AuthorizationPolicies.PlatformOperator)]
    public async Task<IActionResult> Update([FromRoute] Guid id, [FromBody] UpdateReleaseNoteRequest request, CancellationToken ct)
    {
        var note = await _db.ReleaseNotes.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (note == null)
        {
            return Ok(ApiResponse<ReleaseNoteResponse>.Fail("not_found", "Release note not found", GetTraceId()));
        }

        if (request.Title != null)
            note.Title = request.Title;
        if (request.ContentMarkdown != null)
            note.ContentMarkdown = request.ContentMarkdown;
        if (request.Highlights != null)
            note.HighlightsJson = JsonSerializer.Serialize(request.Highlights, JsonOptions);
        if (request.KnownIssues != null)
            note.KnownIssuesJson = JsonSerializer.Serialize(request.KnownIssues, JsonOptions);
        if (request.IsPublished.HasValue)
        {
            note.IsPublished = request.IsPublished.Value;
            if (request.IsPublished.Value && note.PublishedAt == null)
            {
                note.PublishedAt = DateTime.UtcNow;
            }
        }

        note.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        return Ok(ApiResponse<ReleaseNoteResponse>.Ok(MapToResponse(note), GetTraceId()));
    }

    // ===== POST /api/release-notes/{id}/publish — 发布（管理端）=====
    [HttpPost("{id:guid}/publish")]
    [Authorize(Policy = AuthorizationPolicies.PlatformOperator)]
    public async Task<IActionResult> Publish([FromRoute] Guid id, CancellationToken ct)
    {
        var note = await _db.ReleaseNotes.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (note == null)
        {
            return Ok(ApiResponse<ReleaseNoteResponse>.Fail("not_found", "Release note not found", GetTraceId()));
        }

        note.IsPublished = true;
        note.PublishedAt = DateTime.UtcNow;
        note.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        return Ok(ApiResponse<ReleaseNoteResponse>.Ok(MapToResponse(note), GetTraceId()));
    }

    // ===== Helpers =====

    private ReleaseNoteResponse MapToResponse(ReleaseNote r)
    {
        return new ReleaseNoteResponse
        {
            Id = r.Id,
            Version = r.Version,
            Title = r.Title,
            Channel = r.Channel,
            ContentMarkdown = r.ContentMarkdown,
            Highlights = DeserializeStringList(r.HighlightsJson),
            KnownIssues = DeserializeStringList(r.KnownIssuesJson),
            IsPublished = r.IsPublished,
            PublishedAt = r.PublishedAt,
            CreatedAt = r.CreatedAt,
            UpdatedAt = r.UpdatedAt
        };
    }

    private ReleaseNoteListItem MapToListItem(ReleaseNote r)
    {
        return new ReleaseNoteListItem
        {
            Id = r.Id,
            Version = r.Version,
            Title = r.Title,
            Channel = r.Channel,
            Highlights = DeserializeStringList(r.HighlightsJson),
            IsPublished = r.IsPublished,
            PublishedAt = r.PublishedAt,
            CreatedAt = r.CreatedAt
        };
    }

    private static List<string>? DeserializeStringList(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<List<string>>(json);
        }
        catch
        {
            return null;
        }
    }
}
