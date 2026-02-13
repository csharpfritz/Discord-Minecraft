using Acceptance.Tests.Infrastructure;

namespace Acceptance.Tests;

/// <summary>
/// Acceptance tests that verify the full Discord → Minecraft pipeline:
/// 1. Discord events (simulated via Redis) create villages/buildings
/// 2. WorldGen.Worker processes jobs via RCON
/// 3. BlueMap markers reflect the generated structures
///
/// These tests are SLOW (minutes) because they:
/// - Launch the full Aspire stack (PostgreSQL, Redis, Minecraft, all .NET services)
/// - Wait for Minecraft/Paper to start
/// - Wait for BlueMap to render
/// - Execute actual RCON commands in Minecraft
///
/// Run with: dotnet test --filter "Category=Acceptance"
/// Or explicitly: dotnet test tests/Acceptance.Tests
/// </summary>
[Collection("FullStack")]
[Trait("Category", "Acceptance")]
public class VillageCreationTests : IClassFixture<FullStackFixture>
{
    private readonly FullStackFixture _fixture;
    private readonly BlueMapClient _blueMap;
    private readonly DiscordEventPublisher _publisher;

    /// <summary>
    /// Timeout for waiting for world generation jobs to complete.
    /// Village + building generation can take 30-60 seconds each.
    /// </summary>
    private static readonly TimeSpan JobTimeout = TimeSpan.FromMinutes(5);

    public VillageCreationTests(FullStackFixture fixture)
    {
        _fixture = fixture;
        _blueMap = new BlueMapClient(fixture.BlueMapClient);
        _publisher = new DiscordEventPublisher(fixture.Redis);
    }

    [Fact(DisplayName = "BlueMap web server is accessible")]
    public async Task BlueMap_WebServer_IsAccessible()
    {
        // Arrange & Act
        var isReady = await _blueMap.IsReadyAsync();

        // Assert
        Assert.True(isReady, "BlueMap web server should be accessible");
    }

    [Fact(DisplayName = "Creating a channel group generates a village with BlueMap marker")]
    public async Task ChannelGroup_Creates_VillageWithMarker()
    {
        // Arrange
        var groupId = $"test-village-{Guid.NewGuid():N}";
        var groupName = "Test Village Alpha";

        // Act - Publish channel group created event
        await _publisher.PublishChannelGroupCreatedAsync(groupId, groupName);

        // Wait for world generation to complete
        await _fixture.WaitForJobsToCompleteAsync(JobTimeout);

        // Give BlueMap time to update markers (it may not be instant)
        await Task.Delay(TimeSpan.FromSeconds(5));

        // Assert - Check BlueMap has the village marker
        var villages = await _blueMap.GetVillageMarkersAsync();
        var ourVillage = villages.FirstOrDefault(v => v.Label?.Contains(groupName) == true);

        Assert.NotNull(ourVillage);
        Assert.NotNull(ourVillage.Position);
    }

    [Fact(DisplayName = "Creating a channel generates a building with BlueMap marker")]
    public async Task Channel_Creates_BuildingWithMarker()
    {
        // Arrange - First create a village
        var groupId = $"test-group-{Guid.NewGuid():N}";
        var groupName = "Building Test Village";
        var channelId = $"test-channel-{Guid.NewGuid():N}";
        var channelName = "general-chat";

        await _publisher.PublishChannelGroupCreatedAsync(groupId, groupName);
        await _fixture.WaitForJobsToCompleteAsync(JobTimeout);

        // Act - Create a channel in the group
        await _publisher.PublishChannelCreatedAsync(channelId, channelName, groupId);
        await _fixture.WaitForJobsToCompleteAsync(JobTimeout);

        // Give BlueMap time to update
        await Task.Delay(TimeSpan.FromSeconds(5));

        // Assert - Check BlueMap has the building marker
        var buildings = await _blueMap.GetBuildingMarkersAsync();
        var ourBuilding = buildings.FirstOrDefault(b => b.Label?.Contains(channelName) == true);

        Assert.NotNull(ourBuilding);
        Assert.NotNull(ourBuilding.Position);
    }

    [Fact(DisplayName = "Village marker position matches grid formula")]
    public async Task Village_Position_MatchesGridFormula()
    {
        // Arrange
        var groupId = $"test-position-{Guid.NewGuid():N}";
        var groupName = "Position Test Village";

        // Act
        await _publisher.PublishChannelGroupCreatedAsync(groupId, groupName);
        await _fixture.WaitForJobsToCompleteAsync(JobTimeout);
        await Task.Delay(TimeSpan.FromSeconds(5));

        // Assert - Village should be at a grid position (multiples of 500)
        var villages = await _blueMap.GetVillageMarkersAsync();
        var ourVillage = villages.FirstOrDefault(v => v.Label?.Contains(groupName) == true);

        Assert.NotNull(ourVillage?.Position);

        // Grid positions should be multiples of 500 (VillageSpacing from WorldConstants)
        // First village is at (0, 0), second at (500, 0), etc.
        var x = ourVillage.Position.X;
        var z = ourVillage.Position.Z;

        // Positions should be multiples of 500
        Assert.True(x % 500 == 0 || Math.Abs(x % 500) < 1,
            $"Village X coordinate {x} should be a multiple of 500");
        Assert.True(z % 500 == 0 || Math.Abs(z % 500) < 1,
            $"Village Z coordinate {z} should be a multiple of 500");
    }

    [Fact(DisplayName = "Building marker is near its village center")]
    public async Task Building_Position_NearVillageCenter()
    {
        // Arrange
        var groupId = $"test-ring-{Guid.NewGuid():N}";
        var groupName = "Ring Test Village";
        var channelId = $"test-ring-ch-{Guid.NewGuid():N}";
        var channelName = "ring-channel";

        await _publisher.PublishChannelGroupCreatedAsync(groupId, groupName);
        await _fixture.WaitForJobsToCompleteAsync(JobTimeout);

        // Act
        await _publisher.PublishChannelCreatedAsync(channelId, channelName, groupId);
        await _fixture.WaitForJobsToCompleteAsync(JobTimeout);
        await Task.Delay(TimeSpan.FromSeconds(5));

        // Assert
        var villages = await _blueMap.GetVillageMarkersAsync();
        var buildings = await _blueMap.GetBuildingMarkersAsync();

        var village = villages.FirstOrDefault(v => v.Label?.Contains(groupName) == true);
        var building = buildings.FirstOrDefault(b => b.Label?.Contains(channelName) == true);

        Assert.NotNull(village?.Position);
        Assert.NotNull(building?.Position);

        // Building should be within ring radius (60 blocks) + building size of village center
        var dx = Math.Abs(building.Position.X - village.Position.X);
        var dz = Math.Abs(building.Position.Z - village.Position.Z);
        var distance = Math.Sqrt(dx * dx + dz * dz);

        // Ring radius is 60 blocks, building is 21x21, so max distance ~70-80 blocks
        Assert.True(distance < 100,
            $"Building should be within 100 blocks of village center, but was {distance:F1} blocks away");
    }

    [Fact(DisplayName = "Multiple channels create buildings at different positions")]
    public async Task Multiple_Channels_CreateDistinctBuildings()
    {
        // Arrange
        var groupId = $"test-multi-{Guid.NewGuid():N}";
        var groupName = "Multi-Building Village";

        await _publisher.PublishChannelGroupCreatedAsync(groupId, groupName);
        await _fixture.WaitForJobsToCompleteAsync(JobTimeout);

        // Act - Create 3 channels
        for (int i = 1; i <= 3; i++)
        {
            var channelId = $"test-multi-ch-{i}-{Guid.NewGuid():N}";
            await _publisher.PublishChannelCreatedAsync(channelId, $"channel-{i}", groupId, position: i);
        }
        await _fixture.WaitForJobsToCompleteAsync(JobTimeout);
        await Task.Delay(TimeSpan.FromSeconds(5));

        // Assert - Should have 3 distinct building markers
        var buildings = await _blueMap.GetBuildingMarkersAsync();
        var ourBuildings = buildings
            .Where(b => b.Label?.StartsWith("channel-") == true)
            .ToList();

        Assert.True(ourBuildings.Count >= 3, $"Expected at least 3 buildings, found {ourBuildings.Count}");

        // All buildings should be at different positions
        var positions = ourBuildings
            .Select(b => (b.Position?.X ?? 0, b.Position?.Z ?? 0))
            .Distinct()
            .ToList();

        Assert.True(positions.Count >= 3, "All 3 buildings should have distinct positions");
    }
}
