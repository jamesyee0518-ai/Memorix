using KnowledgeEngine.Api.Security;
using Xunit;

namespace KnowledgeEngine.Infrastructure.Tests;

public class StartupSecretsTests
{
    [Fact]
    public void Production_RejectsMissingAndHistoricalSecrets()
    {
        var missing = StartupSecrets.CollectErrors(false, "postgres", null, null, null, null);
        Assert.Contains(missing, error => error.Contains("Jwt:Secret", StringComparison.Ordinal));
        Assert.Contains(missing, error => error.Contains("DefaultConnection", StringComparison.Ordinal));
        Assert.Contains(missing, error => error.Contains("Minio", StringComparison.Ordinal));

        var historical = StartupSecrets.CollectErrors(
            false,
            "postgres",
            StartupSecrets.HistoricalJwtSecret,
            "Host=localhost;Password=ke_password",
            "minioadmin",
            "minioadmin");
        Assert.Contains(historical, error => error.Contains("Jwt:Secret", StringComparison.Ordinal));
        Assert.Contains(historical, error => error.Contains("ke_password", StringComparison.Ordinal));
        Assert.Contains(historical, error => error.Contains("minioadmin", StringComparison.Ordinal));
    }

    [Fact]
    public void Production_AcceptsUniqueSecrets_AndSqliteSkipsCloudCredentials()
    {
        var cloud = StartupSecrets.CollectErrors(
            false,
            "postgres",
            new string('k', 32),
            "Host=localhost;Username=ke_user;Password=local-only",
            "access",
            "secret");
        Assert.Empty(cloud);

        var sqlite = StartupSecrets.CollectErrors(false, "sqlite", new string('k', 32), null, null, null);
        Assert.Empty(sqlite);
    }

    [Fact]
    public void Development_DoesNotFailOnEmptySecrets()
    {
        var errors = StartupSecrets.CollectErrors(true, "postgres", null, null, null, null);
        Assert.Empty(errors);
    }
}
