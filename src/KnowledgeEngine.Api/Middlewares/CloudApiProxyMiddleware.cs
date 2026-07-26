using System.Net.Http.Headers;
using System.Text.Json;
using KnowledgeEngine.Api.Services;
using KnowledgeEngine.Application.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeEngine.Api.Middlewares;

public sealed class CloudApiProxyMiddleware
{
    private static readonly string[] ProxiedPrefixes =
    [
        "/api/dashboard", "/api/topics", "/api/sources", "/api/documents",
        "/api/search", "/api/qa", "/api/reports", "/api/exports",
        "/api/entities", "/api/tags", "/api/usage"
    ];
    private readonly RequestDelegate _next;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;

    public CloudApiProxyMiddleware(
        RequestDelegate next,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration)
    {
        _next = next;
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
    }

    public async Task InvokeAsync(
        HttpContext context,
        IConfigService configService,
        IAppDbContext db,
        ICurrentUserContext currentUser,
        IBindingService bindingService,
        DesktopRuntimeCoordinator coordinator)
    {
        if (!ProxiedPrefixes.Any(prefix =>
                context.Request.Path.StartsWithSegments(prefix, StringComparison.OrdinalIgnoreCase)))
        {
            await _next(context);
            return;
        }

        var configuredWorkspaceId = await configService.GetCurrentWorkspaceIdAsync(context.RequestAborted);
        if (!Guid.TryParse(configuredWorkspaceId, out var localWorkspaceId))
        {
            await _next(context);
            return;
        }
        var userId = currentUser.UserId;
        var workspace = userId.HasValue
            ? await db.Workspaces.AsNoTracking().FirstOrDefaultAsync(
                x => x.Id == localWorkspaceId && x.UserId == userId.Value,
                context.RequestAborted)
            : null;
        if (workspace?.Mode != "cloud")
        {
            await _next(context);
            return;
        }

        if (!_configuration.GetValue<bool>("Features:DesktopCloudModeEnabled"))
        {
            await WriteErrorAsync(context, 503, "CLOUD_MODE_DISABLED", "云端模式尚未启用。请切换到本地工作区。", context.RequestAborted);
            return;
        }
        if (context.Request.HasFormContentType)
        {
            await WriteErrorAsync(context, 501, "CLOUD_UPLOAD_NOT_READY", "云端文件上传将在下一阶段开放。", context.RequestAborted);
            return;
        }

        var binding = (await bindingService.ListWorkspaceBindingsAsync(localWorkspaceId, context.RequestAborted))
            .FirstOrDefault(x => x.BindingStatus == "active");
        if (binding == null)
        {
            await WriteErrorAsync(context, 503, "CLOUD_WORKSPACE_NOT_BOUND", "当前工作区尚未绑定云端工作区。", context.RequestAborted);
            return;
        }
        var account = (await bindingService.ListCloudAccountsAsync(context.RequestAborted))
            .FirstOrDefault(x => x.Id == binding.CloudAccountBindingId && x.BindingStatus == "active");
        if (account == null)
        {
            await WriteErrorAsync(context, 401, "CLOUD_REAUTH_REQUIRED", "云端账号需要重新登录。", context.RequestAborted);
            return;
        }
        var targetBase = ValidateTarget(account.CloudApiBaseUrl);
        if (IsLoop(context, targetBase))
        {
            await WriteErrorAsync(context, 508, "CLOUD_PROXY_LOOP", "云端 API 地址指向了当前桌面服务。", context.RequestAborted);
            return;
        }

        var body = await ReadBodyAsync(context.Request, context.RequestAborted);
        var token = await bindingService.GetAccessTokenAsync(account.Id, context.RequestAborted);
        if (string.IsNullOrWhiteSpace(token))
        {
            await WriteErrorAsync(context, 401, "CLOUD_REAUTH_REQUIRED", "云端访问令牌不可用，请重新登录。", context.RequestAborted);
            return;
        }

        var modeChangedToken = coordinator.ModeChangedToken;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            context.RequestAborted, modeChangedToken);
        HttpResponseMessage response;
        try
        {
            response = await SendAsync(context.Request, targetBase, binding.CloudWorkspaceId, token, body, linked.Token);
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                response.Dispose();
                token = await bindingService.RefreshAccessTokenAsync(account.Id, linked.Token);
                response = string.IsNullOrWhiteSpace(token)
                    ? new HttpResponseMessage(System.Net.HttpStatusCode.Unauthorized)
                    : await SendAsync(context.Request, targetBase, binding.CloudWorkspaceId, token, body, linked.Token);
            }
        }
        catch (OperationCanceledException) when (
            modeChangedToken.IsCancellationRequested && !context.RequestAborted.IsCancellationRequested)
        {
            await WriteErrorAsync(context, 409, "RUNTIME_MODE_CHANGED", "工作区已切换，本次请求已取消。", context.RequestAborted);
            return;
        }
        catch (HttpRequestException)
        {
            await WriteErrorAsync(context, 503, "CLOUD_OFFLINE", "无法连接云端服务，请检查网络后重试。", context.RequestAborted);
            return;
        }
        using (response)
        {
            context.Response.StatusCode = (int)response.StatusCode;
            foreach (var header in response.Headers)
                context.Response.Headers[header.Key] = header.Value.ToArray();
            foreach (var header in response.Content.Headers)
                context.Response.Headers[header.Key] = header.Value.ToArray();
            context.Response.Headers.Remove("transfer-encoding");
            await response.Content.CopyToAsync(context.Response.Body, linked.Token);
        }
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpRequest source, Uri targetBase, string cloudWorkspaceId,
        string token, byte[]? body, CancellationToken ct)
    {
        var target = new UriBuilder(targetBase)
        {
            Path = source.Path,
            Query = source.QueryString.HasValue ? source.QueryString.Value![1..] : string.Empty
        }.Uri;
        var request = new HttpRequestMessage(new HttpMethod(source.Method), target);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.TryAddWithoutValidation("X-Workspace-Id", cloudWorkspaceId);
        request.Headers.TryAddWithoutValidation("X-Memorix-Desktop", "1");
        if (body is { Length: > 0 })
        {
            request.Content = new ByteArrayContent(body);
            if (!string.IsNullOrWhiteSpace(source.ContentType))
                request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(source.ContentType);
        }
        return await _httpClientFactory.CreateClient().SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, ct);
    }

    private static async Task<byte[]?> ReadBodyAsync(HttpRequest request, CancellationToken ct)
    {
        if (request.ContentLength == 0) return null;
        if (HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method)) return null;
        using var buffer = new MemoryStream();
        await request.Body.CopyToAsync(buffer, ct);
        return buffer.ToArray();
    }

    private static Uri ValidateTarget(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            (uri.Scheme != Uri.UriSchemeHttps && !(uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback)))
            throw new InvalidOperationException("云端 API 地址必须使用 HTTPS（本机回环地址除外）。");
        return uri;
    }

    private static bool IsLoop(HttpContext context, Uri target) =>
        string.Equals(context.Request.Host.Host, target.Host, StringComparison.OrdinalIgnoreCase) &&
        context.Request.Host.Port == (target.IsDefaultPort ? null : target.Port);

    private static async Task WriteErrorAsync(
        HttpContext context, int status, string code, string message, CancellationToken ct)
    {
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            success = false,
            error = new { code, message },
            traceId = context.TraceIdentifier
        }), ct);
    }
}
