namespace KnowledgeEngine.Application.Settings;

public class JwtSettings
{
    public string Secret { get; set; } = string.Empty;
    public int ExpiresMinutes { get; set; } = 1440;
    public int MobileDeviceExpiresMinutes { get; set; } = 43200;
    public int MobileDeviceRefreshExpiresDays { get; set; } = 90;
    public string Issuer { get; set; } = "KnowledgeEngine";
    public string Audience { get; set; } = "KnowledgeEngine";
}

public class MinioSettings
{
    public string Endpoint { get; set; } = "localhost:9000";
    public string AccessKey { get; set; } = string.Empty;
    public string SecretKey { get; set; } = string.Empty;
    public string Bucket { get; set; } = "knowledge-engine";
    public bool UseSsl { get; set; } = false;
}

public class CorsSettings
{
    public List<string> AllowedOrigins { get; set; } = new();
}
