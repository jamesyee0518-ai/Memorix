using KnowledgeEngine.Application.DTOs;
using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace KnowledgeEngine.Api.Controllers;

/// <summary>
/// Post-ASR text correction API.
/// Provides endpoints for correcting transcription text using dictionaries
/// and managing correction dictionary entries (brand names, person names,
/// terminology, abbreviations, homophones, custom entries).
/// </summary>
[ApiController]
[Route("api/correction")]
[Authorize]
public class CorrectionController : BaseController
{
    private readonly IPostAsrCorrectionService _correctionService;
    private readonly ICurrentUserContext _currentUser;
    private readonly ILogger<CorrectionController> _logger;

    public CorrectionController(
        IPostAsrCorrectionService correctionService,
        ICurrentUserContext currentUser,
        ILogger<CorrectionController> logger)
    {
        _correctionService = correctionService;
        _currentUser = currentUser;
        _logger = logger;
    }

    /// <summary>
    /// Corrects transcription text using workspace dictionary entries and
    /// built-in correction rules.
    /// </summary>
    [HttpPost("correct")]
    public async Task<IActionResult> CorrectText([FromBody] CorrectionRequest request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated || _currentUser.UserId == null)
        {
            return Unauthorized(ApiResponse<object>.FailObject("UNAUTHORIZED", "User not authenticated", GetTraceId()));
        }

        if (string.IsNullOrWhiteSpace(request.Text))
        {
            return BadRequest(ApiResponse<object>.FailObject("MISSING_TEXT", "Text is required for correction", GetTraceId()));
        }

        var result = await _correctionService.CorrectAsync(request, ct);

        return Ok(ApiResponse<CorrectionResult>.Ok(result, GetTraceId()));
    }

    /// <summary>
    /// Lists correction dictionary entries for the current workspace, optionally
    /// filtered by category.
    /// </summary>
    [HttpGet("dictionary")]
    public async Task<IActionResult> ListEntries(
        [FromQuery] Guid? workspaceId,
        [FromQuery] string? category,
        CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated || _currentUser.UserId == null)
        {
            return Unauthorized(ApiResponse<object>.FailObject("UNAUTHORIZED", "User not authenticated", GetTraceId()));
        }

        var entries = await _correctionService.ListEntriesAsync(workspaceId, category, ct);

        var dtos = entries.Select(ToDto).ToList();

        return Ok(ApiResponse<List<CorrectionDictionaryDto>>.Ok(dtos, GetTraceId()));
    }

    /// <summary>
    /// Adds a new entry to the correction dictionary.
    /// </summary>
    [HttpPost("dictionary")]
    public async Task<IActionResult> AddEntry([FromBody] AddCorrectionEntryRequest request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated || _currentUser.UserId == null)
        {
            return Unauthorized(ApiResponse<object>.FailObject("UNAUTHORIZED", "User not authenticated", GetTraceId()));
        }

        if (string.IsNullOrWhiteSpace(request.Original))
        {
            return BadRequest(ApiResponse<object>.FailObject("MISSING_ORIGINAL", "Original text is required", GetTraceId()));
        }

        if (string.IsNullOrWhiteSpace(request.Corrected))
        {
            return BadRequest(ApiResponse<object>.FailObject("MISSING_CORRECTED", "Corrected text is required", GetTraceId()));
        }

        var entry = await _correctionService.AddEntryAsync(
            request.WorkspaceId,
            request.Original,
            request.Corrected,
            request.Category,
            _currentUser.UserId.Value.ToString(),
            ct);

        return Ok(ApiResponse<CorrectionDictionaryDto>.Ok(ToDto(entry), GetTraceId()));
    }

    /// <summary>
    /// Deletes (deactivates) a correction dictionary entry by ID.
    /// </summary>
    [HttpDelete("dictionary/{id}")]
    public async Task<IActionResult> DeleteEntry(Guid id, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated || _currentUser.UserId == null)
        {
            return Unauthorized(ApiResponse<object>.FailObject("UNAUTHORIZED", "User not authenticated", GetTraceId()));
        }

        var deleted = await _correctionService.DeleteEntryAsync(id, ct);

        if (!deleted)
        {
            return NotFound(ApiResponse<object>.FailObject("ENTRY_NOT_FOUND", "Dictionary entry not found", GetTraceId()));
        }

        return Ok(ApiResponse<object>.Ok(new { id, deleted = true }, GetTraceId()));
    }

    // ── Helpers ──

    private static CorrectionDictionaryDto ToDto(CorrectionDictionary entry)
    {
        return new CorrectionDictionaryDto
        {
            Id = entry.Id,
            WorkspaceId = entry.WorkspaceId,
            OriginalText = entry.OriginalText,
            CorrectedText = entry.CorrectedText,
            Category = entry.Category,
            Language = entry.Language,
            CreatedBy = entry.CreatedBy,
            IsActive = entry.IsActive,
            CreatedAt = entry.CreatedAt,
            UpdatedAt = entry.UpdatedAt,
        };
    }
}

// ── Correction Controller DTOs ──

/// <summary>
/// Request payload for adding a correction dictionary entry.
/// </summary>
public class AddCorrectionEntryRequest
{
    public Guid? WorkspaceId { get; set; }
    public string Original { get; set; } = string.Empty;
    public string Corrected { get; set; } = string.Empty;
    public string? Category { get; set; }
}

/// <summary>
/// DTO for a correction dictionary entry, safe for API responses.
/// </summary>
public class CorrectionDictionaryDto
{
    public Guid Id { get; set; }
    public Guid? WorkspaceId { get; set; }
    public string OriginalText { get; set; } = string.Empty;
    public string CorrectedText { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string? Language { get; set; }
    public string? CreatedBy { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
