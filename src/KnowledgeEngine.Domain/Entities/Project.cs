namespace KnowledgeEngine.Domain.Entities;

/// <summary>
/// A canonical project identity derived from git metadata, allowing different
/// coding agents (Codex, Claude, Trae) working on the same repository (across
/// different branches/worktrees) to share memory and handoffs.
///
/// <para>
/// The <see cref="ProjectKey"/> is a stable hash of the git remote + canonical
/// repo name, so all sessions against the same upstream collapse to one Project.
/// </para>
/// </summary>
public class Project
{
    public Guid Id { get; set; }

    /// <summary>
    /// Globally-unique stable key: SHA256(git_remote + "|" + repo_name) truncated
    /// to 24 hex chars. Two agents on different worktrees of the same repo
    /// resolve to the same ProjectKey.
    /// </summary>
    public string ProjectKey { get; set; } = string.Empty;

    public string RepoName { get; set; } = string.Empty;

    /// <summary>
    /// The canonical git remote URL (e.g. "https://github.com/user/repo.git").
    /// Null for local-only repos with no remote yet.
    /// </summary>
    public string? GitRemote { get; set; }

    /// <summary>
    /// The local root path of the repo when it was first registered. Informational
    /// only — the same project may be checked out at different paths on different
    /// machines. Identity is driven by <see cref="ProjectKey"/>, not this path.
    /// </summary>
    public string? LocalRoot { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
