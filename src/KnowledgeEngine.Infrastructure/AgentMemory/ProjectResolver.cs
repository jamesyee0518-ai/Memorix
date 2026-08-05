using System.Security.Cryptography;
using System.Text;
using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KnowledgeEngine.Infrastructure.AgentMemory;

/// <summary>
/// Resolves a canonical <see cref="Project"/> identity from git metadata.
///
/// <para>
/// Two coding agents working on the same git repository (even across different
/// branches, worktrees, or machines) resolve to the same <see cref="Project"/>,
/// enabling shared memory and cross-agent handoffs. The identity is driven by
/// <see cref="Project.ProjectKey"/> — a stable hash of the git remote + canonical
/// repo name — never by the local filesystem path.
/// </para>
/// </summary>
public class ProjectResolver
{
    private readonly IAppDbContext _db;
    private readonly ILogger<ProjectResolver> _logger;

    public ProjectResolver(IAppDbContext db, ILogger<ProjectResolver> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// Compute the stable <see cref="Project.ProjectKey"/> from git remote and repo name.
    /// SHA256(gitRemote + "|" + repoName), truncated to 24 hex chars.
    /// For local-only repos with no remote, the remote component is "local".
    /// </summary>
    public static string ComputeProjectKey(string? gitRemote, string repoName)
    {
        var remote = string.IsNullOrWhiteSpace(gitRemote) ? "local" : NormalizeGitRemote(gitRemote);
        var material = $"{remote}|{repoName}";
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(material));
        return Convert.ToHexString(hashBytes, 0, 12).ToLowerInvariant(); // 24 hex chars
    }

    /// <summary>
    /// Normalize a git remote URL to a canonical form so that
    /// "https://github.com/user/repo.git" and "git@github.com:user/repo.git"
    /// collapse to the same key.
    /// </summary>
    private static string NormalizeGitRemote(string remote)
    {
        var r = remote.Trim().TrimEnd('/').TrimEnd(".git".ToCharArray());

        // SSH form: git@host:path → host/path
        if (r.Contains('@') && r.Contains(':'))
        {
            // git@github.com:user/repo → github.com/user/repo
            var atIdx = r.IndexOf('@');
            r = r[(atIdx + 1)..].Replace(':', '/');
        }
        else if (r.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                 r.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            // Strip protocol, keep host + path
            var schemeEnd = r.IndexOf("://", StringComparison.OrdinalIgnoreCase);
            if (schemeEnd >= 0) r = r[(schemeEnd + 3)..];
        }

        return r.ToLowerInvariant();
    }

    /// <summary>
    /// Resolve an existing Project by git metadata, or create one if none exists.
    /// Idempotent: concurrent callers for the same repo will share the same Project.
    /// </summary>
    /// <param name="gitRemote">The git remote URL (null for local-only repos).</param>
    /// <param name="repoName">The canonical repository name (e.g. "Memorix").</param>
    /// <param name="localRoot">The local checkout root (informational only).</param>
    public async Task<Project> ResolveOrCreateAsync(
        string? gitRemote,
        string repoName,
        string? localRoot,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(repoName))
        {
            throw new ArgumentException("repoName is required to resolve a project.", nameof(repoName));
        }

        var projectKey = ComputeProjectKey(gitRemote, repoName);

        // Try existing first
        var existing = await _db.Projects
            .FirstOrDefaultAsync(p => p.ProjectKey == projectKey, ct);

        if (existing != null)
        {
            return existing;
        }

        // Create new
        var now = DateTime.UtcNow;
        var project = new Project
        {
            Id = Guid.NewGuid(),
            ProjectKey = projectKey,
            RepoName = repoName,
            GitRemote = string.IsNullOrWhiteSpace(gitRemote) ? null : gitRemote,
            LocalRoot = localRoot,
            CreatedAt = now,
            UpdatedAt = now
        };

        _db.Projects.Add(project);
        try
        {
            await _db.SaveChangesAsync(ct);
            _logger.LogInformation(
                "Resolved new project: key={ProjectKey}, repo={RepoName}, remote={GitRemote}",
                projectKey, repoName, gitRemote ?? "(local)");
        }
        catch (DbUpdateException)
        {
            // Race condition: another caller created the same project.
            // Re-fetch the existing one.
            existing = await _db.Projects
                .FirstOrDefaultAsync(p => p.ProjectKey == projectKey, ct);
            if (existing != null) return existing;
            throw;
        }

        return project;
    }
}
