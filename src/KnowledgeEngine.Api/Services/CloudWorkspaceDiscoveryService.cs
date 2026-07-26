using System.Net.Http.Headers;
using System.Text.Json;
using KnowledgeEngine.Application.DTOs;
using KnowledgeEngine.Application.Interfaces;

namespace KnowledgeEngine.Api.Services;

public sealed class CloudWorkspaceDiscoveryService
{
    private readonly IBindingService _bindingService;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;

    public CloudWorkspaceDiscoveryService(
        IBindingService bindingService,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration)
    {
        _bindingService = bindingService;
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
    }

    public async Task<CloudWorkspaceDiscoveryDto> DiscoverAsync(
        Guid cloudAccountBindingId,
        CancellationToken ct = default)
    {
        var account = (await _bindingService.ListCloudAccountsAsync(ct))
            .FirstOrDefault(x => x.Id == cloudAccountBindingId && x.BindingStatus == "active")
            ?? throw new InvalidOperationException("未找到当前本地用户的有效云端账号，请重新登录。");
        var baseUri = ValidateCloudApiBaseUri(account.CloudApiBaseUrl);
        var accessToken = await _bindingService.GetAccessTokenAsync(cloudAccountBindingId, ct);
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            throw new InvalidOperationException("云端账号访问令牌不可用，请重新登录。");
        }

        var client = _httpClientFactory.CreateClient();
        using var workspaceRequest = CreateRequest(
            HttpMethod.Get, BuildApiUri(baseUri, "workspaces"), accessToken);
        using var workspaceResponse = await client.SendAsync(workspaceRequest, ct);
        if (workspaceResponse.StatusCode is System.Net.HttpStatusCode.Unauthorized or
            System.Net.HttpStatusCode.Forbidden)
        {
            throw new InvalidOperationException("云端账号授权已失效或无权访问工作区，请重新登录。");
        }
        if (!workspaceResponse.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"云端工作区加载失败（HTTP {(int)workspaceResponse.StatusCode}）。");
        }

        await using var stream = await workspaceResponse.Content.ReadAsStreamAsync(ct);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var workspaces = ParseWorkspaces(document.RootElement);
        var result = new CloudWorkspaceDiscoveryDto { Workspaces = workspaces };
        await PopulateCapabilitiesAsync(client, baseUri, accessToken, result, ct);
        return result;
    }

    private async Task PopulateCapabilitiesAsync(
        HttpClient client,
        Uri baseUri,
        string accessToken,
        CloudWorkspaceDiscoveryDto result,
        CancellationToken ct)
    {
        try
        {
            using var request = CreateRequest(
                HttpMethod.Get, BuildApiUri(baseUri, "desktop/cloud-api-capabilities"), accessToken);
            using var response = await client.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                result.Compatible = false;
                result.CompatibilityMessage = "云端服务未提供版本协商接口，请升级云端服务。";
                return;
            }
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            var data = GetEnvelopeData(document.RootElement);
            result.CloudApiVersion = GetString(data, "apiVersion");
            if (TryGetProperty(data, "features", out var features) &&
                features.ValueKind == JsonValueKind.Array)
            {
                result.Capabilities = features.EnumerateArray()
                    .Where(x => x.ValueKind == JsonValueKind.String)
                    .Select(x => x.GetString()!)
                    .ToList();
            }
            var minimum = _configuration["Features:MinimumCloudApiVersion"] ?? "1.0";
            result.Compatible = IsAtLeast(result.CloudApiVersion, minimum);
            result.CompatibilityMessage = result.Compatible
                ? null
                : $"云端 API 版本 {result.CloudApiVersion ?? "未知"} 低于桌面端要求的 {minimum}。";
        }
        catch (HttpRequestException)
        {
            result.Compatible = false;
            result.CompatibilityMessage = "无法完成云端 API 版本协商。";
        }
        catch (JsonException)
        {
            result.Compatible = false;
            result.CompatibilityMessage = "云端 API 版本响应格式不兼容。";
        }
    }

    private static List<CloudWorkspaceSummaryDto> ParseWorkspaces(JsonElement root)
    {
        var data = GetEnvelopeData(root);
        if (data.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("云端工作区响应格式不正确。");
        }
        return data.EnumerateArray()
            .Select(item => new CloudWorkspaceSummaryDto
            {
                Id = GetString(item, "id") ?? string.Empty,
                Name = GetString(item, "name") ?? "未命名工作区",
                Mode = GetString(item, "mode") ?? "cloud",
                Role = GetString(item, "role")
            })
            .Where(x => !string.IsNullOrWhiteSpace(x.Id))
            .ToList();
    }

    private static JsonElement GetEnvelopeData(JsonElement root)
    {
        if (TryGetProperty(root, "success", out var success) &&
            success.ValueKind == JsonValueKind.False)
        {
            throw new InvalidOperationException("云端服务返回了失败响应。");
        }
        return TryGetProperty(root, "data", out var data) ? data : root;
    }

    private static HttpRequestMessage CreateRequest(HttpMethod method, Uri uri, string token)
    {
        var request = new HttpRequestMessage(method, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    private static Uri BuildApiUri(Uri baseUri, string resource)
    {
        var path = baseUri.AbsolutePath.TrimEnd('/');
        if (!path.EndsWith("/api", StringComparison.OrdinalIgnoreCase)) path += "/api";
        var builder = new UriBuilder(baseUri) { Path = $"{path}/{resource.TrimStart('/')}", Query = string.Empty };
        return builder.Uri;
    }

    private static Uri ValidateCloudApiBaseUri(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            (uri.Scheme != Uri.UriSchemeHttps &&
             !(uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback)))
        {
            throw new InvalidOperationException("云端 API 必须使用 HTTPS（本机回环地址除外）。");
        }
        return uri;
    }

    private static bool IsAtLeast(string? actual, string minimum) =>
        Version.TryParse(actual, out var actualVersion) &&
        Version.TryParse(minimum, out var minimumVersion) &&
        actualVersion >= minimumVersion;

    private static string? GetString(JsonElement element, string name) =>
        TryGetProperty(element, name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }
        value = default;
        return false;
    }
}
