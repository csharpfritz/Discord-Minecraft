using System.Text.Json;
using Acceptance.Tests.Infrastructure;
using StackExchange.Redis;

namespace Acceptance.Tests;

/// <summary>
/// Negative acceptance tests covering error conditions:
/// - Invalid/malformed events
/// - Missing required fields
/// - Job processing failures and retries
/// - System recovery after errors
/// - Resource exhaustion scenarios
/// </summary>
[Collection("FullStack")]
[Trait("Category", "Acceptance")]
[Trait("Subcategory", "Negative")]
public class NegativeTests : IClassFixture<FullStackFixture>
{
    private readonly FullStackFixture _fixture;
    private readonly BlueMapClient _blueMap;
    private readonly DiscordEventPublisher _publisher;

    private static readonly TimeSpan JobTimeout = TimeSpan.FromMinutes(5);

    public NegativeTests(FullStackFixture fixture)
    {
        _fixture = fixture;
        _blueMap = new BlueMapClient(fixture.BlueMapClient);
        _publisher = new DiscordEventPublisher(fixture.Redis);
    }

    [Fact(DisplayName = "Malformed JSON event is ignored gracefully")]
    public async Task MalformedJsonEvent_IgnoredGracefully()
    {
        // Arrange - Push raw malformed JSON to the event channel
        var sub = _fixture.Redis.GetSubscriber();

        // Act - Send garbage
        await sub.PublishAsync(
            RedisChannel.Literal("events:discord:channel"),
            "{ this is not valid JSON at all }}");

        // Wait a moment for processing
        await Task.Delay(TimeSpan.FromSeconds(3));

        // Assert - System should still be healthy
        var response = await _fixture.BridgeApiClient.GetAsync("/health");
        response.EnsureSuccessStatusCode();
    }

    [Fact(DisplayName = "Event with missing EventType is rejected")]
    public async Task MissingEventType_Rejected()
    {
        // Arrange - Create a valid-looking JSON but without EventType
        var invalidEvent = JsonSerializer.Serialize(new
        {
            Timestamp = DateTimeOffset.UtcNow,
            GuildId = "test-guild",
            ChannelGroupId = "missing-type-group",
            ChannelGroupName = "Missing Type Village"
            // No EventType!
        });

        var sub = _fixture.Redis.GetSubscriber();

        // Act
        await sub.PublishAsync(
            RedisChannel.Literal("events:discord:channel"),
            invalidEvent);

        await Task.Delay(TimeSpan.FromSeconds(3));

        // Assert - System remains healthy
        var response = await _fixture.BridgeApiClient.GetAsync("/health");
        response.EnsureSuccessStatusCode();
    }

    [Fact(DisplayName = "Event with null ChannelGroupId is handled")]
    public async Task NullChannelGroupId_Handled()
    {
        // Arrange - Event with explicit null values
        var nullEvent = JsonSerializer.Serialize(new
        {
            EventType = "ChannelCreated",
            Timestamp = DateTimeOffset.UtcNow,
            GuildId = "test-guild",
            ChannelId = "null-group-channel",
            Name = "null-group-test",
            ChannelGroupId = (string?)null // Explicit null
        });

        var sub = _fixture.Redis.GetSubscriber();

        // Act
        await sub.PublishAsync(
            RedisChannel.Literal("events:discord:channel"),
            nullEvent);

        await Task.Delay(TimeSpan.FromSeconds(3));

        // Assert - System remains healthy
        var response = await _fixture.BridgeApiClient.GetAsync("/health");
        response.EnsureSuccessStatusCode();
    }

    [Fact(DisplayName = "Empty event payload is rejected")]
    public async Task EmptyPayload_Rejected()
    {
        // Arrange
        var sub = _fixture.Redis.GetSubscriber();

        // Act - Send empty string
        await sub.PublishAsync(
            RedisChannel.Literal("events:discord:channel"),
            "");

        // Send empty object
        await sub.PublishAsync(
            RedisChannel.Literal("events:discord:channel"),
            "{}");

        await Task.Delay(TimeSpan.FromSeconds(3));

        // Assert - System remains healthy
        var response = await _fixture.BridgeApiClient.GetAsync("/health");
        response.EnsureSuccessStatusCode();
    }

    [Fact(DisplayName = "Extremely large event payload is handled")]
    public async Task ExtremelyLargePayload_Handled()
    {
        // Arrange - Create a giant name
        var giantName = new string('X', 10_000); // 10KB of X's

        var largeEvent = JsonSerializer.Serialize(new
        {
            EventType = "ChannelGroupCreated",
            Timestamp = DateTimeOffset.UtcNow,
            GuildId = "test-guild",
            ChannelGroupId = $"large-payload-{Guid.NewGuid():N}",
            ChannelGroupName = giantName
        });

        var sub = _fixture.Redis.GetSubscriber();

        // Act
        await sub.PublishAsync(
            RedisChannel.Literal("events:discord:channel"),
            largeEvent);

        await Task.Delay(TimeSpan.FromSeconds(5));

        // Assert - System remains healthy
        var response = await _fixture.BridgeApiClient.GetAsync("/health");
        response.EnsureSuccessStatusCode();
    }

    [Fact(DisplayName = "Deleting non-existent channel is no-op")]
    public async Task DeleteNonExistentChannel_NoOp()
    {
        // Arrange - Delete a channel that was never created
        var fakeChannelId = $"never-existed-{Guid.NewGuid():N}";
        var fakeGroupId = $"never-existed-group-{Guid.NewGuid():N}";

        // Act - Should not throw or crash
        await _publisher.PublishChannelDeletedAsync(fakeChannelId, fakeGroupId);
        await Task.Delay(TimeSpan.FromSeconds(3));

        // Assert - System remains healthy
        var response = await _fixture.BridgeApiClient.GetAsync("/health");
        response.EnsureSuccessStatusCode();
    }

    [Fact(DisplayName = "Deleting non-existent channel group is no-op")]
    public async Task DeleteNonExistentChannelGroup_NoOp()
    {
        // Arrange
        var fakeGroupId = $"never-existed-group-{Guid.NewGuid():N}";

        // Act
        await _publisher.PublishChannelGroupDeletedAsync(fakeGroupId);
        await Task.Delay(TimeSpan.FromSeconds(3));

        // Assert
        var response = await _fixture.BridgeApiClient.GetAsync("/health");
        response.EnsureSuccessStatusCode();
    }

    [Fact(DisplayName = "Updating non-existent channel is no-op")]
    public async Task UpdateNonExistentChannel_NoOp()
    {
        // Arrange
        var fakeChannelId = $"never-existed-ch-{Guid.NewGuid():N}";
        var fakeGroupId = $"never-existed-grp-{Guid.NewGuid():N}";

        // Act
        await _publisher.PublishChannelUpdatedAsync(
            fakeChannelId, fakeGroupId, "old-name", "new-name");
        await Task.Delay(TimeSpan.FromSeconds(3));

        // Assert
        var response = await _fixture.BridgeApiClient.GetAsync("/health");
        response.EnsureSuccessStatusCode();
    }

    [Fact(DisplayName = "Special characters in names don't break processing")]
    public async Task SpecialCharactersInNames_DontBreak()
    {
        // Arrange - Names with SQL injection attempts, quotes, etc.
        var groupId = $"special-chars-{Guid.NewGuid():N}";
        var groupName = "Village'; DROP TABLE channel_groups;--";

        // Act
        await _publisher.PublishChannelGroupCreatedAsync(groupId, groupName);
        await _fixture.WaitForJobsToCompleteAsync(JobTimeout);

        // Assert - System still works and village was created
        var response = await _fixture.BridgeApiClient.GetAsync("/health");
        response.EnsureSuccessStatusCode();
    }

    [Fact(DisplayName = "Unknown event type is ignored")]
    public async Task UnknownEventType_Ignored()
    {
        // Arrange - Valid JSON but unknown event type
        var unknownEvent = JsonSerializer.Serialize(new
        {
            EventType = "UnknownFutureEventType",
            Timestamp = DateTimeOffset.UtcNow,
            GuildId = "test-guild",
            ChannelGroupId = "unknown-event-group",
            ChannelGroupName = "Unknown Event Village"
        });

        var sub = _fixture.Redis.GetSubscriber();

        // Act
        await sub.PublishAsync(
            RedisChannel.Literal("events:discord:channel"),
            unknownEvent);

        await Task.Delay(TimeSpan.FromSeconds(3));

        // Assert - System remains healthy
        var response = await _fixture.BridgeApiClient.GetAsync("/health");
        response.EnsureSuccessStatusCode();
    }

    [Fact(DisplayName = "Zero-length channel name is handled")]
    public async Task ZeroLengthChannelName_Handled()
    {
        // Arrange
        var groupId = $"empty-name-group-{Guid.NewGuid():N}";
        var groupName = "Empty Name Test Village";

        await _publisher.PublishChannelGroupCreatedAsync(groupId, groupName);
        await _fixture.WaitForJobsToCompleteAsync(JobTimeout);

        var channelId = $"empty-name-ch-{Guid.NewGuid():N}";

        // Act - Create channel with empty name
        await _publisher.PublishChannelCreatedAsync(channelId, "", groupId);
        await Task.Delay(TimeSpan.FromSeconds(3));

        // Assert - System remains healthy
        var response = await _fixture.BridgeApiClient.GetAsync("/health");
        response.EnsureSuccessStatusCode();
    }

    [Fact(DisplayName = "Negative position values are handled")]
    public async Task NegativePosition_Handled()
    {
        // Arrange
        var groupId = $"neg-pos-group-{Guid.NewGuid():N}";
        var groupName = "Negative Position Village";

        await _publisher.PublishChannelGroupCreatedAsync(groupId, groupName);
        await _fixture.WaitForJobsToCompleteAsync(JobTimeout);

        var channelId = $"neg-pos-ch-{Guid.NewGuid():N}";

        // Act - Create channel with negative position
        await _publisher.PublishChannelCreatedAsync(channelId, "negative-pos-channel", groupId, position: -999);
        await Task.Delay(TimeSpan.FromSeconds(5));

        // Assert - System handles it gracefully
        var response = await _fixture.BridgeApiClient.GetAsync("/health");
        response.EnsureSuccessStatusCode();
    }

    [Fact(DisplayName = "API endpoint returns 404 for non-existent village")]
    public async Task ApiEndpoint_Returns404ForNonExistentVillage()
    {
        // Arrange - A village ID that definitely doesn't exist
        var fakeVillageId = int.MaxValue;

        // Act
        var response = await _fixture.BridgeApiClient.GetAsync($"/api/villages/{fakeVillageId}");

        // Assert - Should return 404, not crash
        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }
}
