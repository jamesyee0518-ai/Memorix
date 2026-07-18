using KnowledgeEngine.Application.DTOs;
using KnowledgeEngine.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace KnowledgeEngine.Api.Controllers;

[Authorize]
public class SearchController : BaseController
{
    private readonly ISearchService _searchService;
    private readonly ICurrentUserContext _currentUser;

    public SearchController(ISearchService searchService, ICurrentUserContext currentUser)
    {
        _searchService = searchService;
        _currentUser = currentUser;
    }

    [HttpPost]
    public async Task<IActionResult> Search([FromBody] SearchRequest request, CancellationToken ct)
    {
        var userId = _currentUser.UserId;
        if (userId == null)
        {
            return Unauthorized();
        }

        var result = await _searchService.SearchAsync(userId.Value, request, ct);

        if (!result.Success)
        {
            return Ok(ApiResponse<SearchResult>.Fail(result.Error!.Code, result.Error!.Message, GetTraceId()));
        }

        return Ok(ApiResponse<SearchResult>.Ok(result.Data!, GetTraceId()));
    }
}
