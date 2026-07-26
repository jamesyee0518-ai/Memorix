using KnowledgeEngine.Api.Services;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace KnowledgeEngine.Infrastructure.Tests;

public sealed class DesktopCapabilityServiceTests
{
    [Fact]
    public void Defaults_KeepIncompleteRuntimeModesClosed()
    {
        var service = CreateService([]);

        var capabilities = service.GetCapabilities();

        var local = capabilities.Modes.Single(mode => mode.Mode == "local");
        var cloud = capabilities.Modes.Single(mode => mode.Mode == "cloud");
        var hybrid = capabilities.Modes.Single(mode => mode.Mode == "hybrid");

        Assert.True(local.Available);
        Assert.Equal("ready", local.Status);
        Assert.False(cloud.Available);
        Assert.Equal("coming_soon", cloud.Status);
        Assert.Equal("cloud_runtime_not_ready", cloud.Reason);
        Assert.False(hybrid.Available);
        Assert.Equal("preview", hybrid.Status);
        Assert.True(capabilities.CloudInbox.Available);
        Assert.Equal("beta", capabilities.CloudInbox.Status);
    }

    [Fact]
    public void FeatureFlags_EnableOnlyExplicitlyReleasedModes()
    {
        var service = CreateService(new Dictionary<string, string?>
        {
            [DesktopCapabilityService.CloudModeFlag] = "true",
            [DesktopCapabilityService.HybridModeFlag] = "true",
            [DesktopCapabilityService.CloudInboxFlag] = "false",
            [DesktopCapabilityService.MinimumCloudApiVersionKey] = "2.1"
        });

        var capabilities = service.GetCapabilities();
        var cloud = capabilities.Modes.Single(mode => mode.Mode == "cloud");
        var hybrid = capabilities.Modes.Single(mode => mode.Mode == "hybrid");

        Assert.True(cloud.Available);
        Assert.Equal("ready", cloud.Status);
        Assert.Equal("2.1", cloud.MinimumCloudApiVersion);
        Assert.True(hybrid.Available);
        Assert.Equal("beta", hybrid.Status);
        Assert.False(capabilities.CloudInbox.Available);
    }

    [Fact]
    public void UnknownMode_IsNeverAvailable()
    {
        var service = CreateService([]);

        Assert.False(service.IsModeAvailable("unsupported"));
        Assert.Throws<ArgumentException>(() => service.GetMode("unsupported"));
    }

    private static DesktopCapabilityService CreateService(
        IEnumerable<KeyValuePair<string, string?>> values)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
        return new DesktopCapabilityService(configuration);
    }
}
