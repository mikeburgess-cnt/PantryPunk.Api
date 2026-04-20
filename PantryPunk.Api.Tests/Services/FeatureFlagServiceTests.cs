using Microsoft.FeatureManagement;
using Moq;
using PantryPunk.Api.Services;

namespace PantryPunk.Api.Tests.Services;

public class FeatureFlagServiceTests
{
    private readonly Mock<IFeatureManager> _featureManager = new();
    private readonly FeatureFlagService _sut;

    public FeatureFlagServiceTests()
    {
        _sut = new FeatureFlagService(_featureManager.Object);
    }

    [Fact]
    public async Task GetFlagsAsync_ReturnsAllKnownFlags()
    {
        _featureManager.Setup(f => f.IsEnabledAsync("TalkIt", It.IsAny<object>())).ReturnsAsync(true);
        _featureManager.Setup(f => f.IsEnabledAsync("RealtimeSync", It.IsAny<object>())).ReturnsAsync(false);
        _featureManager.Setup(f => f.IsEnabledAsync("AnnualSubscription", It.IsAny<object>())).ReturnsAsync(true);
        _featureManager.Setup(f => f.IsEnabledAsync("AppAttest", It.IsAny<object>())).ReturnsAsync(false);

        var result = await _sut.GetFlagsAsync("auth0|abc", true);

        Assert.Equal(4, result.Count);
        Assert.True(result["talkIt"]);
        Assert.False(result["realtimeSync"]);
        Assert.True(result["annualSubscription"]);
        Assert.False(result["appAttest"]);
    }

    [Fact]
    public async Task GetFlagsAsync_UsesCamelCaseKeys()
    {
        _featureManager.Setup(f => f.IsEnabledAsync(It.IsAny<string>(), It.IsAny<object>())).ReturnsAsync(false);

        var result = await _sut.GetFlagsAsync("auth0|abc", false);

        Assert.All(result.Keys, key => Assert.Equal(char.ToLowerInvariant(key[0]), key[0]));
    }
}
