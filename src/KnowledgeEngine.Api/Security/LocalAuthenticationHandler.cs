using System.Net;
using System.Security.Claims;
using KnowledgeEngine.Application.Security;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace KnowledgeEngine.Api.Security;

public class LocalAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "Local";

    public LocalAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var remoteIp = Context.Connection.RemoteIpAddress;
        if (remoteIp == null || !IPAddress.IsLoopback(remoteIp))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var claims = new[]
        {
            new Claim("user_id", LocalUserConstants.UserId.ToString()),
            new Claim(ClaimTypes.NameIdentifier, LocalUserConstants.UserId.ToString()),
            new Claim("email", LocalUserConstants.Email),
            new Claim(ClaimTypes.Email, LocalUserConstants.Email),
            new Claim(ClaimTypes.Name, LocalUserConstants.Nickname),
            new Claim(ClaimTypes.Role, PlatformRoles.PlatformAdmin),
            new Claim("role", PlatformRoles.PlatformAdmin),
        };

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
