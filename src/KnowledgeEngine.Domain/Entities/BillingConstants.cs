namespace KnowledgeEngine.Domain.Entities;

public static class BillingAccountTypes
{
    public const string Personal = "PERSONAL";
    public const string Team = "TEAM";
    public const string Enterprise = "ENTERPRISE";
    public const string Promotion = "PROMO";
    public const string Internal = "INTERNAL";
}

public static class BillingAccountStatuses
{
    public const string Active = "ACTIVE";
    public const string Suspended = "SUSPENDED";
    public const string Closed = "CLOSED";
}

public static class AiExecutionModes
{
    public const string Local = "LOCAL";
    public const string UserByok = "USER_BYOK";
    public const string MemorixCloud = "MEMORIX_CLOUD";
}

public static class AiBillingModes
{
    public const string LocalFree = "LOCAL_FREE";
    public const string LocalLicensed = "LOCAL_LICENSED";
    public const string UserByok = "USER_BYOK";
    public const string CloudIncludedQuota = "CLOUD_INCLUDED_QUOTA";
    public const string CloudPayAsYouGo = "CLOUD_PAY_AS_YOU_GO";
    public const string EnterpriseContract = "ENTERPRISE_CONTRACT";
    public const string PlatformFree = "PLATFORM_FREE";
}

public static class AiJobStatuses
{
    public const string Pending = "pending";
    public const string Reserved = "reserved";
    public const string Running = "running";
    public const string Completed = "completed";
    public const string Failed = "failed";
    public const string Cancelled = "cancelled";
}

public static class UsageTypes
{
    public const string InputToken = "INPUT_TOKEN";
    public const string OutputToken = "OUTPUT_TOKEN";
    public const string CacheReadToken = "CACHE_READ_TOKEN";
    public const string CacheWriteToken = "CACHE_WRITE_TOKEN";
    public const string ReasoningToken = "REASONING_TOKEN";
    public const string EmbeddingToken = "EMBEDDING_TOKEN";
    public const string RerankRequest = "RERANK_REQUEST";
    public const string OcrPage = "OCR_PAGE";
    public const string AudioSecond = "AUDIO_SECOND";
    public const string ImageRequest = "IMAGE_REQUEST";
    public const string StorageByteHour = "STORAGE_BYTE_HOUR";
    public const string SyncEgressByte = "SYNC_EGRESS_BYTE";
    public const string AgentCall = "AGENT_CALL";
    public const string PluginCall = "PLUGIN_CALL";
}

public static class UsageSources
{
    public const string Provider = "PROVIDER";
    public const string VerifiedGatewayTokenizer = "VERIFIED_GATEWAY_TOKENIZER";
    public const string Estimated = "ESTIMATED";
    public const string ManualAdjustment = "MANUAL_ADJUSTMENT";
}

public static class QuotaBucketSources
{
    public const string Plan = "PLAN";
    public const string TopUp = "TOP_UP";
    public const string Promotion = "PROMOTION";
    public const string EnterpriseCredit = "ENTERPRISE_CREDIT";
    public const string Manual = "MANUAL";
}

public static class ReservationStatuses
{
    public const string Active = "ACTIVE";
    public const string Consumed = "CONSUMED";
    public const string Released = "RELEASED";
    public const string Expired = "EXPIRED";
    public const string Cancelled = "CANCELLED";
}

public static class LedgerActions
{
    public const string Grant = "GRANT";
    public const string Reserve = "RESERVE";
    public const string Release = "RELEASE";
    public const string Consume = "CONSUME";
    public const string TopUp = "TOP_UP";
    public const string Refund = "REFUND";
    public const string Reversal = "REVERSAL";
    public const string Expire = "EXPIRE";
    public const string Adjust = "ADJUST";
}

public static class PriceVersionStatuses
{
    public const string Draft = "DRAFT";
    public const string Published = "PUBLISHED";
    public const string Retired = "RETIRED";
}

public static class PaymentChannels
{
    public const string WeChat = "WECHAT";
    public const string Alipay = "ALIPAY";
    public const string Fake = "FAKE";
}

public static class PaymentScenes
{
    public const string Native = "NATIVE";
    public const string Page = "PAGE";
    public const string Fake = "FAKE";
}

public static class PaymentPayloadTypes
{
    public const string QrCode = "QR_CODE";
    public const string RedirectUrl = "REDIRECT_URL";
    public const string Fake = "FAKE";
}

public static class RechargeOrderStatuses
{
    public const string Created = "CREATED";
    public const string Paying = "PAYING";
    public const string Paid = "PAID";
    public const string Closed = "CLOSED";
    public const string Failed = "FAILED";
    public const string Refunding = "REFUNDING";
    public const string PartiallyRefunded = "PARTIALLY_REFUNDED";
    public const string Refunded = "REFUNDED";
}

public static class PaymentRefundStatuses
{
    public const string Created = "CREATED";
    public const string Reviewing = "REVIEWING";
    public const string Processing = "PROCESSING";
    public const string Succeeded = "SUCCEEDED";
    public const string Failed = "FAILED";
    public const string Rejected = "REJECTED";
}
