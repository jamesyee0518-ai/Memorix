namespace KnowledgeEngine.Application.Settings;

public class PaymentSettings
{
    public bool Enabled { get; set; }
    public int OrderTtlMinutes { get; set; } = 15;
    public int RecoveryIntervalSeconds { get; set; } = 30;
    public int RecoveryBatchSize { get; set; } = 50;
    public List<RechargeProductSettings> Products { get; set; } = [];
    public FakePaymentSettings Fake { get; set; } = new();
    public WeChatPaySettings WeChat { get; set; } = new();
    public AlipaySettings Alipay { get; set; } = new();
}

public class RechargeProductSettings
{
    public string Code { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Currency { get; set; } = "CNY";
    public long AmountMinor { get; set; }
    public decimal PaidCredits { get; set; }
    public decimal BonusCredits { get; set; }
    public int? BonusExpiresInDays { get; set; }
    public bool Enabled { get; set; } = true;
    public int SortOrder { get; set; }
}

public class FakePaymentSettings
{
    public bool Enabled { get; set; }
}

public class WeChatPaySettings
{
    public bool Enabled { get; set; }
    public string AppId { get; set; } = string.Empty;
    public string MerchantId { get; set; } = string.Empty;
    public string MerchantCertificateSerialNumber { get; set; } = string.Empty;
    public string MerchantPrivateKeyPem { get; set; } = string.Empty;
    public string ApiV3Key { get; set; } = string.Empty;
    public string WeChatPayPublicKeyId { get; set; } = string.Empty;
    public string WeChatPayPublicKeyPem { get; set; } = string.Empty;
    public string NotifyUrl { get; set; } = string.Empty;
    public string ApiBaseUrl { get; set; } = "https://api.mch.weixin.qq.com";
}

public class AlipaySettings
{
    public bool Enabled { get; set; }
    public string AppId { get; set; } = string.Empty;
    public string SellerId { get; set; } = string.Empty;
    public string MerchantPrivateKeyPem { get; set; } = string.Empty;
    public string AlipayPublicKeyPem { get; set; } = string.Empty;
    public string NotifyUrl { get; set; } = string.Empty;
    public string ReturnUrl { get; set; } = string.Empty;
    public string GatewayUrl { get; set; } = "https://openapi.alipay.com/gateway.do";
}
