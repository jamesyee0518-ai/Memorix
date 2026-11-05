namespace KnowledgeEngine.Api.Security;

/// <summary>
/// Rejects committed placeholder credentials outside Development.
/// Development may omit a JWT secret; a process-local fallback is used only then.
/// </summary>
public static class StartupSecrets
{
    public const string HistoricalJwtSecret =
        "KnowledgeEngineSuperSecretKey2024ChangeMePleaseMakeItAtLeast32Bytes!";

    public const string DevelopmentOnlyJwtSecret =
        "dev-only-insecure-jwt-signing-key-do-not-deploy";

    public const string HistoricalDbPassword = "ke_password";
    public const string HistoricalMinioCredential = "minioadmin";

    public static void Apply(WebApplicationBuilder builder)
    {
        if (builder.Environment.IsDevelopment() &&
            string.IsNullOrWhiteSpace(builder.Configuration["Jwt:Secret"]))
        {
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Secret"] = DevelopmentOnlyJwtSecret
            });
        }

        var errors = CollectErrors(
            builder.Environment.IsDevelopment(),
            builder.Configuration["DatabaseProvider"],
            builder.Configuration["Jwt:Secret"],
            builder.Configuration.GetConnectionString("DefaultConnection"),
            builder.Configuration["Minio:AccessKey"],
            builder.Configuration["Minio:SecretKey"]);

        if (errors.Count > 0)
        {
            throw new InvalidOperationException(
                "Refusing to start because required secrets are missing or still use a known placeholder. " +
                string.Join(" ", errors));
        }
    }

    public static IReadOnlyList<string> CollectErrors(
        bool isDevelopment,
        string? databaseProvider,
        string? jwtSecret,
        string? connectionString,
        string? minioAccessKey,
        string? minioSecretKey)
    {
        if (isDevelopment)
        {
            return [];
        }

        var errors = new List<string>();
        if (IsPlaceholderJwt(jwtSecret))
        {
            errors.Add(
                "Set Jwt:Secret (Jwt__Secret) to a unique value of at least 32 bytes. " +
                "Do not reuse the historical repository placeholder.");
        }

        var sqlite = string.Equals(databaseProvider, "sqlite", StringComparison.OrdinalIgnoreCase);
        if (!sqlite)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                errors.Add(
                    "Set ConnectionStrings:DefaultConnection (ConnectionStrings__DefaultConnection) for PostgreSQL.");
            }
            else if (ContainsHistoricalPassword(connectionString))
            {
                errors.Add(
                    "ConnectionStrings:DefaultConnection still uses the historical password ke_password.");
            }

            if (IsPlaceholderMinio(minioAccessKey) || IsPlaceholderMinio(minioSecretKey))
            {
                errors.Add(
                    "Set Minio:AccessKey and Minio:SecretKey (Minio__AccessKey, Minio__SecretKey). " +
                    "Do not use minioadmin.");
            }
        }

        return errors;
    }

    private static bool IsPlaceholderJwt(string? secret)
    {
        if (string.IsNullOrWhiteSpace(secret) || EncodingLength(secret) < 32)
        {
            return true;
        }

        return string.Equals(secret, HistoricalJwtSecret, StringComparison.Ordinal) ||
               string.Equals(secret, DevelopmentOnlyJwtSecret, StringComparison.Ordinal);
    }

    private static int EncodingLength(string secret) =>
        System.Text.Encoding.UTF8.GetByteCount(secret);

    private static bool ContainsHistoricalPassword(string connectionString)
    {
        foreach (var part in connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = part.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            var key = part[..separator].Trim();
            var value = part[(separator + 1)..].Trim();
            if (key.Equals("Password", StringComparison.OrdinalIgnoreCase) &&
                value.Equals(HistoricalDbPassword, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsPlaceholderMinio(string? value) =>
        string.IsNullOrWhiteSpace(value) ||
        value.Equals(HistoricalMinioCredential, StringComparison.OrdinalIgnoreCase);
}
