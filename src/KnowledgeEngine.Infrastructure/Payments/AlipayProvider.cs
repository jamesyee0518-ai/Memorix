using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KnowledgeEngine.Application.DTOs;
using KnowledgeEngine.Application.Exceptions;
using KnowledgeEngine.Application.Interfaces;
using KnowledgeEngine.Application.Settings;
using KnowledgeEngine.Domain.Entities;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace KnowledgeEngine.Infrastructure.Payments;

public sealed class AlipayProvider : IPaymentProvider
{
    private readonly HttpClient _httpClient;
    private readonly AlipaySettings _settings;

    public AlipayProvider(HttpClient httpClient, IOptions<PaymentSettings> settings)
    {
        _httpClient = httpClient;
        _settings = settings.Value.Alipay;
    }

    public string Channel => PaymentChannels.Alipay;

    public bool IsEnabled =>
        _settings.Enabled &&
        !string.IsNullOrWhiteSpace(_settings.AppId) &&
        !string.IsNullOrWhiteSpace(_settings.SellerId) &&
        !string.IsNullOrWhiteSpace(_settings.MerchantPrivateKeyPem) &&
        !string.IsNullOrWhiteSpace(_settings.AlipayPublicKeyPem) &&
        !string.IsNullOrWhiteSpace(_settings.NotifyUrl) &&
        !string.IsNullOrWhiteSpace(_settings.ReturnUrl);

    public Task<PaymentProviderCreateResult> CreatePaymentAsync(
        PaymentProviderOrder order,
        string scene,
        CancellationToken ct = default)
    {
        EnsureEnabled();
        if (!string.Equals(scene, PaymentScenes.Page, StringComparison.OrdinalIgnoreCase))
        {
            throw new ValidationException("paymentScene", "支付宝首版仅支持电脑网站支付。");
        }
        if (!string.Equals(order.Currency, "CNY", StringComparison.OrdinalIgnoreCase))
        {
            throw new ValidationException("currency", "支付宝首版仅支持 CNY。");
        }

        var bizContent = JsonSerializer.Serialize(new
        {
            out_trade_no = order.OrderNo,
            total_amount = FormatAmount(order.AmountMinor),
            subject = Truncate(order.Description, 256),
            product_code = "FAST_INSTANT_TRADE_PAY",
            time_expire = ToChinaTime(order.ExpiresAt)
                .ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
        });
        var parameters = CreateCommonParameters("alipay.trade.page.pay", bizContent);
        parameters["return_url"] = _settings.ReturnUrl;
        parameters["notify_url"] = _settings.NotifyUrl;
        parameters["sign"] = Sign(parameters);
        var redirectUrl = QueryHelpers.AddQueryString(_settings.GatewayUrl, parameters!);
        return Task.FromResult(new PaymentProviderCreateResult(
            RechargeOrderStatuses.Paying,
            PaymentPayloadTypes.RedirectUrl,
            redirectUrl));
    }

    public async Task<PaymentProviderStatusResult> QueryPaymentAsync(
        string orderNo,
        CancellationToken ct = default)
    {
        EnsureEnabled();
        var response = await ExecuteAsync(
            "alipay.trade.query",
            JsonSerializer.Serialize(new { out_trade_no = orderNo }),
            "alipay_trade_query_response",
            ct);
        var code = response.TryGetProperty("code", out var codeElement) ? codeElement.GetString() : null;
        if (code != "10000")
        {
            var subCode = response.TryGetProperty("sub_code", out var subCodeElement)
                ? subCodeElement.GetString()
                : code;
            if (subCode is "ACQ.TRADE_NOT_EXIST")
            {
                return new PaymentProviderStatusResult(
                    Channel,
                    orderNo,
                    RechargeOrderStatuses.Paying,
                    0,
                    "CNY",
                    null,
                    null);
            }
            throw new AppException(
                "payment_provider_error",
                response.TryGetProperty("sub_msg", out var message)
                    ? message.GetString() ?? "支付宝查单失败。"
                    : "支付宝查单失败。");
        }
        var amountMinor = ParseAmountMinor(response.GetProperty("total_amount").GetString());
        var paidAt = response.TryGetProperty("send_pay_date", out var paidAtElement)
            ? ParseChinaTime(paidAtElement.GetString())
            : null;
        return new PaymentProviderStatusResult(
            Channel,
            response.TryGetProperty("out_trade_no", out var order) ? order.GetString() ?? orderNo : orderNo,
            MapStatus(response.TryGetProperty("trade_status", out var status) ? status.GetString() : null),
            amountMinor,
            "CNY",
            response.TryGetProperty("trade_no", out var tradeNo) ? tradeNo.GetString() : null,
            paidAt);
    }

    public async Task ClosePaymentAsync(string orderNo, CancellationToken ct = default)
    {
        EnsureEnabled();
        var response = await ExecuteAsync(
            "alipay.trade.close",
            JsonSerializer.Serialize(new { out_trade_no = orderNo }),
            "alipay_trade_close_response",
            ct);
        if (!response.TryGetProperty("code", out var code) || code.GetString() != "10000")
        {
            throw new AppException(
                "payment_provider_error",
                response.TryGetProperty("sub_msg", out var message)
                    ? message.GetString() ?? "支付宝关单失败。"
                    : "支付宝关单失败。");
        }
    }

    public Task<PaymentProviderNotification> ParseNotificationAsync(
        string body,
        IReadOnlyDictionary<string, string> headers,
        CancellationToken ct = default)
    {
        EnsureEnabled();
        var parsed = QueryHelpers.ParseQuery(body.StartsWith('?') ? body : $"?{body}");
        var parameters = parsed.ToDictionary(
            x => x.Key,
            x => x.Value.ToString(),
            StringComparer.Ordinal);
        if (!parameters.Remove("sign", out var signature) || string.IsNullOrWhiteSpace(signature))
        {
            throw new AppException("payment_notification_invalid", "支付宝通知缺少签名。");
        }
        parameters.Remove("sign_type");
        var canonical = Canonicalize(parameters);
        if (!Verify(canonical, signature))
        {
            throw new AppException("payment_signature_invalid", "支付宝通知验签失败。");
        }
        if (!parameters.TryGetValue("app_id", out var appId) ||
            !string.Equals(appId, _settings.AppId, StringComparison.Ordinal) ||
            !parameters.TryGetValue("seller_id", out var sellerId) ||
            !string.Equals(sellerId, _settings.SellerId, StringComparison.Ordinal))
        {
            throw new AppException("payment_merchant_mismatch", "支付宝通知商户信息不匹配。");
        }

        var bodyHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(body)));
        var providerTradeNo = parameters.GetValueOrDefault("trade_no");
        var paidAt = ParseChinaTime(parameters.GetValueOrDefault("gmt_payment"));
        return Task.FromResult(new PaymentProviderNotification(
            Channel,
            string.IsNullOrWhiteSpace(providerTradeNo) ? bodyHash : providerTradeNo,
            parameters.GetValueOrDefault("out_trade_no") ?? string.Empty,
            MapStatus(parameters.GetValueOrDefault("trade_status")),
            ParseAmountMinor(parameters.GetValueOrDefault("total_amount")),
            "CNY",
            providerTradeNo,
            paidAt,
            bodyHash));
    }

    private async Task<JsonElement> ExecuteAsync(
        string method,
        string bizContent,
        string responseProperty,
        CancellationToken ct)
    {
        var parameters = CreateCommonParameters(method, bizContent);
        parameters["notify_url"] = _settings.NotifyUrl;
        parameters["sign"] = Sign(parameters);
        using var content = new FormUrlEncodedContent(parameters);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/x-www-form-urlencoded");
        using var response = await _httpClient.PostAsync(_settings.GatewayUrl, content, ct);
        var responseBody = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new AppException(
                "payment_provider_error",
                $"支付宝请求失败（HTTP {(int)response.StatusCode}）：{Truncate(responseBody, 300)}");
        }
        using var document = JsonDocument.Parse(responseBody);
        if (!document.RootElement.TryGetProperty(responseProperty, out var responseElement))
        {
            throw new AppException("payment_provider_invalid_response", "支付宝返回结构无效。");
        }
        if (!document.RootElement.TryGetProperty("sign", out var signElement) ||
            !Verify(responseElement.GetRawText(), signElement.GetString() ?? string.Empty))
        {
            throw new AppException("payment_signature_invalid", "支付宝响应验签失败。");
        }
        return responseElement.Clone();
    }

    private SortedDictionary<string, string> CreateCommonParameters(string method, string bizContent) =>
        new(StringComparer.Ordinal)
        {
            ["app_id"] = _settings.AppId,
            ["method"] = method,
            ["format"] = "JSON",
            ["charset"] = "utf-8",
            ["sign_type"] = "RSA2",
            ["timestamp"] = ToChinaTime(DateTime.UtcNow)
                .ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            ["version"] = "1.0",
            ["biz_content"] = bizContent
        };

    private string Sign(IReadOnlyDictionary<string, string> parameters)
    {
        using var rsa = RSA.Create();
        rsa.ImportFromPem(_settings.MerchantPrivateKeyPem);
        var bytes = rsa.SignData(
            Encoding.UTF8.GetBytes(Canonicalize(parameters)),
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        return Convert.ToBase64String(bytes);
    }

    private bool Verify(string content, string signature)
    {
        try
        {
            using var rsa = RSA.Create();
            rsa.ImportFromPem(_settings.AlipayPublicKeyPem);
            return rsa.VerifyData(
                Encoding.UTF8.GetBytes(content),
                Convert.FromBase64String(signature),
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static string Canonicalize(IReadOnlyDictionary<string, string> parameters) =>
        string.Join(
            "&",
            parameters
                .Where(x =>
                    !string.Equals(x.Key, "sign", StringComparison.Ordinal) &&
                    !string.Equals(x.Key, "sign_type", StringComparison.Ordinal) &&
                    !string.IsNullOrEmpty(x.Value))
                .OrderBy(x => x.Key, StringComparer.Ordinal)
                .Select(x => $"{x.Key}={x.Value}"));

    private static string MapStatus(string? status) => status switch
    {
        "TRADE_SUCCESS" or "TRADE_FINISHED" => RechargeOrderStatuses.Paid,
        "TRADE_CLOSED" => RechargeOrderStatuses.Closed,
        _ => RechargeOrderStatuses.Paying
    };

    private static string FormatAmount(long amountMinor) =>
        (amountMinor / 100m).ToString("0.00", CultureInfo.InvariantCulture);

    private static DateTime? ParseChinaTime(string? value)
    {
        if (!DateTime.TryParseExact(
                value,
                "yyyy-MM-dd HH:mm:ss",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsed))
        {
            return null;
        }
        return new DateTimeOffset(
            DateTime.SpecifyKind(parsed, DateTimeKind.Unspecified),
            TimeSpan.FromHours(8)).UtcDateTime;
    }

    private static DateTime ToChinaTime(DateTime utc) =>
        DateTime.SpecifyKind(utc, DateTimeKind.Utc).AddHours(8);

    private static long ParseAmountMinor(string? amount)
    {
        if (!decimal.TryParse(amount, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
        {
            return 0;
        }
        return checked((long)Math.Round(parsed * 100m, MidpointRounding.AwayFromZero));
    }

    private void EnsureEnabled()
    {
        if (!IsEnabled)
        {
            throw new AppException("payment_channel_unavailable", "支付宝配置不完整或未启用。");
        }
    }

    private static string Truncate(string value, int length) =>
        value.Length <= length ? value : value[..length];
}
