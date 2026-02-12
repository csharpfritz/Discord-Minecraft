using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Bridge.Api.Tests.Infrastructure;
using Bridge.Data;
using Bridge.Data.Entities;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

namespace Bridge.Api.Tests;

/// <summary>
/// Tests for Bridge API HTTP endpoints: sync, villages, buildings, player link.
/// </summary>
public sealed class ApiEndpointTests : IClassFixture<BridgeApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly BridgeApiFactory _factory;
    private readonly HttpClient _client;

    public ApiEndpointTests(BridgeApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task PostSync_CreatesVillagesWithCoordinates()
    {
        // Arrange
        var syncRequest = new
        {
            guildId = "999999999999999999",
            channelGroups = new[]
            {
                new
                {
                    discordId = $"sync-grp-{Guid.NewGuid():N}",
                    name = "sync-village",
                    position = 0,
                    channels = new[]
                    {
                        new { discordId = $"sync-ch-{Guid.NewGuid():N}", name = "lobby", position = 0 },
                        new { discordId = $"sync-ch-{Guid.NewGuid():N}", name = "chat", position = 1 }
                    }
                }
            }
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/mappings/sync", syncRequest, JsonOptions);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(result.GetProperty("created").GetInt32() >= 3, "Expected at least 3 created (1 group + 2 channels)");

        // Verify via DB
        using var db = _factory.CreateDbContext();
        var group = await db.ChannelGroups.FirstOrDefaultAsync(g => g.DiscordId == syncRequest.channelGroups[0].discordId);
        Assert.NotNull(group);
        Assert.Equal("sync-village", group.Name);

        // Verify coordinates follow the grid formula
        int expectedCol = group.VillageIndex % WorldConstants.GridColumns;
        int expectedRow = group.VillageIndex / WorldConstants.GridColumns;
        Assert.Equal(expectedCol * WorldConstants.VillageSpacing, group.CenterX);
        Assert.Equal(expectedRow * WorldConstants.VillageSpacing, group.CenterZ);
    }

    [Fact]
    public async Task GetVillages_ReturnsCreatedVillages()
    {
        // Arrange — create a village via sync
        var groupDiscordId = $"villages-get-{Guid.NewGuid():N}";
        var syncRequest = new
        {
            guildId = "888888888888888888",
            channelGroups = new[]
            {
                new
                {
                    discordId = groupDiscordId,
                    name = "get-test-village",
                    position = 0,
                    channels = Array.Empty<object>()
                }
            }
        };

        await _client.PostAsJsonAsync("/api/mappings/sync", syncRequest, JsonOptions);

        // Act
        var response = await _client.GetAsync("/api/villages");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var villages = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(villages.GetArrayLength() > 0, "Expected at least one village");

        var found = false;
        foreach (var v in villages.EnumerateArray())
        {
            if (v.GetProperty("discordId").GetString() == groupDiscordId)
            {
                Assert.Equal("get-test-village", v.GetProperty("name").GetString());
                found = true;
                break;
            }
        }
        Assert.True(found, $"Village with discordId '{groupDiscordId}' not found in GET /api/villages response");
    }

    [Fact]
    public async Task GetVillageBuildings_ReturnsCorrectBuildings()
    {
        // Arrange — create a village with channels via sync
        var groupDiscordId = $"bld-grp-{Guid.NewGuid():N}";
        var channelDiscordId1 = $"bld-ch1-{Guid.NewGuid():N}";
        var channelDiscordId2 = $"bld-ch2-{Guid.NewGuid():N}";

        var syncRequest = new
        {
            guildId = "777777777777777777",
            channelGroups = new[]
            {
                new
                {
                    discordId = groupDiscordId,
                    name = "building-test",
                    position = 0,
                    channels = new[]
                    {
                        new { discordId = channelDiscordId1, name = "room-a", position = 0 },
                        new { discordId = channelDiscordId2, name = "room-b", position = 1 }
                    }
                }
            }
        };

        await _client.PostAsJsonAsync("/api/mappings/sync", syncRequest, JsonOptions);

        // Look up the group to get its ID
        using var db = _factory.CreateDbContext();
        var group = await db.ChannelGroups.FirstOrDefaultAsync(g => g.DiscordId == groupDiscordId);
        Assert.NotNull(group);

        // Act
        var response = await _client.GetAsync($"/api/villages/{group.Id}/buildings");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var buildings = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, buildings.GetArrayLength());

        var names = new List<string>();
        foreach (var b in buildings.EnumerateArray())
        {
            names.Add(b.GetProperty("name").GetString()!);
            Assert.False(b.GetProperty("isArchived").GetBoolean());
        }
        Assert.Contains("room-a", names);
        Assert.Contains("room-b", names);
    }

    [Fact]
    public async Task GetVillageBuildings_NonExistentVillage_ReturnsNotFound()
    {
        var response = await _client.GetAsync("/api/villages/99999/buildings");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PostPlayerLink_ReturnsCodeAndStoresInRedis()
    {
        // Arrange
        var discordUserId = "user-" + Guid.NewGuid().ToString("N");
        var request = new { discordId = discordUserId };

        // Act
        var response = await _client.PostAsJsonAsync("/api/players/link", request, JsonOptions);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        var code = result.GetProperty("code").GetString();
        Assert.NotNull(code);
        Assert.Equal(6, code.Length);
        Assert.Matches("^[A-Z0-9]{6}$", code);

        // Verify the code is stored in Redis with the discord ID
        var redisDb = _factory.Redis.GetDatabase();
        var storedDiscordId = await redisDb.StringGetAsync($"link:{code}");
        Assert.Equal(discordUserId, storedDiscordId.ToString());
    }

    [Fact]
    public async Task PostSync_MultipleGroups_AssignsDifferentCoordinates()
    {
        // Arrange — sync two groups at once
        var group1Id = $"multi-g1-{Guid.NewGuid():N}";
        var group2Id = $"multi-g2-{Guid.NewGuid():N}";

        var syncRequest = new
        {
            guildId = "666666666666666666",
            channelGroups = new[]
            {
                new
                {
                    discordId = group1Id,
                    name = "multi-village-1",
                    position = 0,
                    channels = Array.Empty<object>()
                },
                new
                {
                    discordId = group2Id,
                    name = "multi-village-2",
                    position = 1,
                    channels = Array.Empty<object>()
                }
            }
        };

        // Act
        await _client.PostAsJsonAsync("/api/mappings/sync", syncRequest, JsonOptions);

        // Assert — groups should have different coordinates
        using var db = _factory.CreateDbContext();
        var g1 = await db.ChannelGroups.FirstOrDefaultAsync(g => g.DiscordId == group1Id);
        var g2 = await db.ChannelGroups.FirstOrDefaultAsync(g => g.DiscordId == group2Id);

        Assert.NotNull(g1);
        Assert.NotNull(g2);
        Assert.NotEqual((g1.CenterX, g1.CenterZ), (g2.CenterX, g2.CenterZ));
    }

    [Fact]
    public async Task Health_ReturnsHealthy()
    {
        var response = await _client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
