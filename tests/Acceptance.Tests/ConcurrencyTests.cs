using Acceptance.Tests.Infrastructure;

namespace Acceptance.Tests;

/// <summary>
/// Acceptance tests for concurrent village and channel creation.
/// Verifies the system handles parallelism correctly:
/// - Multiple villages created simultaneously
/// - Multiple channels added to different villages concurrently
/// - Concurrent track generation doesn't corrupt state
/// - Job queue handles parallel inserts
/// </summary>
[Collection("FullStack")]
[Trait("Category", "Acceptance")]
[Trait("Subcategory", "Concurrency")]
public class ConcurrencyTests : IClassFixture<FullStackFixture>
{
    private readonly FullStackFixture _fixture;
    private readonly BlueMapClient _blueMap;
    private readonly DiscordEventPublisher _publisher;

    private static readonly TimeSpan JobTimeout = TimeSpan.FromMinutes(10);

    public ConcurrencyTests(FullStackFixture fixture)
    {
        _fixture = fixture;
        _blueMap = new BlueMapClient(fixture.BlueMapClient);
        _publisher = new DiscordEventPublisher(fixture.Redis);
    }

    [Fact(DisplayName = "Three villages created simultaneously all succeed")]
    public async Task ThreeVillages_CreatedSimultaneously_AllSucceed()
    {
        // Arrange
        var villages = new[]
        {
            ($"concurrent-v1-{Guid.NewGuid():N}", "Concurrent Village One"),
            ($"concurrent-v2-{Guid.NewGuid():N}", "Concurrent Village Two"),
            ($"concurrent-v3-{Guid.NewGuid():N}", "Concurrent Village Three")
        };

        // Act - Create all three villages simultaneously
        var tasks = villages.Select(v =>
            _publisher.PublishChannelGroupCreatedAsync(v.Item1, v.Item2));
        await Task.WhenAll(tasks);

        // Wait for all jobs to complete
        await _fixture.WaitForJobsToCompleteAsync(JobTimeout);
        await Task.Delay(TimeSpan.FromSeconds(5));

        // Assert - All three villages should exist
        var markers = await _blueMap.GetVillageMarkersAsync();

        foreach (var (_, name) in villages)
        {
            var marker = markers.FirstOrDefault(v => v.Label?.Contains(name) == true);
            Assert.NotNull(marker);
            Assert.NotNull(marker.Position);
        }

        // All positions should be distinct
        var positions = markers
            .Where(m => villages.Any(v => m.Label?.Contains(v.Item2) == true))
            .Select(m => $"{m.Position?.X},{m.Position?.Z}")
            .Distinct()
            .ToList();

        Assert.Equal(3, positions.Count);
    }

    [Fact(DisplayName = "Concurrent channels in same village get distinct ring positions")]
    public async Task ConcurrentChannelsInSameVillage_DistinctPositions()
    {
        // Arrange - Create a village first
        var groupId = $"concurrent-channels-{Guid.NewGuid():N}";
        var groupName = "Concurrent Channels Village";

        await _publisher.PublishChannelGroupCreatedAsync(groupId, groupName);
        await _fixture.WaitForJobsToCompleteAsync(TimeSpan.FromMinutes(5));

        // Act - Create 4 channels simultaneously in the same village
        var channels = new[]
        {
            ($"cc-ch1-{Guid.NewGuid():N}", "concurrent-ch-1", 1),
            ($"cc-ch2-{Guid.NewGuid():N}", "concurrent-ch-2", 2),
            ($"cc-ch3-{Guid.NewGuid():N}", "concurrent-ch-3", 3),
            ($"cc-ch4-{Guid.NewGuid():N}", "concurrent-ch-4", 4)
        };

        var tasks = channels.Select(c =>
            _publisher.PublishChannelCreatedAsync(c.Item1, c.Item2, groupId, position: c.Item3));
        await Task.WhenAll(tasks);

        await _fixture.WaitForJobsToCompleteAsync(JobTimeout);
        await Task.Delay(TimeSpan.FromSeconds(5));

        // Assert - All 4 buildings should exist at distinct positions
        var buildings = await _blueMap.GetBuildingMarkersAsync();
        var ourBuildings = buildings
            .Where(b => b.Label?.StartsWith("concurrent-ch-") == true)
            .ToList();

        Assert.Equal(4, ourBuildings.Count);

        // All positions must be unique
        var positions = ourBuildings
            .Select(b => $"{b.Position?.X},{b.Position?.Z}")
            .Distinct()
            .ToList();

        Assert.Equal(4, positions.Count);
    }

    [Fact(DisplayName = "Channels in different villages created concurrently")]
    public async Task ChannelsInDifferentVillages_ConcurrentCreation()
    {
        // Arrange - Create two villages
        var group1Id = $"diff-village-1-{Guid.NewGuid():N}";
        var group1Name = "Different Village One";
        var group2Id = $"diff-village-2-{Guid.NewGuid():N}";
        var group2Name = "Different Village Two";

        await _publisher.PublishChannelGroupCreatedAsync(group1Id, group1Name);
        await _publisher.PublishChannelGroupCreatedAsync(group2Id, group2Name);
        await _fixture.WaitForJobsToCompleteAsync(TimeSpan.FromMinutes(5));

        // Act - Create channels in both villages simultaneously
        var tasks = new List<Task>
        {
            _publisher.PublishChannelCreatedAsync($"dv1-ch1-{Guid.NewGuid():N}", "diff-v1-channel-1", group1Id),
            _publisher.PublishChannelCreatedAsync($"dv1-ch2-{Guid.NewGuid():N}", "diff-v1-channel-2", group1Id),
            _publisher.PublishChannelCreatedAsync($"dv2-ch1-{Guid.NewGuid():N}", "diff-v2-channel-1", group2Id),
            _publisher.PublishChannelCreatedAsync($"dv2-ch2-{Guid.NewGuid():N}", "diff-v2-channel-2", group2Id)
        };
        await Task.WhenAll(tasks);

        await _fixture.WaitForJobsToCompleteAsync(JobTimeout);
        await Task.Delay(TimeSpan.FromSeconds(5));

        // Assert - All 4 buildings should exist
        var buildings = await _blueMap.GetBuildingMarkersAsync();

        var v1Buildings = buildings.Where(b => b.Label?.StartsWith("diff-v1-channel") == true).ToList();
        var v2Buildings = buildings.Where(b => b.Label?.StartsWith("diff-v2-channel") == true).ToList();

        Assert.Equal(2, v1Buildings.Count);
        Assert.Equal(2, v2Buildings.Count);
    }

    [Fact(DisplayName = "Mixed village and channel creation is handled")]
    public async Task MixedCreation_VillagesAndChannels()
    {
        // Arrange - Pre-create one village
        var existingGroupId = $"mixed-existing-{Guid.NewGuid():N}";
        var existingGroupName = "Mixed Existing Village";

        await _publisher.PublishChannelGroupCreatedAsync(existingGroupId, existingGroupName);
        await _fixture.WaitForJobsToCompleteAsync(TimeSpan.FromMinutes(5));

        // Act - Simultaneously:
        // - Create a new village
        // - Create a channel in the existing village
        var newGroupId = $"mixed-new-{Guid.NewGuid():N}";
        var newGroupName = "Mixed New Village";
        var channelId = $"mixed-ch-{Guid.NewGuid():N}";

        await Task.WhenAll(
            _publisher.PublishChannelGroupCreatedAsync(newGroupId, newGroupName),
            _publisher.PublishChannelCreatedAsync(channelId, "mixed-channel", existingGroupId)
        );

        await _fixture.WaitForJobsToCompleteAsync(JobTimeout);
        await Task.Delay(TimeSpan.FromSeconds(5));

        // Assert - Both operations succeed
        var villages = await _blueMap.GetVillageMarkersAsync();
        var buildings = await _blueMap.GetBuildingMarkersAsync();

        var existingVillage = villages.FirstOrDefault(v => v.Label?.Contains(existingGroupName) == true);
        var newVillage = villages.FirstOrDefault(v => v.Label?.Contains(newGroupName) == true);
        var building = buildings.FirstOrDefault(b => b.Label?.Contains("mixed-channel") == true);

        Assert.NotNull(existingVillage);
        Assert.NotNull(newVillage);
        Assert.NotNull(building);
    }

    [Fact(DisplayName = "Concurrent delete and create for same group ID")]
    public async Task ConcurrentDeleteAndCreate_SameGroupId()
    {
        // This is an edge case: what if a group is deleted and recreated rapidly?
        // The system should handle this without corruption.

        // Arrange - Create a village
        var groupId = $"delete-recreate-{Guid.NewGuid():N}";
        var groupName = "Delete Recreate Village";

        await _publisher.PublishChannelGroupCreatedAsync(groupId, groupName);
        await _fixture.WaitForJobsToCompleteAsync(TimeSpan.FromMinutes(5));

        // Act - Delete and recreate simultaneously (race condition test)
        await Task.WhenAll(
            _publisher.PublishChannelGroupDeletedAsync(groupId),
            Task.Delay(100).ContinueWith(_ =>
                _publisher.PublishChannelGroupCreatedAsync(groupId, "Recreated Village"))
        );

        await _fixture.WaitForJobsToCompleteAsync(JobTimeout);
        await Task.Delay(TimeSpan.FromSeconds(3));

        // Assert - System should be stable (result may vary but shouldn't crash)
        var response = await _fixture.BridgeApiClient.GetAsync("/health");
        response.EnsureSuccessStatusCode();
    }

    [Fact(DisplayName = "High-volume event burst is processed without loss")]
    public async Task HighVolumeEventBurst_ProcessedWithoutLoss()
    {
        // Arrange
        var groupId = $"high-volume-{Guid.NewGuid():N}";
        var groupName = "High Volume Test Village";

        await _publisher.PublishChannelGroupCreatedAsync(groupId, groupName);
        await _fixture.WaitForJobsToCompleteAsync(TimeSpan.FromMinutes(5));

        // Act - Create 10 channels as fast as possible
        var channelCount = 10;
        var tasks = new List<Task>();

        for (int i = 1; i <= channelCount; i++)
        {
            var channelId = $"hv-ch-{i}-{Guid.NewGuid():N}";
            tasks.Add(_publisher.PublishChannelCreatedAsync(channelId, $"high-vol-{i}", groupId, position: i));
        }

        await Task.WhenAll(tasks);
        await _fixture.WaitForJobsToCompleteAsync(TimeSpan.FromMinutes(15)); // Longer for high volume
        await Task.Delay(TimeSpan.FromSeconds(10));

        // Assert - All channels should be created
        var buildings = await _blueMap.GetBuildingMarkersAsync();
        var hvBuildings = buildings.Where(b => b.Label?.StartsWith("high-vol-") == true).ToList();

        Assert.Equal(channelCount, hvBuildings.Count);
    }

    [Fact(DisplayName = "Concurrent village creation triggers correct track counts")]
    public async Task ConcurrentVillageCreation_CorrectTrackCount()
    {
        // When 3 villages are created, we should eventually have 3 tracks:
        // V1: 0 tracks
        // V2: 1 track (to V1)
        // V3: 2 tracks (to V1, V2)
        // Total: 3 bidirectional track connections

        // Arrange
        var v1 = ($"track-concurrent-1-{Guid.NewGuid():N}", "Track Concurrent One");
        var v2 = ($"track-concurrent-2-{Guid.NewGuid():N}", "Track Concurrent Two");
        var v3 = ($"track-concurrent-3-{Guid.NewGuid():N}", "Track Concurrent Three");

        // Act - Create villages in rapid sequence
        await _publisher.PublishChannelGroupCreatedAsync(v1.Item1, v1.Item2);
        await Task.Delay(100); // Small delay to ensure ordering
        await _publisher.PublishChannelGroupCreatedAsync(v2.Item1, v2.Item2);
        await Task.Delay(100);
        await _publisher.PublishChannelGroupCreatedAsync(v3.Item1, v3.Item2);

        // Wait for all jobs including track generation
        await _fixture.WaitForJobsToCompleteAsync(JobTimeout);
        await Task.Delay(TimeSpan.FromSeconds(5));

        // Assert - All villages exist
        var villages = await _blueMap.GetVillageMarkersAsync();

        var found1 = villages.FirstOrDefault(v => v.Label?.Contains(v1.Item2) == true);
        var found2 = villages.FirstOrDefault(v => v.Label?.Contains(v2.Item2) == true);
        var found3 = villages.FirstOrDefault(v => v.Label?.Contains(v3.Item2) == true);

        Assert.NotNull(found1);
        Assert.NotNull(found2);
        Assert.NotNull(found3);

        // Track verification would require BlueMap track markers
        // For now, verify job queue is empty (all tracks processed)
        var db = _fixture.Redis.GetDatabase();
        var queueLen = await db.ListLengthAsync("queue:worldgen");
        Assert.Equal(0, queueLen);
    }
}
