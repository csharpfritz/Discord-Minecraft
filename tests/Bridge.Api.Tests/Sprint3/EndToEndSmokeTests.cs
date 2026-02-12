using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Bridge.Api.Tests.Infrastructure;
using Bridge.Data;
using Bridge.Data.Entities;
using Bridge.Data.Events;
using Bridge.Data.Jobs;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

namespace Bridge.Api.Tests.Sprint3;

/// <summary>
/// End-to-end integration test scenarios for Sprint 3 (S3-07, Issue #7).
/// These tests exercise the full Discord event -> Bridge.Api -> DB -> job queue pipeline.
/// Tests that depend on not-yet-implemented Sprint 3 features (slash command endpoints,
/// track generation) are marked with [Fact(Skip = "...")] until implementation lands.
/// </summary>
public sealed class EndToEndSmokeTests : IClassFixture<BridgeApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly BridgeApiFactory _factory;
    private readonly HttpClient _client;

    public EndToEndSmokeTests(BridgeApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    // ──────────────────────────────────────────────────────────────
    // E-01: Full channel sync creates all DB records and jobs
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task FullSync_MultipleGroupsAndChannels_CreatesAllRecordsAndJobs()
    {
        var groups = Enumerable.Range(0, 3)
            .Select(i => new
            {
                discordId = $"e2e-e01-grp{i}-{Guid.NewGuid():N}",
                name = $"e01-village-{i}",
                position = i,
                channels = Enumerable.Range(0, 2).Select(j => new
                {
                    discordId = $"e2e-e01-g{i}ch{j}-{Guid.NewGuid():N}",
                    name = $"e01-channel-{i}-{j}",
                    position = j
                }).ToArray()
            }).ToArray();

        var syncRequest = new
        {
            guildId = "200000000000000001",
            channelGroups = groups
        };

        var response = await _client.PostAsJsonAsync("/api/mappings/sync", syncRequest, JsonOptions);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(result.GetProperty("created").GetInt32() >= 9,
            "Expected at least 9 records created (3 groups + 6 channels)");

        using var db = _factory.CreateDbContext();
        foreach (var g in groups)
        {
            var group = await db.ChannelGroups
                .Include(cg => cg.Channels)
                .FirstOrDefaultAsync(cg => cg.DiscordId == g.discordId);

            Assert.NotNull(group);
            Assert.Equal(g.name, group.Name);
            Assert.Equal(2, group.Channels.Count);
        }

        // Verify all groups have different coordinates
        var allGroups = await db.ChannelGroups
            .Where(g => groups.Select(x => x.discordId).Contains(g.DiscordId))
            .ToListAsync();

        var coords = allGroups.Select(g => (g.CenterX, g.CenterZ)).ToHashSet();
        Assert.Equal(3, coords.Count);
    }

    // ──────────────────────────────────────────────────────────────
    // E-02: Channel create via Redis event creates building job
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task ChannelCreateEvent_CreatesDbRecordAndEnqueuesJob()
    {
        var groupId = $"e2e-e02-grp-{Guid.NewGuid():N}";
        var channelId = $"e2e-e02-ch-{Guid.NewGuid():N}";
        var subscriber = _factory.Redis.GetSubscriber();
        var redisDb = _factory.Redis.GetDatabase();

        await subscriber.PublishAsync(
            RedisChannel.Literal(RedisChannels.DiscordChannel),
            new DiscordChannelEvent
            {
                EventType = DiscordChannelEventType.ChannelGroupCreated,
                Timestamp = DateTimeOffset.UtcNow,
                GuildId = "200000000000000002",
                ChannelGroupId = groupId,
                Name = "e02-village",
                Position = 0
            }.ToJson());
        await Task.Delay(500);

        while (await redisDb.ListRightPopAsync(RedisQueues.WorldGen) != RedisValue.Null) { }

        await subscriber.PublishAsync(
            RedisChannel.Literal(RedisChannels.DiscordChannel),
            new DiscordChannelEvent
            {
                EventType = DiscordChannelEventType.ChannelCreated,
                Timestamp = DateTimeOffset.UtcNow,
                GuildId = "200000000000000002",
                ChannelId = channelId,
                ChannelGroupId = groupId,
                Name = "e02-building",
                Position = 0
            }.ToJson());
        await Task.Delay(1000);

        using var db = _factory.CreateDbContext();
        var channel = await db.Channels.FirstOrDefaultAsync(c => c.DiscordId == channelId);
        Assert.NotNull(channel);
        Assert.Equal("e02-building", channel.Name);
        Assert.False(channel.IsArchived);

        var job = await db.GenerationJobs
            .Where(j => j.Type == WorldGenJobType.CreateBuilding.ToString())
            .OrderByDescending(j => j.Id)
            .FirstOrDefaultAsync();
        Assert.NotNull(job);
        Assert.Equal(GenerationJobStatus.Pending, job.Status);

        var queueLength = await redisDb.ListLengthAsync(RedisQueues.WorldGen);
        Assert.True(queueLength > 0);
    }

    // ──────────────────────────────────────────────────────────────
    // E-05: Channel update refreshes name and enqueues UpdateBuilding
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task ChannelUpdateEvent_UpdatesNameAndEnqueuesUpdateBuildingJob()
    {
        var groupId = $"e2e-e05-grp-{Guid.NewGuid():N}";
        var channelId = $"e2e-e05-ch-{Guid.NewGuid():N}";
        var subscriber = _factory.Redis.GetSubscriber();

        await subscriber.PublishAsync(
            RedisChannel.Literal(RedisChannels.DiscordChannel),
            new DiscordChannelEvent
            {
                EventType = DiscordChannelEventType.ChannelGroupCreated,
                Timestamp = DateTimeOffset.UtcNow,
                GuildId = "200000000000000005",
                ChannelGroupId = groupId,
                Name = "e05-village",
                Position = 0
            }.ToJson());
        await Task.Delay(500);

        await subscriber.PublishAsync(
            RedisChannel.Literal(RedisChannels.DiscordChannel),
            new DiscordChannelEvent
            {
                EventType = DiscordChannelEventType.ChannelCreated,
                Timestamp = DateTimeOffset.UtcNow,
                GuildId = "200000000000000005",
                ChannelId = channelId,
                ChannelGroupId = groupId,
                Name = "original-name",
                Position = 0
            }.ToJson());
        await Task.Delay(500);

        await subscriber.PublishAsync(
            RedisChannel.Literal(RedisChannels.DiscordChannel),
            new DiscordChannelEvent
            {
                EventType = DiscordChannelEventType.ChannelUpdated,
                Timestamp = DateTimeOffset.UtcNow,
                GuildId = "200000000000000005",
                ChannelId = channelId,
                ChannelGroupId = groupId,
                OldName = "original-name",
                NewName = "renamed-channel"
            }.ToJson());
        await Task.Delay(1000);

        using var db = _factory.CreateDbContext();
        var channel = await db.Channels.FirstOrDefaultAsync(c => c.DiscordId == channelId);
        Assert.NotNull(channel);
        Assert.Equal("renamed-channel", channel.Name);

        var job = await db.GenerationJobs
            .Where(j => j.Type == WorldGenJobType.UpdateBuilding.ToString())
            .OrderByDescending(j => j.Id)
            .FirstOrDefaultAsync();
        Assert.NotNull(job);
        Assert.Contains("renamed-channel", job.Payload);
    }

    // ──────────────────────────────────────────────────────────────
    // E-08: Mixed create and delete operations
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task MixedCreateAndDelete_CorrectFinalState()
    {
        var groupId = $"e2e-e08-grp-{Guid.NewGuid():N}";
        var ch1 = $"e2e-e08-ch1-{Guid.NewGuid():N}";
        var ch2 = $"e2e-e08-ch2-{Guid.NewGuid():N}";
        var ch3 = $"e2e-e08-ch3-{Guid.NewGuid():N}";

        await SyncGroupWithChannels(groupId, "e08-village",
            (ch1, "keep-1"), (ch2, "to-delete"), (ch3, "keep-2"));

        await PublishEvent(new DiscordChannelEvent
        {
            EventType = DiscordChannelEventType.ChannelDeleted,
            Timestamp = DateTimeOffset.UtcNow,
            GuildId = "200000000000000008",
            ChannelId = ch2,
            ChannelGroupId = groupId,
            Name = "to-delete"
        });

        using var db = _factory.CreateDbContext();
        var channels = await db.Channels
            .Where(c => new[] { ch1, ch2, ch3 }.Contains(c.DiscordId))
            .ToListAsync();

        Assert.Equal(3, channels.Count);
        Assert.Equal(2, channels.Count(c => !c.IsArchived));
        Assert.Single(channels, c => c.IsArchived);

        var villageResponse = await _client.GetAsync("/api/villages");
        var villages = await villageResponse.Content.ReadFromJsonAsync<JsonElement>();
        foreach (var v in villages.EnumerateArray())
        {
            if (v.GetProperty("discordId").GetString() == groupId)
            {
                Assert.Equal(2, v.GetProperty("buildingCount").GetInt32());
                return;
            }
        }
        Assert.Fail("Village not found in response");
    }

    // ──────────────────────────────────────────────────────────────
    // E-09: /status endpoint (pending Sprint 3 implementation)
    // ──────────────────────────────────────────────────────────────

    [Fact(Skip = "Pending /api/status endpoint implementation in Sprint 3")]
    public async Task StatusEndpoint_ReturnsCorrectCounts()
    {
        var groupId = $"e2e-e09-grp-{Guid.NewGuid():N}";
        await SyncGroupWithChannels(groupId, "e09-village",
            ($"e2e-e09-ch1-{Guid.NewGuid():N}", "ch1"),
            ($"e2e-e09-ch2-{Guid.NewGuid():N}", "ch2"));

        var response = await _client.GetAsync("/api/status");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var status = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(status.GetProperty("villageCount").GetInt32() > 0);
        Assert.True(status.GetProperty("buildingCount").GetInt32() > 0);
    }

    // ──────────────────────────────────────────────────────────────
    // E-10: /navigate endpoint (pending Sprint 3 implementation)
    // ──────────────────────────────────────────────────────────────

    [Fact(Skip = "Pending /api/navigate endpoint implementation in Sprint 3")]
    public async Task NavigateEndpoint_ReturnsChannelLocation()
    {
        var groupId = $"e2e-e10-grp-{Guid.NewGuid():N}";
        var channelId = $"e2e-e10-ch-{Guid.NewGuid():N}";
        await SyncSingleGroupWithChannel(groupId, "e10-village", channelId, "e10-channel");

        var response = await _client.GetAsync($"/api/navigate/{channelId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(result.TryGetProperty("villageName", out _));
        Assert.True(result.TryGetProperty("buildingName", out _));
    }

    // ══════════════════════════════════════════════════════════════
    // Test Helpers
    // ══════════════════════════════════════════════════════════════

    private async Task SyncSingleGroupWithChannel(
        string groupDiscordId, string groupName,
        string channelDiscordId, string channelName)
    {
        var syncRequest = new
        {
            guildId = "200000000000000000",
            channelGroups = new[]
            {
                new
                {
                    discordId = groupDiscordId,
                    name = groupName,
                    position = 0,
                    channels = new[]
                    {
                        new { discordId = channelDiscordId, name = channelName, position = 0 }
                    }
                }
            }
        };

        var response = await _client.PostAsJsonAsync("/api/mappings/sync", syncRequest, JsonOptions);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task SyncGroupWithChannels(
        string groupDiscordId, string groupName,
        params (string Id, string Name)[] channels)
    {
        var syncRequest = new
        {
            guildId = "200000000000000000",
            channelGroups = new[]
            {
                new
                {
                    discordId = groupDiscordId,
                    name = groupName,
                    position = 0,
                    channels = channels.Select((c, i) => new
                    {
                        discordId = c.Id,
                        name = c.Name,
                        position = i
                    }).ToArray()
                }
            }
        };

        var response = await _client.PostAsJsonAsync("/api/mappings/sync", syncRequest, JsonOptions);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task PublishEvent(DiscordChannelEvent evt, int delayMs = 1000)
    {
        var subscriber = _factory.Redis.GetSubscriber();
        await subscriber.PublishAsync(
            RedisChannel.Literal(RedisChannels.DiscordChannel),
            evt.ToJson());
        await Task.Delay(delayMs);
    }
}
