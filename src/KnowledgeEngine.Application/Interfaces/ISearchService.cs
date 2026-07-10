using KnowledgeEngine.Application.DTOs;

namespace KnowledgeEngine.Application.Interfaces;

public interface ISearchService
{
    Task<ApiResponse<SearchResult>> SearchAsync(Guid userId, SearchRequest request, CancellationToken ct = default);
}
