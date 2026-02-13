using Bridge.Data.Events;
using StackExchange.Redis;

namespace Acceptance.Tests.Infrastructure;

/// <summary>
/// Helpers for generating test Discord events and publishing them to Redis.
/// Simulates what DiscordBot.Service would do when Discord events arrive.
/// </summary>
public sealed class DiscordEventPublisher
{
    private readonly IConnectionMultiplexer _redis;
    private readonly string _testGuildId;

    public DiscordEventPublisher(IConnectionMultiplexer redis, string testGuildId = "test-guild-001")
    {
        _redis = redis;
        _testGuildId = testGuildId;
    }

    /// <summary>
    /// Publishes a ChannelGroupCreated event (new Discord category → new village).
    /// </summary>
    public async Task PublishChannelGroupCreatedAsync(string groupId, string groupName)
    {
        var evt = new DiscordChannelEvent
        {
            EventType = DiscordChannelEventType.ChannelGroupCreated,
            Timestamp = DateTimeOffset.UtcNow,
            GuildId = _testGuildId,
            ChannelGroupId = groupId,
            ChannelGroupName = groupName
        };
        await PublishAsync(evt);
    }

    /// <summary>
    /// Publishes a ChannelCreated event (new Discord channel → new building).
    /// </summary>
    public async Task PublishChannelCreatedAsync(string channelId, string channelName, string groupId, int position = 0)
    {
        var evt = new DiscordChannelEvent
        {
            EventType = DiscordChannelEventType.ChannelCreated,
            Timestamp = DateTimeOffset.UtcNow,
            GuildId = _testGuildId,
            ChannelId = channelId,
            Name = channelName,
            Position = position,
            ChannelGroupId = groupId
        };
        await PublishAsync(evt);
    }

    /// <summary>
    /// Publishes a ChannelGroupDeleted event (Discord category deleted → village archived).
    /// </summary>
    public async Task PublishChannelGroupDeletedAsync(string groupId)
    {
        var evt = new DiscordChannelEvent
        {
            EventType = DiscordChannelEventType.ChannelGroupDeleted,
            Timestamp = DateTimeOffset.UtcNow,
            GuildId = _testGuildId,
            ChannelGroupId = groupId
        };
        await PublishAsync(evt);
    }

    /// <summary>
    /// Publishes a ChannelDeleted event (Discord channel deleted → building archived).
    /// </summary>
    public async Task PublishChannelDeletedAsync(string channelId, string groupId)
    {
        var evt = new DiscordChannelEvent
        {
            EventType = DiscordChannelEventType.ChannelDeleted,
            Timestamp = DateTimeOffset.UtcNow,
            GuildId = _testGuildId,
            ChannelId = channelId,
            ChannelGroupId = groupId
        };
        await PublishAsync(evt);
    }

    /// <summary>
    /// Publishes a ChannelUpdated event (channel renamed).
    /// </summary>
    public async Task PublishChannelUpdatedAsync(string channelId, string groupId, string oldName, string newName)
    {
        var evt = new DiscordChannelEvent
        {
            EventType = DiscordChannelEventType.ChannelUpdated,
            Timestamp = DateTimeOffset.UtcNow,
            GuildId = _testGuildId,
            ChannelId = channelId,
            ChannelGroupId = groupId,
            OldName = oldName,
            NewName = newName
        };
        await PublishAsync(evt);
    }

    private async Task PublishAsync(DiscordChannelEvent evt)
    {
        var sub = _redis.GetSubscriber();
        await sub.PublishAsync(RedisChannel.Literal(RedisChannels.DiscordChannel), evt.ToJson());
        Console.WriteLine($"[TestPublisher] Published {evt.EventType}: {evt.ChannelGroupName ?? evt.Name}");
    }
}
