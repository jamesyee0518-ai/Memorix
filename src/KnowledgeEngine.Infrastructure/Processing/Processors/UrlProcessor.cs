using System.Net;
using System.Text.RegularExpressions;
using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace KnowledgeEngine.Infrastructure.Processing.Processors;

public class UrlProcessor : ISourceProcessor
{
    private const string ParserVersion = "1.0";
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(20);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IContentProcessor _contentProcessor;
    private readonly ILogger<UrlProcessor> _logger;

    public UrlProcessor(
        IHttpClientFactory httpClientFactory,
        IContentProcessor contentProcessor,
        ILogger<UrlProcessor> logger)
    {
        _httpClientFactory = httpClientFactory;
        _contentProcessor = contentProcessor;
        _logger = logger;
    }

    public bool Supports(string sourceType)
    {
        return string.Equals(sourceType, "url", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<ParseResult> ParseAsync(Source source, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(source.Url))
        {
            throw new InvalidOperationException("URL source has no URL");
        }

        var httpClient = _httpClientFactory.CreateClient("ContentFetcher");
        httpClient.Timeout = RequestTimeout;
        httpClient.DefaultRequestHeaders.Add("User-Agent", "KnowledgeEngineBot/0.1");

        var response = await httpClient.GetAsync(source.Url, ct);
        response.EnsureSuccessStatusCode();

        var html = await response.Content.ReadAsStringAsync(ct);

        var markdown = await _contentProcessor.CleanHtmlAsync(html);
        var title = ExtractTitleFromHtml(html) ?? source.Title;
        var domain = ExtractDomain(source.Url);

        var result = new ParseResult
        {
            Title = title,
            Domain = domain,
            Author = source.Author,
            PublishedAt = source.PublishedAt,
            RawText = markdown,
            RawHtml = html,
            Markdown = markdown,
            ParserName = "url",
            ParserVersion = ParserVersion
        };

        _logger.LogInformation("URL parsed: {Url}, domain={Domain}, title={Title}",
            source.Url, domain, title);

        return result;
    }

    private static string? ExtractTitleFromHtml(string html)
    {
        var match = Regex.Match(
            html, @"<title[^>]*>(.*?)</title>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        if (match.Success)
        {
            return WebUtility.HtmlDecode(match.Groups[1].Value.Trim());
        }

        return null;
    }

    private static string? ExtractDomain(string url)
    {
        try
        {
            var uri = new Uri(url);
            return uri.Host;
        }
        catch
        {
            return null;
        }
    }
}
