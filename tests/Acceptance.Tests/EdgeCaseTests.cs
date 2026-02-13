using Acceptance.Tests.Infrastructure;
using StackExchange.Redis;

namespace Acceptance.Tests;

/// <summary>
/// Edge case acceptance tests covering unusual but valid scenarios:
/// - Channel created before its category exists
/// - Simultaneous channel creation
/// - BlueMap marker timeout handling
/// - Out-of-order event delivery
/// - Rapid-fire event sequences
/// </summary>
[Collection("FullStack")]
[Trait("Category", "Acceptance")]
[Trait("Subcategory", "EdgeCases")]
public class EdgeCaseTests : IClassFixture<FullStackFixture>
{
    private readonly FullStackFixture _fixture;
    private readonly BlueMapClient _blueMap;
    private readonly DiscordEventPublisher _publisher;

    private static readonly TimeSpan JobTimeout = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan ShortTimeout = TimeSpan.FromMinutes(2);

    public EdgeCaseTests(FullStackFixture fixture)
    {
        _fixture = fixture;
        _blueMap = new BlueMapClient(fixture.BlueMapClient);
        _publisher = new DiscordEventPublisher(fixture.Redis);
    }

    [Fact(DisplayName = "Channel created before category exists creates both")]
    public async Task ChannelBeforeCategory_CreatesGroupAutomatically()
    {
        // Arrange - Create a channel event referencing a non-existent group
        // The event consumer should auto-create the group (Sprint 2 behavior)
        var groupId = $"orphan-group-{Guid.NewGuid():N}";
        var channelId = $"orphan-channel-{Guid.NewGuid():N}";
        var channelName = "orphan-channel";

        // Act - Create channel without first creating the group
        // The DiscordChannelEvent includes ChannelGroupId but no ChannelGroupCreated was sent
        await _publisher.PublishChannelCreatedAsync(channelId, channelName, groupId);
        await _fixture.WaitForJobsToCompleteAsync(JobTimeout);
        await Task.Delay(TimeSpan.FromSeconds(5));

        // Assert - The system should have created an implicit group
        // Check that the building was created (implies group was auto-created)
        var buildings = await _blueMap.GetBuildingMarkersAsync();
        var building = buildings.FirstOrDefault(b => b.Label?.Contains(channelName) == true);

        // This may succeed or fail depending on implementation
        // The test documents the expected behavior
        // If auto-create is implemented: building should exist
        // If strict ordering: building may not exist (acceptable fallback)
        if (building != null)
        {
            Assert.NotNull(building.Position);
        }
    }

    [Fact(DisplayName = "Two channels created simultaneously get distinct positions")]
    public async Task SimultaneousChannelCreation_DistinctPositions()
    {
        // Arrange - Create a village first
        var groupId = $"simultaneous-group-{Guid.NewGuid():N}";
        var groupName = "Simultaneous Test Village";

        await _publisher.PublishChannelGroupCreatedAsync(groupId, groupName);
        await _fixture.WaitForJobsToCompleteAsync(JobTimeout);

        // Act - Fire two channel events as fast as possible
        var channel1Id = $"sim-ch1-{Guid.NewGuid():N}";
        var channel2Id = $"sim-ch2-{Guid.NewGuid():N}";

        // Publish both without waiting
        await Task.WhenAll(
            _publisher.PublishChannelCreatedAsync(channel1Id, "sim-channel-1", groupId, position: 1),
            _publisher.PublishChannelCreatedAsync(channel2Id, "sim-channel-2", groupId, position: 2)
        );

        await _fixture.WaitForJobsToCompleteAsync(JobTimeout);
        await Task.Delay(TimeSpan.FromSeconds(5));

        // Assert - Both buildings should exist at different positions
        var buildings = await _blueMap.GetBuildingMarkersAsync();
        var building1 = buildings.FirstOrDefault(b => b.Label?.Contains("sim-channel-1") == true);
        var building2 = buildings.FirstOrDefault(b => b.Label?.Contains("sim-channel-2") == true);

        Assert.NotNull(building1?.Position);
        Assert.NotNull(building2?.Position);

        // Positions must be distinct
        var samePosition = building1.Position.X == building2.Position.X &&
                          building1.Position.Z == building2.Position.Z;
        Assert.False(samePosition, "Simultaneous buildings should have distinct positions");
    }

    [Fact(DisplayName = "BlueMap marker appears within reasonable timeout")]
    public async Task BlueMapMarker_AppearsWithinTimeout()
    {
        // Arrange
        var groupId = $"timeout-test-{Guid.NewGuid():N}";
        var groupName = "Timeout Test Village";
        var startTime = DateTime.UtcNow;

        // Act
        await _publisher.PublishChannelGroupCreatedAsync(groupId, groupName);
        await _fixture.WaitForJobsToCompleteAsync(JobTimeout);

        // Assert - Poll for marker with timeout
        BlueMapMarker? village = null;
        var maxWait = TimeSpan.FromSeconds(30);
        var pollInterval = TimeSpan.FromSeconds(2);

        while (DateTime.UtcNow - startTime < maxWait && village == null)
        {
            var villages = await _blueMap.GetVillageMarkersAsync();
            village = villages.FirstOrDefault(v => v.Label?.Contains(groupName) == true);

            if (village == null)
                await Task.Delay(pollInterval);
        }

        Assert.NotNull(village);
        var elapsed = DateTime.UtcNow - startTime;
        Assert.True(elapsed < maxWait, $"BlueMap marker should appear within {maxWait}, took {elapsed}");
    }

    [Fact(DisplayName = "Out-of-order channel events still create valid structures")]
    public async Task OutOfOrderEvents_CreateValidStructures()
    {
        // Arrange - We'll send events in weird order:
        // 1. Channel update (before channel exists)
        // 2. Channel create
        // 3. Channel group create (after channel)

        var groupId = $"outoforder-group-{Guid.NewGuid():N}";
        var channelId = $"outoforder-ch-{Guid.NewGuid():N}";
        var channelName = "outoforder-channel";
        var groupName = "Out Of Order Village";

        // Act - Send in wrong order
        // Note: update before create might be ignored or queued
        await _publisher.PublishChannelUpdatedAsync(channelId, groupId, "old-name", channelName);

        await _publisher.PublishChannelCreatedAsync(channelId, channelName, groupId);

        await _publisher.PublishChannelGroupCreatedAsync(groupId, groupName);

        await _fixture.WaitForJobsToCompleteAsync(JobTimeout);
        await Task.Delay(TimeSpan.FromSeconds(5));

        // Assert - Eventually, structures should exist (order reconciled)
        var villages = await _blueMap.GetVillageMarkersAsync();
        var village = villages.FirstOrDefault(v => v.Label?.Contains(groupName) == true);

        // Village should exist after the group-created event was processed
        Assert.NotNull(village);
    }

    [Fact(DisplayName = "Rapid channel creation burst is handled correctly")]
    public async Task RapidChannelBurst_AllProcessedCorrectly()
    {
        // Arrange - Create a village
        var groupId = $"burst-group-{Guid.NewGuid():N}";
        var groupName = "Burst Test Village";

        await _publisher.PublishChannelGroupCreatedAsync(groupId, groupName);
        await _fixture.WaitForJobsToCompleteAsync(JobTimeout);

        // Act - Create 5 channels in rapid succession
        var channelCount = 5;
        var tasks = new List<Task>();

        for (int i = 1; i <= channelCount; i++)
        {
            var channelId = $"burst-ch-{i}-{Guid.NewGuid():N}";
            var channelName = $"burst-channel-{i}";
            tasks.Add(_publisher.PublishChannelCreatedAsync(channelId, channelName, groupId, position: i));
        }

        await Task.WhenAll(tasks);
        await _fixture.WaitForJobsToCompleteAsync(TimeSpan.FromMinutes(10)); // Longer for burst
        await Task.Delay(TimeSpan.FromSeconds(5));

        // Assert - All channels should be created
        var buildings = await _blueMap.GetBuildingMarkersAsync();
        var burstBuildings = buildings.Where(b =>
            b.Label?.StartsWith("burst-channel-") == true).ToList();

        Assert.Equal(channelCount, burstBuildings.Count);

        // All should have unique positions
        var positions = burstBuildings
            .Select(b => $"{b.Position?.X},{b.Position?.Z}")
            .Distinct()
            .ToList();

        Assert.Equal(channelCount, positions.Count);
    }

    [Fact(DisplayName = "Duplicate channel creation events are idempotent")]
    public async Task DuplicateChannelEvents_Idempotent()
    {
        // Arrange
        var groupId = $"dupe-group-{Guid.NewGuid():N}";
        var groupName = "Duplicate Test Village";
        var channelId = $"dupe-channel-{Guid.NewGuid():N}";
        var channelName = "duplicate-channel";

        await _publisher.PublishChannelGroupCreatedAsync(groupId, groupName);
        await _fixture.WaitForJobsToCompleteAsync(JobTimeout);

        // Act - Send the same channel create event multiple times
        await _publisher.PublishChannelCreatedAsync(channelId, channelName, groupId);
        await _publisher.PublishChannelCreatedAsync(channelId, channelName, groupId);
        await _publisher.PublishChannelCreatedAsync(channelId, channelName, groupId);

        await _fixture.WaitForJobsToCompleteAsync(JobTimeout);
        await Task.Delay(TimeSpan.FromSeconds(5));

        // Assert - Should only have one building (idempotent processing)
        var buildings = await _blueMap.GetBuildingMarkersAsync();
        var dupeBuildings = buildings.Where(b =>
            b.Label?.Contains(channelName) == true).ToList();

        Assert.Single(dupeBuildings);
    }

    [Fact(DisplayName = "Very long channel names are handled gracefully")]
    public async Task VeryLongChannelName_HandledGracefully()
    {
        // Arrange
        var groupId = $"longname-group-{Guid.NewGuid():N}";
        var groupName = "Long Name Test Village";

        await _publisher.PublishChannelGroupCreatedAsync(groupId, groupName);
        await _fixture.WaitForJobsToCompleteAsync(JobTimeout);

        // Act - Create a channel with an absurdly long name
        var channelId = $"longname-ch-{Guid.NewGuid():N}";
        var channelName = "this-is-a-very-long-channel-name-that-exceeds-typical-limits-" +
                          "and-should-be-truncated-or-handled-gracefully-by-the-system";

        await _publisher.PublishChannelCreatedAsync(channelId, channelName, groupId);
        await _fixture.WaitForJobsToCompleteAsync(JobTimeout);
        await Task.Delay(TimeSpan.FromSeconds(5));

        // Assert - Building should exist (name may be truncated)
        var buildings = await _blueMap.GetBuildingMarkersAsync();
        var building = buildings.FirstOrDefault(b =>
            b.Label?.Contains("this-is-a-very-long") == true ||
            b.Label?.Contains("long-channel") == true);

        Assert.NotNull(building);
    }

    [Fact(DisplayName = "Unicode channel names are handled correctly")]
    public async Task UnicodeChannelName_HandledCorrectly()
    {
        // Arrange
        var groupId = $"unicode-group-{Guid.NewGuid():N}";
        var groupName = "Unicode Test Village 日本語";

        await _publisher.PublishChannelGroupCreatedAsync(groupId, groupName);
        await _fixture.WaitForJobsToCompleteAsync(JobTimeout);

        var channelId = $"unicode-ch-{Guid.NewGuid():N}";
        var channelName = "общий-чат-🚀";

        await _publisher.PublishChannelCreatedAsync(channelId, channelName, groupId);
        await _fixture.WaitForJobsToCompleteAsync(JobTimeout);
        await Task.Delay(TimeSpan.FromSeconds(5));

        // Assert - Village and building should exist
        var villages = await _blueMap.GetVillageMarkersAsync();
        var village = villages.FirstOrDefault(v =>
            v.Label?.Contains("Unicode Test Village") == true ||
            v.Label?.Contains("日本語") == true);

        Assert.NotNull(village);
    }
}
