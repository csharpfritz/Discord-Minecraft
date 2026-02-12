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
/// Comprehensive tests for channel deletion handling (S3-05, Issue #5).
/// Validates that deletion archives buildings (sets IsArchived, enqueues sign/barrier jobs)
/// without destroying Minecraft structures.
/// </summary>
public sealed class ChannelDeletionTests : IClassFixture<BridgeApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly BridgeApiFactory _factory;
    private readonly HttpClient _client;

    public ChannelDeletionTests(BridgeApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    // ──────────────────────────────────────────────────────────────
    // D-01: Single channel deletion sets IsArchived
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task ChannelDeleted_SetsIsArchivedTrue()
    {
        var groupId = $"del-d01-grp-{Guid.NewGuid():N}";
        var channelId = $"del-d01-ch-{Guid.NewGuid():N}";
        await SyncSingleGroupWithChannel(groupId, "d01-village", channelId, "d01-channel");

        await PublishEvent(new DiscordChannelEvent
        {
            EventType = DiscordChannelEventType.ChannelDeleted,
            Timestamp = DateTimeOffset.UtcNow,
            GuildId = "100000000000000001",
            ChannelId = channelId,
            ChannelGroupId = groupId,
            Name = "d01-channel"
        });

        using var db = _factory.CreateDbContext();
        var channel = await db.Channels.FirstOrDefaultAsync(c => c.DiscordId == channelId);
        Assert.NotNull(channel);
        Assert.True(channel.IsArchived, "Channel should be archived after deletion");

        var group = await db.ChannelGroups.FirstOrDefaultAsync(g => g.DiscordId == groupId);
        Assert.NotNull(group);
        Assert.False(group.IsArchived, "Group should NOT be archived when only a channel is deleted");
    }

    // ──────────────────────────────────────────────────────────────
    // D-02: Channel group deletion archives group AND all children
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task ChannelGroupDeleted_ArchivesGroupAndAllChannels()
    {
        var groupId = $"del-d02-grp-{Guid.NewGuid():N}";
        var ch1 = $"del-d02-ch1-{Guid.NewGuid():N}";
        var ch2 = $"del-d02-ch2-{Guid.NewGuid():N}";
        var ch3 = $"del-d02-ch3-{Guid.NewGuid():N}";

        await SyncGroupWithChannels(groupId, "d02-village",
            (ch1, "general"), (ch2, "announcements"), (ch3, "off-topic"));

        await PublishEvent(new DiscordChannelEvent
        {
            EventType = DiscordChannelEventType.ChannelGroupDeleted,
            Timestamp = DateTimeOffset.UtcNow,
            GuildId = "100000000000000002",
            ChannelGroupId = groupId,
            Name = "d02-village"
        });

        using var db = _factory.CreateDbContext();
        var group = await db.ChannelGroups
            .Include(g => g.Channels)
            .FirstOrDefaultAsync(g => g.DiscordId == groupId);

        Assert.NotNull(group);
        Assert.True(group.IsArchived, "Group should be archived");
        Assert.Equal(3, group.Channels.Count);
        Assert.All(group.Channels, ch =>
            Assert.True(ch.IsArchived, $"Channel '{ch.Name}' should be archived"));
    }

    // ──────────────────────────────────────────────────────────────
    // D-03: Delete unknown channel — graceful handling
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task ChannelDeleted_UnknownChannel_HandlesGracefully()
    {
        await PublishEvent(new DiscordChannelEvent
        {
            EventType = DiscordChannelEventType.ChannelDeleted,
            Timestamp = DateTimeOffset.UtcNow,
            GuildId = "100000000000000003",
            ChannelId = $"phantom-{Guid.NewGuid():N}",
            ChannelGroupId = $"phantom-grp-{Guid.NewGuid():N}",
            Name = "phantom"
        });

        var response = await _client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ──────────────────────────────────────────────────────────────
    // D-04: Delete unknown channel group — graceful handling
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task ChannelGroupDeleted_UnknownGroup_HandlesGracefully()
    {
        await PublishEvent(new DiscordChannelEvent
        {
            EventType = DiscordChannelEventType.ChannelGroupDeleted,
            Timestamp = DateTimeOffset.UtcNow,
            GuildId = "100000000000000004",
            ChannelGroupId = $"phantom-grp-{Guid.NewGuid():N}",
            Name = "phantom-village"
        });

        var response = await _client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ──────────────────────────────────────────────────────────────
    // D-05: Delete already-archived channel — idempotent
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task ChannelDeleted_AlreadyArchived_RemainsArchivedNoError()
    {
        var groupId = $"del-d05-grp-{Guid.NewGuid():N}";
        var channelId = $"del-d05-ch-{Guid.NewGuid():N}";
        await SyncSingleGroupWithChannel(groupId, "d05-village", channelId, "d05-channel");

        var deleteEvt = new DiscordChannelEvent
        {
            EventType = DiscordChannelEventType.ChannelDeleted,
            Timestamp = DateTimeOffset.UtcNow,
            GuildId = "100000000000000005",
            ChannelId = channelId,
            ChannelGroupId = groupId,
            Name = "d05-channel"
        };

        await PublishEvent(deleteEvt);
        await PublishEvent(deleteEvt with { Timestamp = DateTimeOffset.UtcNow });

        using var db = _factory.CreateDbContext();
        var channel = await db.Channels.FirstOrDefaultAsync(c => c.DiscordId == channelId);
        Assert.NotNull(channel);
        Assert.True(channel.IsArchived);

        var response = await _client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ──────────────────────────────────────────────────────────────
    // D-06: Delete already-archived group — idempotent
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task ChannelGroupDeleted_AlreadyArchived_RemainsArchivedNoError()
    {
        var groupId = $"del-d06-grp-{Guid.NewGuid():N}";
        var channelId = $"del-d06-ch-{Guid.NewGuid():N}";
        await SyncSingleGroupWithChannel(groupId, "d06-village", channelId, "d06-channel");

        var deleteEvt = new DiscordChannelEvent
        {
            EventType = DiscordChannelEventType.ChannelGroupDeleted,
            Timestamp = DateTimeOffset.UtcNow,
            GuildId = "100000000000000006",
            ChannelGroupId = groupId,
            Name = "d06-village"
        };

        await PublishEvent(deleteEvt);
        await PublishEvent(deleteEvt with { Timestamp = DateTimeOffset.UtcNow });

        using var db = _factory.CreateDbContext();
        var group = await db.ChannelGroups
            .Include(g => g.Channels)
            .FirstOrDefaultAsync(g => g.DiscordId == groupId);

        Assert.NotNull(group);
        Assert.True(group.IsArchived);
        Assert.All(group.Channels, ch => Assert.True(ch.IsArchived));

        var response = await _client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ──────────────────────────────────────────────────────────────
    // D-09: GET /api/villages excludes archived buildings from count
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetVillages_ExcludesArchivedBuildingsFromCount()
    {
        var groupId = $"del-d09-grp-{Guid.NewGuid():N}";
        var ch1 = $"del-d09-ch1-{Guid.NewGuid():N}";
        var ch2 = $"del-d09-ch2-{Guid.NewGuid():N}";

        await SyncGroupWithChannels(groupId, "d09-village", (ch1, "active"), (ch2, "doomed"));

        await PublishEvent(new DiscordChannelEvent
        {
            EventType = DiscordChannelEventType.ChannelDeleted,
            Timestamp = DateTimeOffset.UtcNow,
            GuildId = "100000000000000009",
            ChannelId = ch2,
            ChannelGroupId = groupId,
            Name = "doomed"
        });

        var response = await _client.GetAsync("/api/villages");
        var villages = await response.Content.ReadFromJsonAsync<JsonElement>();

        foreach (var v in villages.EnumerateArray())
        {
            if (v.GetProperty("discordId").GetString() == groupId)
            {
                Assert.Equal(1, v.GetProperty("buildingCount").GetInt32());
                return;
            }
        }

        Assert.Fail($"Village with discordId '{groupId}' not found in response");
    }

    // ──────────────────────────────────────────────────────────────
    // D-10: GET /api/villages/{id}/buildings shows archived status
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetVillageBuildings_ShowsArchivedStatus()
    {
        var groupId = $"del-d10-grp-{Guid.NewGuid():N}";
        var ch1 = $"del-d10-ch1-{Guid.NewGuid():N}";
        var ch2 = $"del-d10-ch2-{Guid.NewGuid():N}";

        await SyncGroupWithChannels(groupId, "d10-village", (ch1, "alive"), (ch2, "archived-ch"));

        await PublishEvent(new DiscordChannelEvent
        {
            EventType = DiscordChannelEventType.ChannelDeleted,
            Timestamp = DateTimeOffset.UtcNow,
            GuildId = "100000000000000010",
            ChannelId = ch2,
            ChannelGroupId = groupId,
            Name = "archived-ch"
        });

        using var db = _factory.CreateDbContext();
        var group = await db.ChannelGroups.FirstOrDefaultAsync(g => g.DiscordId == groupId);
        Assert.NotNull(group);

        var response = await _client.GetAsync($"/api/villages/{group.Id}/buildings");
        var buildings = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(2, buildings.GetArrayLength());
        var archivedCount = 0;
        var activeCount = 0;
        foreach (var b in buildings.EnumerateArray())
        {
            if (b.GetProperty("isArchived").GetBoolean())
                archivedCount++;
            else
                activeCount++;
        }

        Assert.Equal(1, archivedCount);
        Assert.Equal(1, activeCount);
    }

    // ──────────────────────────────────────────────────────────────
    // D-11: Archived channel doesn't steal building index
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task NewChannel_AfterDeletion_GetsNextBuildingIndex()
    {
        var groupId = $"del-d11-grp-{Guid.NewGuid():N}";
        var ch1 = $"del-d11-ch1-{Guid.NewGuid():N}";
        var ch2 = $"del-d11-ch2-{Guid.NewGuid():N}";

        await SyncSingleGroupWithChannel(groupId, "d11-village", ch1, "first-channel");

        await PublishEvent(new DiscordChannelEvent
        {
            EventType = DiscordChannelEventType.ChannelDeleted,
            Timestamp = DateTimeOffset.UtcNow,
            GuildId = "100000000000000011",
            ChannelId = ch1,
            ChannelGroupId = groupId,
            Name = "first-channel"
        });

        await PublishEvent(new DiscordChannelEvent
        {
            EventType = DiscordChannelEventType.ChannelCreated,
            Timestamp = DateTimeOffset.UtcNow,
            GuildId = "100000000000000011",
            ChannelId = ch2,
            ChannelGroupId = groupId,
            Name = "second-channel",
            Position = 1
        });

        using var db = _factory.CreateDbContext();
        var archived = await db.Channels.FirstOrDefaultAsync(c => c.DiscordId == ch1);
        var newChannel = await db.Channels.FirstOrDefaultAsync(c => c.DiscordId == ch2);

        Assert.NotNull(archived);
        Assert.NotNull(newChannel);
        Assert.Equal(0, archived.BuildingIndex);
        Assert.True(archived.IsArchived);
        Assert.Equal(1, newChannel.BuildingIndex);
        Assert.False(newChannel.IsArchived);
    }

    // ──────────────────────────────────────────────────────────────
    // D-13: Delete last channel in group — group stays active
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task ChannelDeleted_LastChannelInGroup_GroupStaysActive()
    {
        var groupId = $"del-d13-grp-{Guid.NewGuid():N}";
        var channelId = $"del-d13-ch-{Guid.NewGuid():N}";
        await SyncSingleGroupWithChannel(groupId, "d13-village", channelId, "lonely-channel");

        await PublishEvent(new DiscordChannelEvent
        {
            EventType = DiscordChannelEventType.ChannelDeleted,
            Timestamp = DateTimeOffset.UtcNow,
            GuildId = "100000000000000013",
            ChannelId = channelId,
            ChannelGroupId = groupId,
            Name = "lonely-channel"
        });

        using var db = _factory.CreateDbContext();
        var group = await db.ChannelGroups.FirstOrDefaultAsync(g => g.DiscordId == groupId);
        Assert.NotNull(group);
        Assert.False(group.IsArchived, "Group must NOT be auto-archived when last channel is deleted");
    }

    // ──────────────────────────────────────────────────────────────
    // D-14: Multiple rapid channel deletions
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task MultipleChannelsDeleted_Rapidly_AllArchived()
    {
        var groupId = $"del-d14-grp-{Guid.NewGuid():N}";
        var channels = Enumerable.Range(0, 4)
            .Select(i => (Id: $"del-d14-ch{i}-{Guid.NewGuid():N}", Name: $"rapid-{i}"))
            .ToList();

        await SyncGroupWithChannels(groupId, "d14-village",
            channels.Select(c => (c.Id, c.Name)).ToArray());

        var subscriber = _factory.Redis.GetSubscriber();
        foreach (var (id, name) in channels)
        {
            await subscriber.PublishAsync(
                RedisChannel.Literal(RedisChannels.DiscordChannel),
                new DiscordChannelEvent
                {
                    EventType = DiscordChannelEventType.ChannelDeleted,
                    Timestamp = DateTimeOffset.UtcNow,
                    GuildId = "100000000000000014",
                    ChannelId = id,
                    ChannelGroupId = groupId,
                    Name = name
                }.ToJson());
        }

        await Task.Delay(2000);

        using var db = _factory.CreateDbContext();
        var dbChannels = await db.Channels
            .Where(c => channels.Select(x => x.Id).Contains(c.DiscordId))
            .ToListAsync();

        Assert.Equal(4, dbChannels.Count);
        Assert.All(dbChannels, ch =>
            Assert.True(ch.IsArchived, $"Channel '{ch.Name}' should be archived"));
    }

    // ──────────────────────────────────────────────────────────────
    // Group deleted with zero channels
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task ChannelGroupDeleted_WithZeroChannels_ArchivesCleanly()
    {
        var groupId = $"del-empty-grp-{Guid.NewGuid():N}";
        var syncRequest = new
        {
            guildId = "100000000000000020",
            channelGroups = new[]
            {
                new
                {
                    discordId = groupId,
                    name = "empty-village",
                    position = 0,
                    channels = Array.Empty<object>()
                }
            }
        };
        await _client.PostAsJsonAsync("/api/mappings/sync", syncRequest, JsonOptions);

        await PublishEvent(new DiscordChannelEvent
        {
            EventType = DiscordChannelEventType.ChannelGroupDeleted,
            Timestamp = DateTimeOffset.UtcNow,
            GuildId = "100000000000000020",
            ChannelGroupId = groupId,
            Name = "empty-village"
        });

        using var db = _factory.CreateDbContext();
        var group = await db.ChannelGroups
            .Include(g => g.Channels)
            .FirstOrDefaultAsync(g => g.DiscordId == groupId);

        Assert.NotNull(group);
        Assert.True(group.IsArchived);
        Assert.Empty(group.Channels);
    }

    // ──────────────────────────────────────────────────────────────
    // Mixed create and delete — verify mixed state
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task MixedCreateAndDelete_CorrectActiveAndArchivedCounts()
    {
        var groupId = $"del-mix-grp-{Guid.NewGuid():N}";
        var ch1 = $"del-mix-ch1-{Guid.NewGuid():N}";
        var ch2 = $"del-mix-ch2-{Guid.NewGuid():N}";
        var ch3 = $"del-mix-ch3-{Guid.NewGuid():N}";

        await SyncGroupWithChannels(groupId, "mix-village",
            (ch1, "keep-1"), (ch2, "delete-me"), (ch3, "keep-2"));

        await PublishEvent(new DiscordChannelEvent
        {
            EventType = DiscordChannelEventType.ChannelDeleted,
            Timestamp = DateTimeOffset.UtcNow,
            GuildId = "100000000000000030",
            ChannelId = ch2,
            ChannelGroupId = groupId,
            Name = "delete-me"
        });

        using var db = _factory.CreateDbContext();
        var group = await db.ChannelGroups
            .Include(g => g.Channels)
            .FirstOrDefaultAsync(g => g.DiscordId == groupId);

        Assert.NotNull(group);
        Assert.False(group.IsArchived);
        Assert.Equal(2, group.Channels.Count(c => !c.IsArchived));
        Assert.Equal(1, group.Channels.Count(c => c.IsArchived));
    }

    // ──────────────────────────────────────────────────────────────
    // Re-sync after deletion doesn't resurrect archived channels
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task PostSync_AfterDeletion_DoesNotResurrectArchivedChannel()
    {
        var groupId = $"del-resync-grp-{Guid.NewGuid():N}";
        var channelId = $"del-resync-ch-{Guid.NewGuid():N}";

        await SyncSingleGroupWithChannel(groupId, "resync-village", channelId, "will-be-archived");

        await PublishEvent(new DiscordChannelEvent
        {
            EventType = DiscordChannelEventType.ChannelDeleted,
            Timestamp = DateTimeOffset.UtcNow,
            GuildId = "100000000000000040",
            ChannelId = channelId,
            ChannelGroupId = groupId,
            Name = "will-be-archived"
        });

        using var dbBefore = _factory.CreateDbContext();
        var before = await dbBefore.Channels.FirstOrDefaultAsync(c => c.DiscordId == channelId);
        Assert.NotNull(before);
        Assert.True(before.IsArchived);

        var syncRequest = new
        {
            guildId = "100000000000000040",
            channelGroups = new[]
            {
                new
                {
                    discordId = groupId,
                    name = "resync-village",
                    position = 0,
                    channels = new[]
                    {
                        new { discordId = channelId, name = "resurrected?", position = 0 }
                    }
                }
            }
        };

        await _client.PostAsJsonAsync("/api/mappings/sync", syncRequest, JsonOptions);

        using var dbAfter = _factory.CreateDbContext();
        var after = await dbAfter.Channels.FirstOrDefaultAsync(c => c.DiscordId == channelId);
        Assert.NotNull(after);
        Assert.True(after.IsArchived,
            "Sync should not clear IsArchived — archived channels stay archived");
    }

    // ──────────────────────────────────────────────────────────────
    // Deletion of channel with null building coordinates
    // ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task ChannelDeleted_WithNullBuildingCoords_ArchivesCleanly()
    {
        var groupId = $"del-null-grp-{Guid.NewGuid():N}";
        var channelId = $"del-null-ch-{Guid.NewGuid():N}";
        await SyncSingleGroupWithChannel(groupId, "null-coords-village", channelId, "no-building-yet");

        using var dbCheck = _factory.CreateDbContext();
        var ch = await dbCheck.Channels.FirstOrDefaultAsync(c => c.DiscordId == channelId);
        Assert.NotNull(ch);
        Assert.Null(ch.BuildingX);
        Assert.Null(ch.BuildingZ);

        await PublishEvent(new DiscordChannelEvent
        {
            EventType = DiscordChannelEventType.ChannelDeleted,
            Timestamp = DateTimeOffset.UtcNow,
            GuildId = "100000000000000050",
            ChannelId = channelId,
            ChannelGroupId = groupId,
            Name = "no-building-yet"
        });

        using var db = _factory.CreateDbContext();
        var channel = await db.Channels.FirstOrDefaultAsync(c => c.DiscordId == channelId);
        Assert.NotNull(channel);
        Assert.True(channel.IsArchived);
        Assert.Null(channel.BuildingX);
        Assert.Null(channel.BuildingZ);
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
            guildId = "100000000000000000",
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
            guildId = "100000000000000000",
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
