using KnowledgeEngine.Api.Services;
using Xunit;

namespace KnowledgeEngine.Infrastructure.Tests;

public class DesktopRuntimeCoordinatorTests
{
    [Fact]
    public void Advance_CancelsPreviousGenerationOnly()
    {
        using var coordinator = new DesktopRuntimeCoordinator();
        var previous = coordinator.ModeChangedToken;

        coordinator.Advance();

        Assert.True(previous.IsCancellationRequested);
        Assert.False(coordinator.ModeChangedToken.IsCancellationRequested);
        Assert.Equal(1, coordinator.Generation);
    }
}
