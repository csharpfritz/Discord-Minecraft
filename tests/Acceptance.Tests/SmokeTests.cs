using Acceptance.Tests.Infrastructure;
using System.Net.Http.Json;

namespace Acceptance.Tests;

/// <summary>
/// Smoke tests that verify basic system health without full world generation.
/// These are faster than full acceptance tests but still test the full stack.
/// </summary>
[Collection("FullStack")]
[Trait("Category", "Acceptance")]
[Trait("Subcategory", "Smoke")]
public class SmokeTests : IClassFixture<FullStackFixture>
{
    private readonly FullStackFixture _fixture;
    private readonly BlueMapClient _blueMap;

    public SmokeTests(FullStackFixture fixture)
    {
        _fixture = fixture;
        _blueMap = new BlueMapClient(fixture.BlueMapClient);
    }

    [Fact(DisplayName = "Bridge API is healthy")]
    public async Task BridgeApi_IsHealthy()
    {
        // Act
        var response = await _fixture.BridgeApiClient.GetAsync("/health");

        // Assert
        response.EnsureSuccessStatusCode();
    }

    [Fact(DisplayName = "Bridge API villages endpoint responds")]
    public async Task BridgeApi_VillagesEndpoint_Responds()
    {
        // Act
        var response = await _fixture.BridgeApiClient.GetAsync("/api/villages");

        // Assert
        response.EnsureSuccessStatusCode();
    }

    [Fact(DisplayName = "Redis connection is alive")]
    public async Task Redis_IsConnected()
    {
        // Arrange
        var db = _fixture.Redis.GetDatabase();

        // Act
        var pong = await db.PingAsync();

        // Assert
        Assert.True(pong.TotalMilliseconds < 1000, "Redis ping should be fast");
    }

    [Fact(DisplayName = "BlueMap serves static assets")]
    public async Task BlueMap_ServesStaticAssets()
    {
        // Act - BlueMap serves its web app at root
        var response = await _fixture.BlueMapClient.GetAsync("/");

        // Assert
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("BlueMap", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(DisplayName = "WorldGen queue is accessible")]
    public async Task WorldGenQueue_IsAccessible()
    {
        // Arrange
        var db = _fixture.Redis.GetDatabase();

        // Act
        var length = await db.ListLengthAsync("queue:worldgen");

        // Assert - Queue should exist and have a length (even if 0)
        Assert.True(length >= 0, "WorldGen queue should be accessible");
    }

    [Fact(DisplayName = "Full stack startup completes within timeout")]
    public void FullStack_StartsSuccessfully()
    {
        // If we get here, the fixture initialized successfully
        // This is a meta-test that the fixture works
        Assert.NotNull(_fixture.BridgeApiClient);
        Assert.NotNull(_fixture.BlueMapClient);
        Assert.NotNull(_fixture.Redis);
        Assert.False(string.IsNullOrEmpty(_fixture.BridgeApiBaseUrl));
        Assert.False(string.IsNullOrEmpty(_fixture.BlueMapBaseUrl));
    }
}
