using KnowledgeEngine.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace KnowledgeEngine.Application.Services;

/// <summary>
/// Checks for duplicate content in the knowledge base (§10).
/// Prevents the same URL, text content, or file from being imported twice.
/// </summary>
public class DuplicateChecker
{
    private readonly IKnowledgeRepository _repo;
    private readonly ILogger<DuplicateChecker> _logger;

    public DuplicateChecker(IKnowledgeRepository repo, ILogger<DuplicateChecker> logger)
    {
        _repo = repo;
        _logger = logger;
    }

    /// <summary>
    /// Checks if a URL already exists in sources or inbox_items for the given workspace.
    /// </summary>
    public async Task<bool> CheckUrlAsync(string workspaceId, string url, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(url))
            return false;

        var isDup = await _repo.IsDuplicateUrlAsync(workspaceId, url, ct);
        if (isDup)
        {
            _logger.LogInformation("Duplicate URL detected: {Url} in workspace {WorkspaceId}", url, workspaceId);
        }
        return isDup;
    }

    /// <summary>
    /// Checks if content with the given content_hash already exists in sources.
    /// </summary>
    public async Task<bool> CheckContentAsync(string workspaceId, string contentHash, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(contentHash))
            return false;

        var isDup = await _repo.IsDuplicateContentAsync(workspaceId, contentHash, ct);
        if (isDup)
        {
            _logger.LogInformation("Duplicate content detected: hash={Hash} in workspace {WorkspaceId}", contentHash, workspaceId);
        }
        return isDup;
    }

    /// <summary>
    /// Checks if a file with the given SHA256 hash already exists.
    /// Uses the same content_hash check since file hashes are stored in content_hash.
    /// </summary>
    public async Task<bool> CheckFileAsync(string workspaceId, string sha256, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(sha256))
            return false;

        // File dedup uses the same content_hash mechanism
        var isDup = await _repo.IsDuplicateContentAsync(workspaceId, sha256, ct);
        if (isDup)
        {
            _logger.LogInformation("Duplicate file detected: sha256={Hash} in workspace {WorkspaceId}", sha256, workspaceId);
        }
        return isDup;
    }
}
