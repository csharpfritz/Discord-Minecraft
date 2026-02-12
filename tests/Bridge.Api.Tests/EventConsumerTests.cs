using System.Text.Json;
using Bridge.Api.Tests.Infrastructure;
using Bridge.Data;
using Bridge.Data.Entities;
using Bridge.Data.Events;
using Bridge.Data.Jobs;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

namespace Bridge.Api.Tests;

/// <summary>
/// Tests that the DiscordEventConsumer correctly processes Redis pub/sub events,
/// creates database records, and enqueues world generation jobs.
/// </summary>
public sealed class EventConsumerTests : IClassFixture<BridgeApiFactory>
{
    private readonly BridgeApiFactory _factory;
    private readonly HttpClient _client;

    public EventConsumerTests(BridgeApiFactory factory)
    {
        _factory = factory;
        // Creating the client boots the host (including DiscordEventConsumer)
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task ChannelGroupCreated_CreatesGroupAndEnqueuesVillageJob()
    {
        // Arrange
        var discordGroupId = $"group-{Guid.NewGuid():N}";
        var evt = new DiscordChannelEvent
        {
            EventType = DiscordChannelEventType.ChannelGroupCreated,
            Timestamp = DateTimeOffset.UtcNow,
            GuildId = "111111111111111111",
            ChannelGroupId = discordGroupId,
            Name = "test-village",
            Position = 0
        };

        var subscriber = _factory.Redis.GetSubscriber();
        var redisDb = _factory.Redis.GetDatabase();

        // Act — publish the event
        await subscriber.PublishAsync(
            RedisChannel.Literal(RedisChannels.DiscordChannel),
            evt.ToJson());

        // Allow the consumer time to process
        await Task.Delay(1000);

        // Assert — verify ChannelGroup in database
        using var db = _factory.CreateDbContext();
        var group = await db.ChannelGroups.FirstOrDefaultAsync(g => g.DiscordId == discordGroupId);
        Assert.NotNull(group);
        Assert.Equal("test-village", group.Name);
        Assert.Equal(0, group.Position);

        // Assert — verify GenerationJob created with Pending status
        var job = await db.GenerationJobs
            .Where(j => j.Type == WorldGenJobType.CreateVillage.ToString())
            .OrderByDescending(j => j.Id)
            .FirstOrDefaultAsync();
        Assert.NotNull(job);
        Assert.Equal(GenerationJobStatus.Pending, job.Status);
        Assert.Contains(discordGroupId.Length > 0 ? group.Id.ToString() : "", job.Payload);

        // Assert — verify job was enqueued to Redis queue
        var queueLength = await redisDb.ListLengthAsync(RedisQueues.WorldGen);
        Assert.True(queueLength > 0, "Expected at least one job in the worldgen queue");
    }

    [Fact]
    public async Task ChannelCreated_CreatesChannelLinkedToGroup()
    {
        // Arrange — first create a channel group
        var discordGroupId = $"group-ch-{Guid.NewGuid():N}";
        var discordChannelId = $"chan-{Guid.NewGuid():N}";

        var groupEvt = new DiscordChannelEvent
        {
            EventType = DiscordChannelEventType.ChannelGroupCreated,
            Timestamp = DateTimeOffset.UtcNow,
            GuildId = "222222222222222222",
            ChannelGroupId = discordGroupId,
            Name = "channel-test-group",
            Position = 1
        };

        var subscriber = _factory.Redis.GetSubscriber();
        var redisDb = _factory.Redis.GetDatabase();

        // Drain the queue first
        while (await redisDb.ListRightPopAsync(RedisQueues.WorldGen) != RedisValue.Null) { }

        // Publish group created event
        await subscriber.PublishAsync(
            RedisChannel.Literal(RedisChannels.DiscordChannel),
            groupEvt.ToJson());
        await Task.Delay(500);

        // Now publish the channel created event
        var channelEvt = new DiscordChannelEvent
        {
            EventType = DiscordChannelEventType.ChannelCreated,
            Timestamp = DateTimeOffset.UtcNow,
            GuildId = "222222222222222222",
            ChannelId = discordChannelId,
            ChannelGroupId = discordGroupId,
            Name = "welcome-channel",
            Position = 0
        };

        await subscriber.PublishAsync(
            RedisChannel.Literal(RedisChannels.DiscordChannel),
            channelEvt.ToJson());
        await Task.Delay(1000);

        // Assert — verify Channel created and linked to correct group
        using var db = _factory.CreateDbContext();
        var group = await db.ChannelGroups.FirstOrDefaultAsync(g => g.DiscordId == discordGroupId);
        Assert.NotNull(group);

        var channel = await db.Channels.FirstOrDefaultAsync(c => c.DiscordId == discordChannelId);
        Assert.NotNull(channel);
        Assert.Equal("welcome-channel", channel.Name);
        Assert.Equal(group.Id, channel.ChannelGroupId);
        Assert.False(channel.IsArchived);

        // Assert — verify CreateBuilding job enqueued
        var job = await db.GenerationJobs
            .Where(j => j.Type == WorldGenJobType.CreateBuilding.ToString())
            .OrderByDescending(j => j.Id)
            .FirstOrDefaultAsync();
        Assert.NotNull(job);
        Assert.Equal(GenerationJobStatus.Pending, job.Status);

        // Verify job is in Redis queue
        var queueLength = await redisDb.ListLengthAsync(RedisQueues.WorldGen);
        Assert.True(queueLength > 0, "Expected CreateBuilding job in worldgen queue");
    }

    [Fact]
    public async Task ChannelGroupCreated_VillageCoordinatesFollowGridFormula()
    {
        // Arrange — create multiple groups and verify coordinate formula
        var subscriber = _factory.Redis.GetSubscriber();
        var groupIds = new List<string>();

        for (int i = 0; i < 3; i++)
        {
            var discordGroupId = $"grid-{Guid.NewGuid():N}";
            groupIds.Add(discordGroupId);

            var evt = new DiscordChannelEvent
            {
                EventType = DiscordChannelEventType.ChannelGroupCreated,
                Timestamp = DateTimeOffset.UtcNow,
                GuildId = "333333333333333333",
                ChannelGroupId = discordGroupId,
                Name = $"village-{i}",
                Position = i
            };

            await subscriber.PublishAsync(
                RedisChannel.Literal(RedisChannels.DiscordChannel),
                evt.ToJson());
            await Task.Delay(500);
        }

        await Task.Delay(500);

        // Assert — each group's coordinates should follow the grid formula
        using var db = _factory.CreateDbContext();
        foreach (var discordId in groupIds)
        {
            var group = await db.ChannelGroups.FirstOrDefaultAsync(g => g.DiscordId == discordId);
            Assert.NotNull(group);

            int expectedCol = group.VillageIndex % WorldConstants.GridColumns;
            int expectedRow = group.VillageIndex / WorldConstants.GridColumns;
            int expectedX = expectedCol * WorldConstants.VillageSpacing;
            int expectedZ = expectedRow * WorldConstants.VillageSpacing;

            Assert.Equal(expectedX, group.CenterX);
            Assert.Equal(expectedZ, group.CenterZ);
        }
    }

    [Fact]
    public async Task ChannelDeleted_SetsIsArchivedFlag()
    {
        // Arrange — create group + channel
        var discordGroupId = $"del-grp-{Guid.NewGuid():N}";
        var discordChannelId = $"del-ch-{Guid.NewGuid():N}";
        var subscriber = _factory.Redis.GetSubscriber();

        var groupEvt = new DiscordChannelEvent
        {
            EventType = DiscordChannelEventType.ChannelGroupCreated,
            Timestamp = DateTimeOffset.UtcNow,
            GuildId = "444444444444444444",
            ChannelGroupId = discordGroupId,
            Name = "delete-test",
            Position = 0
        };

        await subscriber.PublishAsync(
            RedisChannel.Literal(RedisChannels.DiscordChannel),
            groupEvt.ToJson());
        await Task.Delay(500);

        var channelEvt = new DiscordChannelEvent
        {
            EventType = DiscordChannelEventType.ChannelCreated,
            Timestamp = DateTimeOffset.UtcNow,
            GuildId = "444444444444444444",
            ChannelId = discordChannelId,
            ChannelGroupId = discordGroupId,
            Name = "doomed-channel",
            Position = 0
        };

        await subscriber.PublishAsync(
            RedisChannel.Literal(RedisChannels.DiscordChannel),
            channelEvt.ToJson());
        await Task.Delay(500);

        // Act — delete the channel
        var deleteEvt = new DiscordChannelEvent
        {
            EventType = DiscordChannelEventType.ChannelDeleted,
            Timestamp = DateTimeOffset.UtcNow,
            GuildId = "444444444444444444",
            ChannelId = discordChannelId,
            ChannelGroupId = discordGroupId,
            Name = "doomed-channel"
        };

        await subscriber.PublishAsync(
            RedisChannel.Literal(RedisChannels.DiscordChannel),
            deleteEvt.ToJson());
        await Task.Delay(1000);

        // Assert
        using var db = _factory.CreateDbContext();
        var channel = await db.Channels.FirstOrDefaultAsync(c => c.DiscordId == discordChannelId);
        Assert.NotNull(channel);
        Assert.True(channel.IsArchived, "Channel should be archived after deletion");
    }

    [Fact]
    public async Task ChannelGroupDeleted_ArchivesGroupAndAllChannels()
    {
        // Arrange — create group with two channels
        var discordGroupId = $"archgrp-{Guid.NewGuid():N}";
        var channelId1 = $"archch1-{Guid.NewGuid():N}";
        var channelId2 = $"archch2-{Guid.NewGuid():N}";
        var subscriber = _factory.Redis.GetSubscriber();

        await subscriber.PublishAsync(
            RedisChannel.Literal(RedisChannels.DiscordChannel),
            new DiscordChannelEvent
            {
                EventType = DiscordChannelEventType.ChannelGroupCreated,
                Timestamp = DateTimeOffset.UtcNow,
                GuildId = "555555555555555555",
                ChannelGroupId = discordGroupId,
                Name = "archive-test",
                Position = 0
            }.ToJson());
        await Task.Delay(500);

        await subscriber.PublishAsync(
            RedisChannel.Literal(RedisChannels.DiscordChannel),
            new DiscordChannelEvent
            {
                EventType = DiscordChannelEventType.ChannelCreated,
                Timestamp = DateTimeOffset.UtcNow,
                GuildId = "555555555555555555",
                ChannelId = channelId1,
                ChannelGroupId = discordGroupId,
                Name = "ch1",
                Position = 0
            }.ToJson());
        await Task.Delay(300);

        await subscriber.PublishAsync(
            RedisChannel.Literal(RedisChannels.DiscordChannel),
            new DiscordChannelEvent
            {
                EventType = DiscordChannelEventType.ChannelCreated,
                Timestamp = DateTimeOffset.UtcNow,
                GuildId = "555555555555555555",
                ChannelId = channelId2,
                ChannelGroupId = discordGroupId,
                Name = "ch2",
                Position = 1
            }.ToJson());
        await Task.Delay(500);

        // Act — delete the group
        await subscriber.PublishAsync(
            RedisChannel.Literal(RedisChannels.DiscordChannel),
            new DiscordChannelEvent
            {
                EventType = DiscordChannelEventType.ChannelGroupDeleted,
                Timestamp = DateTimeOffset.UtcNow,
                GuildId = "555555555555555555",
                ChannelGroupId = discordGroupId,
                Name = "archive-test"
            }.ToJson());
        await Task.Delay(1000);

        // Assert
        using var db = _factory.CreateDbContext();
        var group = await db.ChannelGroups
            .Include(g => g.Channels)
            .FirstOrDefaultAsync(g => g.DiscordId == discordGroupId);

        Assert.NotNull(group);
        Assert.True(group.IsArchived, "Group should be archived");
        Assert.All(group.Channels, ch => Assert.True(ch.IsArchived, $"Channel {ch.Name} should be archived"));
    }
}
