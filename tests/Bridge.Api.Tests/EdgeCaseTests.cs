using System.Net.Http.Json;
using System.Text.Json;
using Bridge.Api.Tests.Infrastructure;
using Bridge.Data.Entities;
using Bridge.Data.Events;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

namespace Bridge.Api.Tests;

/// <summary>
/// Edge case tests: duplicate events, channel without category, deletion behavior.
/// </summary>
public sealed class EdgeCaseTests : IClassFixture<BridgeApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly BridgeApiFactory _factory;
    private readonly HttpClient _client;

    public EdgeCaseTests(BridgeApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task DuplicateChannelGroupCreated_UpsertsDoesNotDuplicate()
    {
        // Arrange
        var discordGroupId = $"dup-grp-{Guid.NewGuid():N}";
        var subscriber = _factory.Redis.GetSubscriber();

        var evt = new DiscordChannelEvent
        {
            EventType = DiscordChannelEventType.ChannelGroupCreated,
            Timestamp = DateTimeOffset.UtcNow,
            GuildId = "111111111111111111",
            ChannelGroupId = discordGroupId,
            Name = "duplicate-test",
            Position = 0
        };

        // Act — publish the same event twice
        await subscriber.PublishAsync(
            RedisChannel.Literal(RedisChannels.DiscordChannel), evt.ToJson());
        await Task.Delay(500);

        var evt2 = evt with { Name = "duplicate-test-updated", Timestamp = DateTimeOffset.UtcNow };
        await subscriber.PublishAsync(
            RedisChannel.Literal(RedisChannels.DiscordChannel), evt2.ToJson());
        await Task.Delay(1000);

        // Assert — should be only one group with that discordId (upsert)
        using var db = _factory.CreateDbContext();
        var groups = await db.ChannelGroups
            .Where(g => g.DiscordId == discordGroupId)
            .ToListAsync();

        Assert.Single(groups);
        Assert.Equal("duplicate-test-updated", groups[0].Name);
    }

    [Fact]
    public async Task DuplicateChannelCreated_UpsertsDoesNotDuplicate()
    {
        // Arrange — create group first
        var discordGroupId = $"dupch-grp-{Guid.NewGuid():N}";
        var discordChannelId = $"dupch-ch-{Guid.NewGuid():N}";
        var subscriber = _factory.Redis.GetSubscriber();

        await subscriber.PublishAsync(
            RedisChannel.Literal(RedisChannels.DiscordChannel),
            new DiscordChannelEvent
            {
                EventType = DiscordChannelEventType.ChannelGroupCreated,
                Timestamp = DateTimeOffset.UtcNow,
                GuildId = "111111111111111111",
                ChannelGroupId = discordGroupId,
                Name = "dup-ch-group",
                Position = 0
            }.ToJson());
        await Task.Delay(500);

        var channelEvt = new DiscordChannelEvent
        {
            EventType = DiscordChannelEventType.ChannelCreated,
            Timestamp = DateTimeOffset.UtcNow,
            GuildId = "111111111111111111",
            ChannelId = discordChannelId,
            ChannelGroupId = discordGroupId,
            Name = "original-name",
            Position = 0
        };

        // Act — publish same channel event twice
        await subscriber.PublishAsync(
            RedisChannel.Literal(RedisChannels.DiscordChannel), channelEvt.ToJson());
        await Task.Delay(500);

        var channelEvt2 = channelEvt with { Name = "updated-name", Timestamp = DateTimeOffset.UtcNow };
        await subscriber.PublishAsync(
            RedisChannel.Literal(RedisChannels.DiscordChannel), channelEvt2.ToJson());
        await Task.Delay(1000);

        // Assert — single channel, name updated
        using var db = _factory.CreateDbContext();
        var channels = await db.Channels
            .Where(c => c.DiscordId == discordChannelId)
            .ToListAsync();

        Assert.Single(channels);
        Assert.Equal("updated-name", channels[0].Name);
    }

    [Fact]
    public async Task ChannelCreated_WithoutExistingCategory_AutoCreatesGroup()
    {
        // Arrange — publish ChannelCreated for a group that doesn't exist yet
        var discordGroupId = $"auto-grp-{Guid.NewGuid():N}";
        var discordChannelId = $"auto-ch-{Guid.NewGuid():N}";
        var subscriber = _factory.Redis.GetSubscriber();

        var evt = new DiscordChannelEvent
        {
            EventType = DiscordChannelEventType.ChannelCreated,
            Timestamp = DateTimeOffset.UtcNow,
            GuildId = "111111111111111111",
            ChannelId = discordChannelId,
            ChannelGroupId = discordGroupId,
            Name = "orphan-channel",
            Position = 0
        };

        // Act
        await subscriber.PublishAsync(
            RedisChannel.Literal(RedisChannels.DiscordChannel), evt.ToJson());
        await Task.Delay(1000);

        // Assert — group should have been auto-created
        using var db = _factory.CreateDbContext();
        var group = await db.ChannelGroups.FirstOrDefaultAsync(g => g.DiscordId == discordGroupId);
        Assert.NotNull(group);

        var channel = await db.Channels.FirstOrDefaultAsync(c => c.DiscordId == discordChannelId);
        Assert.NotNull(channel);
        Assert.Equal(group.Id, channel.ChannelGroupId);
    }

    [Fact]
    public async Task ChannelDeletion_SetsIsArchivedFlag()
    {
        // Arrange — create group + channel via sync
        var groupDiscordId = $"edge-del-grp-{Guid.NewGuid():N}";
        var channelDiscordId = $"edge-del-ch-{Guid.NewGuid():N}";

        var syncRequest = new
        {
            guildId = "111111111111111111",
            channelGroups = new[]
            {
                new
                {
                    discordId = groupDiscordId,
                    name = "edge-del-group",
                    position = 0,
                    channels = new[]
                    {
                        new { discordId = channelDiscordId, name = "to-delete", position = 0 }
                    }
                }
            }
        };

        await _client.PostAsJsonAsync("/api/mappings/sync", syncRequest, JsonOptions);

        // Act — delete via event
        var subscriber = _factory.Redis.GetSubscriber();
        await subscriber.PublishAsync(
            RedisChannel.Literal(RedisChannels.DiscordChannel),
            new DiscordChannelEvent
            {
                EventType = DiscordChannelEventType.ChannelDeleted,
                Timestamp = DateTimeOffset.UtcNow,
                GuildId = "111111111111111111",
                ChannelId = channelDiscordId,
                ChannelGroupId = groupDiscordId,
                Name = "to-delete"
            }.ToJson());
        await Task.Delay(1000);

        // Assert
        using var db = _factory.CreateDbContext();
        var channel = await db.Channels.FirstOrDefaultAsync(c => c.DiscordId == channelDiscordId);
        Assert.NotNull(channel);
        Assert.True(channel.IsArchived, "Channel should be archived after ChannelDeleted event");
    }

    [Fact]
    public async Task PostSync_Idempotent_SecondSyncUpdatesExisting()
    {
        // Arrange
        var groupDiscordId = $"idem-grp-{Guid.NewGuid():N}";
        var channelDiscordId = $"idem-ch-{Guid.NewGuid():N}";

        var syncRequest1 = new
        {
            guildId = "111111111111111111",
            channelGroups = new[]
            {
                new
                {
                    discordId = groupDiscordId,
                    name = "original-name",
                    position = 0,
                    channels = new[]
                    {
                        new { discordId = channelDiscordId, name = "ch-original", position = 0 }
                    }
                }
            }
        };

        await _client.PostAsJsonAsync("/api/mappings/sync", syncRequest1, JsonOptions);

        // Act — sync again with updated names
        var syncRequest2 = new
        {
            guildId = "111111111111111111",
            channelGroups = new[]
            {
                new
                {
                    discordId = groupDiscordId,
                    name = "updated-name",
                    position = 1,
                    channels = new[]
                    {
                        new { discordId = channelDiscordId, name = "ch-updated", position = 0 }
                    }
                }
            }
        };

        var response = await _client.PostAsJsonAsync("/api/mappings/sync", syncRequest2, JsonOptions);

        // Assert
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(result.GetProperty("updated").GetInt32() >= 2, "Expected at least 2 updates");

        using var db = _factory.CreateDbContext();
        var group = await db.ChannelGroups.FirstOrDefaultAsync(g => g.DiscordId == groupDiscordId);
        Assert.NotNull(group);
        Assert.Equal("updated-name", group.Name);
        Assert.Equal(1, group.Position);

        var channel = await db.Channels.FirstOrDefaultAsync(c => c.DiscordId == channelDiscordId);
        Assert.NotNull(channel);
        Assert.Equal("ch-updated", channel.Name);
    }

    [Fact]
    public async Task ChannelDeletedForUnknownChannel_HandlesGracefully()
    {
        // Act — delete a channel that was never created
        var subscriber = _factory.Redis.GetSubscriber();
        await subscriber.PublishAsync(
            RedisChannel.Literal(RedisChannels.DiscordChannel),
            new DiscordChannelEvent
            {
                EventType = DiscordChannelEventType.ChannelDeleted,
                Timestamp = DateTimeOffset.UtcNow,
                GuildId = "111111111111111111",
                ChannelId = $"nonexistent-{Guid.NewGuid():N}",
                ChannelGroupId = $"nonexistent-grp-{Guid.NewGuid():N}",
                Name = "ghost"
            }.ToJson());
        await Task.Delay(500);

        // Assert — no exception, health check still works
        var response = await _client.GetAsync("/health");
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task PostPlayerLink_GeneratesUniqueCodesPerRequest()
    {
        // Act — generate two link codes
        var request = new { discordId = "user-unique-test" };

        var response1 = await _client.PostAsJsonAsync("/api/players/link", request, JsonOptions);
        var result1 = await response1.Content.ReadFromJsonAsync<JsonElement>();
        var code1 = result1.GetProperty("code").GetString();

        var response2 = await _client.PostAsJsonAsync("/api/players/link", request, JsonOptions);
        var result2 = await response2.Content.ReadFromJsonAsync<JsonElement>();
        var code2 = result2.GetProperty("code").GetString();

        // Assert — codes should be different (extremely unlikely to collide)
        Assert.NotNull(code1);
        Assert.NotNull(code2);
        Assert.NotEqual(code1, code2);
    }
}
