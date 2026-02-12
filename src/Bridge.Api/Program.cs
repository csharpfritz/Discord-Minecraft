using Bridge.Api;
using Bridge.Data;
using Bridge.Data.Entities;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddNpgsqlDbContext<BridgeDbContext>("bridgedb");
builder.AddRedisClient("redis");
builder.Services.AddOpenApi();
builder.Services.AddHostedService<DiscordEventConsumer>();

var app = builder.Build();

app.MapDefaultEndpoints();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapGet("/health", () => Results.Ok("healthy"));

// POST /api/mappings/sync — Full Discord→DB sync
app.MapPost("/api/mappings/sync", async (SyncRequest request, BridgeDbContext db) =>
{
    int created = 0, updated = 0;

    foreach (var groupDto in request.ChannelGroups)
    {
        var group = await db.ChannelGroups
            .Include(g => g.Channels)
            .FirstOrDefaultAsync(g => g.DiscordId == groupDto.DiscordId);

        if (group is null)
        {
            var villageIndex = await db.ChannelGroups.CountAsync();
            int col = villageIndex % WorldConstants.GridColumns;
            int row = villageIndex / WorldConstants.GridColumns;

            group = new ChannelGroup
            {
                Name = groupDto.Name,
                DiscordId = groupDto.DiscordId,
                Position = groupDto.Position,
                VillageIndex = villageIndex,
                CenterX = col * WorldConstants.VillageSpacing,
                CenterZ = row * WorldConstants.VillageSpacing
            };
            db.ChannelGroups.Add(group);
            created++;
        }
        else
        {
            group.Name = groupDto.Name;
            group.Position = groupDto.Position;
            updated++;
        }

        await db.SaveChangesAsync();

        foreach (var channelDto in groupDto.Channels)
        {
            var channel = await db.Channels
                .FirstOrDefaultAsync(c => c.DiscordId == channelDto.DiscordId);

            if (channel is null)
            {
                var maxIndex = await db.Channels
                    .Where(c => c.ChannelGroupId == group.Id && !c.IsArchived)
                    .Select(c => (int?)c.BuildingIndex)
                    .MaxAsync();
                var buildingIndex = (maxIndex ?? -1) + 1;

                channel = new Channel
                {
                    Name = channelDto.Name,
                    DiscordId = channelDto.DiscordId,
                    ChannelGroupId = group.Id,
                    BuildingIndex = buildingIndex
                };
                db.Channels.Add(channel);
                created++;
            }
            else
            {
                channel.Name = channelDto.Name;
                updated++;
            }
        }

        await db.SaveChangesAsync();
    }

    return Results.Ok(new { created, updated });
});

// GET /api/villages — List all villages
app.MapGet("/api/villages", async (BridgeDbContext db) =>
{
    var villages = await db.ChannelGroups
        .Include(g => g.Channels)
        .OrderBy(g => g.VillageIndex)
        .Select(g => new
        {
            g.Id,
            g.Name,
            g.DiscordId,
            g.CenterX,
            g.CenterZ,
            BuildingCount = g.Channels.Count(c => !c.IsArchived),
            g.CreatedAt
        })
        .ToListAsync();

    return Results.Ok(villages);
});

// GET /api/villages/{id}/buildings — List buildings in a village
app.MapGet("/api/villages/{id}/buildings", async (int id, BridgeDbContext db) =>
{
    var group = await db.ChannelGroups.FindAsync(id);
    if (group is null)
        return Results.NotFound();

    var buildings = await db.Channels
        .Where(c => c.ChannelGroupId == id)
        .OrderBy(c => c.BuildingIndex)
        .Select(c => new
        {
            c.Id,
            c.Name,
            c.DiscordId,
            c.BuildingIndex,
            CoordinateX = c.BuildingX ?? c.CoordinateX,
            CoordinateZ = c.BuildingZ ?? c.CoordinateZ,
            c.IsArchived,
            c.CreatedAt
        })
        .ToListAsync();

    return Results.Ok(buildings);
});

// POST /api/players/link — Initiate account link
app.MapPost("/api/players/link", async (LinkRequest request, IConnectionMultiplexer redis) =>
{
    const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
    var code = new string(Enumerable.Range(0, 6)
        .Select(_ => chars[Random.Shared.Next(chars.Length)])
        .ToArray());

    var redisDb = redis.GetDatabase();
    await redisDb.StringSetAsync($"link:{code}", request.DiscordId, TimeSpan.FromMinutes(5));

    return Results.Ok(new { code });
});

app.Run();

// Request DTOs
public record SyncRequest(string GuildId, List<SyncChannelGroup> ChannelGroups);
public record SyncChannelGroup(string DiscordId, string Name, int Position, List<SyncChannel> Channels);
public record SyncChannel(string DiscordId, string Name, int Position = 0);
public record LinkRequest(string DiscordId);
