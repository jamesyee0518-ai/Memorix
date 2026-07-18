namespace KnowledgeEngine.Application.DTOs;

public class StartOAuthDto
{
    public string AuthorizationEndpoint { get; set; } = string.Empty;
    public string TokenEndpoint { get; set; } = string.Empty;
    public string? UserInfoEndpoint { get; set; }
    public string ClientId { get; set; } = string.Empty;
    public string RedirectUri { get; set; } = string.Empty;
    public string Scope { get; set; } = "openid profile email offline_access";
    public string CloudApiBaseUrl { get; set; } = string.Empty;
}

public class OAuthStartResultDto
{
    public string SessionId { get; set; } = string.Empty;
    public string AuthorizationUrl { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
}

public class OAuthStatusDto
{
    public string Status { get; set; } = "pending";
    public Guid? CloudAccountBindingId { get; set; }
    public string? ErrorMessage { get; set; }
}
