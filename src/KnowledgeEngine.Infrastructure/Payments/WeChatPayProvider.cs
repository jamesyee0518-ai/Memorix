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
using Microsoft.Extensions.Options;

namespace KnowledgeEngine.Infrastructure.Payments;

public sealed class WeChatPayProvider : IPaymentProvider
{
    private readonly HttpClient _httpClient;
    private readonly WeChatPaySettings _settings;

    public WeChatPayProvider(HttpClient httpClient, IOptions<PaymentSettings> settings)
    {
        _httpClient = httpClient;
        _settings = settings.Value.WeChat;
    }

    public string Channel => PaymentChannels.WeChat;

    public bool IsEnabled =>
        _settings.Enabled &&
        !string.IsNullOrWhiteSpace(_settings.AppId) &&
        !string.IsNullOrWhiteSpace(_settings.MerchantId) &&
        !string.IsNullOrWhiteSpace(_settings.MerchantCertificateSerialNumber) &&
        !string.IsNullOrWhiteSpace(_settings.MerchantPrivateKeyPem) &&
        !string.IsNullOrWhiteSpace(_settings.ApiV3Key) &&
        Encoding.UTF8.GetByteCount(_settings.ApiV3Key) == 32 &&
        !string.IsNullOrWhiteSpace(_settings.WeChatPayPublicKeyId) &&
        !string.IsNullOrWhiteSpace(_settings.WeChatPayPublicKeyPem) &&
        !string.IsNullOrWhiteSpace(_settings.NotifyUrl);

    public async Task<PaymentProviderCreateResult> CreatePaymentAsync(
        PaymentProviderOrder order,
        string scene,
        CancellationToken ct = default)
    {
        EnsureEnabled();
        if (!string.Equals(scene, PaymentScenes.Native, StringComparison.OrdinalIgnoreCase))
        {
            throw new ValidationException("paymentScene", "微信支付首版仅支持 Native 二维码。");
        }
        if (!string.Equals(order.Currency, "CNY", StringComparison.OrdinalIgnoreCase))
        {
            throw new ValidationException("currency", "微信支付首版仅支持 CNY。");
        }

        var body = JsonSerializer.Serialize(new
        {
            appid = _settings.AppId,
            mchid = _settings.MerchantId,
            description = Truncate(order.Description, 127),
            out_trade_no = order.OrderNo,
            time_expire = order.ExpiresAt.ToString("yyyy-MM-dd'T'HH:mm:ssK", CultureInfo.InvariantCulture),
            notify_url = _settings.NotifyUrl,
            amount = new { total = order.AmountMinor, currency = order.Currency }
        });
        using var response = await SendAsync(
            HttpMethod.Post,
            "/v3/pay/transactions/native",
            body,
            ct);
        var responseBody = await response.Content.ReadAsStringAsync(ct);
        EnsureSuccess(response, responseBody);
        using var json = JsonDocument.Parse(responseBody);
        var codeUrl = json.RootElement.GetProperty("code_url").GetString();
        if (string.IsNullOrWhiteSpace(codeUrl))
        {
            throw new AppException("payment_provider_invalid_response", "微信支付未返回二维码链接。");
        }
        return new PaymentProviderCreateResult(
            RechargeOrderStatuses.Paying,
            PaymentPayloadTypes.QrCode,
            codeUrl,
            GetHeader(response, "Request-ID"));
    }

    public async Task<PaymentProviderStatusResult> QueryPaymentAsync(
        string orderNo,
        CancellationToken ct = default)
    {
        EnsureEnabled();
        var path = $"/v3/pay/transactions/out-trade-no/{Uri.EscapeDataString(orderNo)}" +
                   $"?mchid={Uri.EscapeDataString(_settings.MerchantId)}";
        using var response = await SendAsync(HttpMethod.Get, path, string.Empty, ct);
        var responseBody = await response.Content.ReadAsStringAsync(ct);
        EnsureSuccess(response, responseBody);
        using var json = JsonDocument.Parse(responseBody);
        var root = json.RootElement;
        var amount = root.GetProperty("amount");
        return new PaymentProviderStatusResult(
            Channel,
            root.GetProperty("out_trade_no").GetString() ?? orderNo,
            MapStatus(root.GetProperty("trade_state").GetString()),
            amount.GetProperty("total").GetInt64(),
            amount.TryGetProperty("currency", out var currency) ? currency.GetString() ?? "CNY" : "CNY",
            root.TryGetProperty("transaction_id", out var trade) ? trade.GetString() : null,
            root.TryGetProperty("success_time", out var paidAt) && DateTime.TryParse(
                paidAt.GetString(),
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var parsedPaidAt)
                ? parsedPaidAt.ToUniversalTime()
                : null);
    }

    public async Task ClosePaymentAsync(string orderNo, CancellationToken ct = default)
    {
        EnsureEnabled();
        var path = $"/v3/pay/transactions/out-trade-no/{Uri.EscapeDataString(orderNo)}/close";
        var body = JsonSerializer.Serialize(new { mchid = _settings.MerchantId });
        using var response = await SendAsync(HttpMethod.Post, path, body, ct);
        var responseBody = await response.Content.ReadAsStringAsync(ct);
        EnsureSuccess(response, responseBody);
    }

    public Task<PaymentProviderNotification> ParseNotificationAsync(
        string body,
        IReadOnlyDictionary<string, string> headers,
        CancellationToken ct = default)
    {
        EnsureEnabled();
        var timestamp = RequiredHeader(headers, "Wechatpay-Timestamp");
        var nonce = RequiredHeader(headers, "Wechatpay-Nonce");
        var signature = RequiredHeader(headers, "Wechatpay-Signature");
        var serial = RequiredHeader(headers, "Wechatpay-Serial");
        if (!string.IsNullOrWhiteSpace(_settings.WeChatPayPublicKeyId) &&
            !string.Equals(serial, _settings.WeChatPayPublicKeyId, StringComparison.OrdinalIgnoreCase))
        {
            throw new AppException("payment_signature_invalid", "微信支付公钥序列号不匹配。");
        }
        if (!long.TryParse(timestamp, out var timestampSeconds) ||
            Math.Abs(DateTimeOffset.UtcNow.ToUnixTimeSeconds() - timestampSeconds) > 300)
        {
            throw new AppException("payment_signature_expired", "微信支付通知时间戳超出允许范围。");
        }

        var message = $"{timestamp}\n{nonce}\n{body}\n";
        using var rsa = RSA.Create();
        rsa.ImportFromPem(_settings.WeChatPayPublicKeyPem);
        if (!rsa.VerifyData(
                Encoding.UTF8.GetBytes(message),
                Convert.FromBase64String(signature),
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1))
        {
            throw new AppException("payment_signature_invalid", "微信支付通知验签失败。");
        }

        using var envelope = JsonDocument.Parse(body);
        var root = envelope.RootElement;
        var eventId = root.TryGetProperty("id", out var id) ? id.GetString() : null;
        var resource = root.GetProperty("resource");
        var decrypted = DecryptResource(
            resource.GetProperty("ciphertext").GetString() ?? string.Empty,
            resource.GetProperty("nonce").GetString() ?? string.Empty,
            resource.TryGetProperty("associated_data", out var associatedData)
                ? associatedData.GetString() ?? string.Empty
                : string.Empty);
        using var transaction = JsonDocument.Parse(decrypted);
        var tx = transaction.RootElement;
        ValidateMerchant(tx);
        var amount = tx.GetProperty("amount");
        var orderNo = tx.GetProperty("out_trade_no").GetString() ?? string.Empty;
        var providerTradeNo = tx.TryGetProperty("transaction_id", out var trade)
            ? trade.GetString()
            : null;
        var status = MapStatus(tx.GetProperty("trade_state").GetString());
        DateTime? paidAt = tx.TryGetProperty("success_time", out var paidAtElement) &&
                     DateTime.TryParse(
                         paidAtElement.GetString(),
                         CultureInfo.InvariantCulture,
                         DateTimeStyles.RoundtripKind,
                         out var parsed)
            ? parsed.ToUniversalTime()
            : null;
        var bodyHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(body)));
        return Task.FromResult(new PaymentProviderNotification(
            Channel,
            string.IsNullOrWhiteSpace(eventId) ? bodyHash : eventId,
            orderNo,
            status,
            amount.GetProperty("total").GetInt64(),
            amount.TryGetProperty("currency", out var currency) ? currency.GetString() ?? "CNY" : "CNY",
            providerTradeNo,
            paidAt,
            bodyHash));
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string path,
        string body,
        CancellationToken ct)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var nonce = RandomNumberGenerator.GetHexString(16).ToLowerInvariant();
        var message = $"{method.Method}\n{path}\n{timestamp}\n{nonce}\n{body}\n";
        using var rsa = RSA.Create();
        rsa.ImportFromPem(_settings.MerchantPrivateKeyPem);
        var signature = Convert.ToBase64String(rsa.SignData(
            Encoding.UTF8.GetBytes(message),
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1));
        var authorization =
            $"WECHATPAY2-SHA256-RSA2048 mchid=\"{_settings.MerchantId}\"," +
            $"nonce_str=\"{nonce}\",timestamp=\"{timestamp}\"," +
            $"serial_no=\"{_settings.MerchantCertificateSerialNumber}\",signature=\"{signature}\"";
        var request = new HttpRequestMessage(method, $"{_settings.ApiBaseUrl.TrimEnd('/')}{path}");
        request.Headers.TryAddWithoutValidation("Authorization", authorization);
        request.Headers.TryAddWithoutValidation(
            "Wechatpay-Serial",
            _settings.WeChatPayPublicKeyId);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (!string.IsNullOrEmpty(body))
        {
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }
        return await _httpClient.SendAsync(request, ct);
    }

    private string DecryptResource(string ciphertext, string nonce, string associatedData)
    {
        var combined = Convert.FromBase64String(ciphertext);
        if (combined.Length <= 16)
        {
            throw new AppException("payment_notification_invalid", "微信支付通知密文无效。");
        }
        var cipher = combined.AsSpan(0, combined.Length - 16);
        var tag = combined.AsSpan(combined.Length - 16, 16);
        var plaintext = new byte[cipher.Length];
        using var aes = new AesGcm(Encoding.UTF8.GetBytes(_settings.ApiV3Key), 16);
        aes.Decrypt(
            Encoding.UTF8.GetBytes(nonce),
            cipher,
            tag,
            plaintext,
            Encoding.UTF8.GetBytes(associatedData));
        return Encoding.UTF8.GetString(plaintext);
    }

    private void ValidateMerchant(JsonElement transaction)
    {
        var merchantId = transaction.TryGetProperty("mchid", out var mchid) ? mchid.GetString() : null;
        var appId = transaction.TryGetProperty("appid", out var appid) ? appid.GetString() : null;
        if (!string.Equals(merchantId, _settings.MerchantId, StringComparison.Ordinal) ||
            !string.Equals(appId, _settings.AppId, StringComparison.Ordinal))
        {
            throw new AppException("payment_merchant_mismatch", "微信支付通知商户信息不匹配。");
        }
    }

    private static string MapStatus(string? status) => status switch
    {
        "SUCCESS" => RechargeOrderStatuses.Paid,
        "CLOSED" or "REVOKED" or "PAYERROR" => RechargeOrderStatuses.Closed,
        _ => RechargeOrderStatuses.Paying
    };

    private static string RequiredHeader(IReadOnlyDictionary<string, string> headers, string name)
    {
        if (headers.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value))
        {
            return value;
        }
        var pair = headers.FirstOrDefault(x => string.Equals(x.Key, name, StringComparison.OrdinalIgnoreCase));
        return !string.IsNullOrWhiteSpace(pair.Value)
            ? pair.Value
            : throw new AppException("payment_notification_invalid", $"缺少微信支付请求头 {name}。");
    }

    private static string? GetHeader(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) ? values.FirstOrDefault() : null;

    private static void EnsureSuccess(HttpResponseMessage response, string body)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }
        throw new AppException(
            "payment_provider_error",
            $"微信支付请求失败（HTTP {(int)response.StatusCode}）：{Truncate(body, 300)}");
    }

    private void EnsureEnabled()
    {
        if (!IsEnabled)
        {
            throw new AppException("payment_channel_unavailable", "微信支付配置不完整或未启用。");
        }
    }

    private static string Truncate(string value, int length) =>
        value.Length <= length ? value : value[..length];
}
