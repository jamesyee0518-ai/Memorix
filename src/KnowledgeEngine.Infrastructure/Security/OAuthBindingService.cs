using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KnowledgeEngine.Application.DTOs;
using KnowledgeEngine.Application.Interfaces;

namespace KnowledgeEngine.Infrastructure.Security;

public sealed class OAuthBindingService : IOAuthBindingService
{
    private static readonly ConcurrentDictionary<string, OAuthSession> Sessions = new();
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IBindingService _bindingService;

    public OAuthBindingService(
        IHttpClientFactory httpClientFactory,
        IBindingService bindingService)
    {
        _httpClientFactory = httpClientFactory;
        _bindingService = bindingService;
    }

    public Task<OAuthStartResultDto> StartAsync(StartOAuthDto input, CancellationToken ct = default)
    {
        ValidateStartInput(input);
        RemoveExpiredSessions();

        var sessionId = $"oauth_{Guid.CreateVersion7():N}";
        var state = Base64Url(RandomNumberGenerator.GetBytes(32));
        var nonce = Base64Url(RandomNumberGenerator.GetBytes(32));
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(64));
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var expiresAt = DateTime.UtcNow.AddMinutes(10);
        var session = new OAuthSession
        {
            SessionId = sessionId,
            State = state,
            Nonce = nonce,
            CodeVerifier = verifier,
            Input = input,
            ExpiresAt = expiresAt
        };
        Sessions[state] = session;

        var parameters = new Dictionary<string, string?>
        {
            ["client_id"] = input.ClientId,
            ["redirect_uri"] = input.RedirectUri,
            ["response_type"] = "code",
            ["scope"] = input.Scope,
            ["code_challenge"] = challenge,
            ["code_challenge_method"] = "S256",
            ["state"] = state,
            ["nonce"] = nonce
        };
        var authorizationUrl = QueryString(input.AuthorizationEndpoint, parameters);
        return Task.FromResult(new OAuthStartResultDto
        {
            SessionId = sessionId,
            AuthorizationUrl = authorizationUrl,
            ExpiresAt = expiresAt
        });
    }

    public async Task CompleteAsync(string code, string state, CancellationToken ct = default)
    {
        if (!Sessions.TryRemove(state, out var session))
        {
            throw new InvalidOperationException("OAuth state is invalid, expired, or already used.");
        }
        if (session.ExpiresAt <= DateTime.UtcNow)
        {
            session.Status = "failed";
            session.ErrorMessage = "OAuth session expired.";
            Sessions[$"completed:{session.SessionId}"] = session;
            throw new InvalidOperationException(session.ErrorMessage);
        }

        try
        {
            var client = _httpClientFactory.CreateClient();
            using var response = await client.PostAsync(
                session.Input.TokenEndpoint,
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "authorization_code",
                    ["client_id"] = session.Input.ClientId,
                    ["code"] = code,
                    ["redirect_uri"] = session.Input.RedirectUri,
                    ["code_verifier"] = session.CodeVerifier
                }),
                ct);
            response.EnsureSuccessStatusCode();
            var token = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken: ct)
                ?? throw new InvalidOperationException("OAuth token endpoint returned an empty response.");
            if (string.IsNullOrWhiteSpace(token.AccessToken) ||
                string.IsNullOrWhiteSpace(token.RefreshToken))
            {
                throw new InvalidOperationException("OAuth provider did not return access and refresh tokens.");
            }

            var identity = await ResolveIdentityAsync(client, session, token, ct);
            var binding = await _bindingService.BindCloudAccountAsync(new CreateCloudAccountBindingDto
            {
                CloudUserId = identity.Subject,
                CloudApiBaseUrl = session.Input.CloudApiBaseUrl,
                AccountDisplayName = identity.Name,
                AccountEmailMasked = MaskEmail(identity.Email),
                RefreshToken = token.RefreshToken,
                AccessToken = token.AccessToken,
                TokenEndpoint = session.Input.TokenEndpoint,
                OAuthClientId = session.Input.ClientId,
                AccessTokenExpiresInSeconds = token.ExpiresIn
            }, ct);
            session.Status = "completed";
            session.CloudAccountBindingId = binding.Id;
        }
        catch (Exception ex)
        {
            session.Status = "failed";
            session.ErrorMessage = ex.Message;
            throw;
        }
        finally
        {
            Sessions[$"completed:{session.SessionId}"] = session;
        }
    }

    public Task<OAuthStatusDto> GetStatusAsync(string sessionId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var session = Sessions.Values.FirstOrDefault(x => x.SessionId == sessionId);
        return Task.FromResult(session == null
            ? new OAuthStatusDto { Status = "expired", ErrorMessage = "OAuth session not found." }
            : new OAuthStatusDto
            {
                Status = session.Status,
                CloudAccountBindingId = session.CloudAccountBindingId,
                ErrorMessage = session.ErrorMessage
            });
    }

    private static async Task<OAuthIdentity> ResolveIdentityAsync(
        HttpClient client,
        OAuthSession session,
        TokenResponse token,
        CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(session.Input.UserInfoEndpoint))
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, session.Input.UserInfoEndpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
            using var response = await client.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<OAuthIdentity>(cancellationToken: ct);
            if (result == null || string.IsNullOrWhiteSpace(result.Subject))
            {
                throw new InvalidOperationException("OAuth user-info response is missing the subject.");
            }
            return result;
        }

        if (string.IsNullOrWhiteSpace(token.IdToken))
        {
            throw new InvalidOperationException("OAuth provider requires a user-info endpoint or ID token.");
        }
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token.IdToken);
        var nonce = jwt.Claims.FirstOrDefault(x => x.Type == "nonce")?.Value;
        if (!string.Equals(nonce, session.Nonce, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("OAuth ID token nonce validation failed.");
        }
        return new OAuthIdentity
        {
            Subject = jwt.Claims.FirstOrDefault(x => x.Type == "sub")?.Value ?? string.Empty,
            Email = jwt.Claims.FirstOrDefault(x => x.Type == "email")?.Value,
            Name = jwt.Claims.FirstOrDefault(x => x.Type == "name")?.Value
        };
    }

    private static void ValidateStartInput(StartOAuthDto input)
    {
        ValidateHttpsUrl(input.AuthorizationEndpoint, nameof(input.AuthorizationEndpoint));
        ValidateHttpsUrl(input.TokenEndpoint, nameof(input.TokenEndpoint));
        if (input.UserInfoEndpoint != null)
        {
            ValidateHttpsUrl(input.UserInfoEndpoint, nameof(input.UserInfoEndpoint));
        }
        if (!Uri.TryCreate(input.RedirectUri, UriKind.Absolute, out var redirect) ||
            redirect.Scheme != Uri.UriSchemeHttp ||
            !redirect.IsLoopback)
        {
            throw new ArgumentException("OAuth redirect URI must be an HTTP loopback address.");
        }
        if (string.IsNullOrWhiteSpace(input.ClientId))
        {
            throw new ArgumentException("OAuth client ID is required.");
        }
    }

    private static void ValidateHttpsUrl(string value, string name)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException($"{name} must be an absolute HTTPS URL.");
        }
    }

    private static string QueryString(string baseUrl, IReadOnlyDictionary<string, string?> parameters) =>
        $"{baseUrl}{(baseUrl.Contains('?') ? '&' : '?')}{string.Join("&", parameters
            .Where(x => x.Value != null)
            .Select(x => $"{Uri.EscapeDataString(x.Key)}={Uri.EscapeDataString(x.Value!)}"))}";

    private static string Base64Url(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string? MaskEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return null;
        var at = email.IndexOf('@');
        if (at <= 1) return $"***{email[at..]}";
        return $"{email[0]}***{email[(at - 1)..]}";
    }

    private static void RemoveExpiredSessions()
    {
        var cutoff = DateTime.UtcNow.AddMinutes(-30);
        foreach (var pair in Sessions.Where(x => x.Value.ExpiresAt < cutoff).ToList())
        {
            Sessions.TryRemove(pair.Key, out _);
        }
    }

    private sealed class OAuthSession
    {
        public string SessionId { get; init; } = string.Empty;
        public string State { get; init; } = string.Empty;
        public string Nonce { get; init; } = string.Empty;
        public string CodeVerifier { get; init; } = string.Empty;
        public StartOAuthDto Input { get; init; } = new();
        public DateTime ExpiresAt { get; init; }
        public string Status { get; set; } = "pending";
        public Guid? CloudAccountBindingId { get; set; }
        public string? ErrorMessage { get; set; }
    }

    private sealed class TokenResponse
    {
        [System.Text.Json.Serialization.JsonPropertyName("access_token")]
        public string AccessToken { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("refresh_token")]
        public string RefreshToken { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("id_token")]
        public string? IdToken { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("expires_in")]
        public int? ExpiresIn { get; set; }
    }

    private sealed class OAuthIdentity
    {
        [System.Text.Json.Serialization.JsonPropertyName("sub")]
        public string Subject { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("email")]
        public string? Email { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("name")]
        public string? Name { get; set; }
    }
}
