using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using KnowledgeEngine.Application.Interfaces;
using Microsoft.AspNetCore.Http;

namespace KnowledgeEngine.Infrastructure.Security;

public class CurrentUserContext : ICurrentUserContext
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentUserContext(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public Guid? UserId
    {
        get
        {
            var user = _httpContextAccessor.HttpContext?.User;
            if (user == null) return null;

            var userIdClaim = user.FindFirst("user_id")
                ?? user.FindFirst(ClaimTypes.NameIdentifier)
                ?? user.FindFirst(JwtRegisteredClaimNames.Sub);

            if (userIdClaim != null && Guid.TryParse(userIdClaim.Value, out var userId))
            {
                return userId;
            }
            return null;
        }
    }

    public string? Email
    {
        get
        {
            var user = _httpContextAccessor.HttpContext?.User;
            if (user == null) return null;

            var emailClaim = user.FindFirst("email")
                ?? user.FindFirst(ClaimTypes.Email)
                ?? user.FindFirst(JwtRegisteredClaimNames.Email);

            return emailClaim?.Value;
        }
    }

    public bool IsAuthenticated
    {
        get
        {
            var identity = _httpContextAccessor.HttpContext?.User?.Identity;
            return identity?.IsAuthenticated ?? false;
        }
    }
}
