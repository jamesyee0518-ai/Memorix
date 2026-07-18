namespace KnowledgeEngine.Application.DTOs;

public class CloudAccountBindingDto
{
    public Guid Id { get; set; }
    public Guid LocalProfileId { get; set; }
    public string CloudUserId { get; set; } = string.Empty;
    public string CloudApiBaseUrl { get; set; } = string.Empty;
    public string? AccountDisplayName { get; set; }
    public string? AccountEmailMasked { get; set; }
    public string BindingStatus { get; set; } = string.Empty;
    public DateTime? LastAuthenticatedAt { get; set; }
}

public class CreateCloudAccountBindingDto
{
    public string CloudUserId { get; set; } = string.Empty;
    public string CloudApiBaseUrl { get; set; } = string.Empty;
    public string? AccountDisplayName { get; set; }
    public string? AccountEmailMasked { get; set; }
    public string RefreshToken { get; set; } = string.Empty;
    public string? AccessToken { get; set; }
    public string? TokenEndpoint { get; set; }
    public string? OAuthClientId { get; set; }
    public int? AccessTokenExpiresInSeconds { get; set; }
}

public class WorkspaceBindingDto
{
    public Guid Id { get; set; }
    public Guid LocalWorkspaceId { get; set; }
    public Guid CloudAccountBindingId { get; set; }
    public string CloudWorkspaceId { get; set; } = string.Empty;
    public string SyncMode { get; set; } = string.Empty;
    public string BindingStatus { get; set; } = string.Empty;
    public Guid? PrimaryDeviceId { get; set; }
    public bool UploadOriginalFiles { get; set; }
    public string ConflictPolicy { get; set; } = string.Empty;
    public string? LastInboxCursor { get; set; }
    public string? LastSyncCursor { get; set; }
    public DateTime? LastSyncAt { get; set; }
}

public class CreateWorkspaceBindingDto
{
    public Guid LocalWorkspaceId { get; set; }
    public Guid CloudAccountBindingId { get; set; }
    public string CloudWorkspaceId { get; set; } = string.Empty;
    public string SyncMode { get; set; } = "none";
    public Guid? PrimaryDeviceId { get; set; }
    public bool UploadOriginalFiles { get; set; }
    public string ConflictPolicy { get; set; } = "manual";
}

public class UpdateWorkspaceBindingDto
{
    public string? SyncMode { get; set; }
    public Guid? PrimaryDeviceId { get; set; }
    public bool? UploadOriginalFiles { get; set; }
    public string? ConflictPolicy { get; set; }
}
