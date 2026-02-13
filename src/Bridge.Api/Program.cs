using System.Text.Json;
using Bridge.Api;
using Bridge.Data;
using Bridge.Data.Entities;
using Bridge.Data.Jobs;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddNpgsqlDbContext<BridgeDbContext>("bridgedb");
builder.AddRedisClient("redis");
builder.Services.AddOpenApi();
builder.Services.AddHostedService<DiscordEventConsumer>();

var app = builder.Build();

// Apply EF Core migrations on startup to ensure database schema exists
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<BridgeDbContext>();
    await db.Database.MigrateAsync();
}

app.MapDefaultEndpoints();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapGet("/health", () => Results.Ok("healthy"));

// POST /api/mappings/sync — Full Discord→DB sync
app.MapPost("/api/mappings/sync", async (SyncRequest request, BridgeDbContext db, IConnectionMultiplexer redis) =>
{
    var redisDb = redis.GetDatabase();
    int created = 0, updated = 0;

    foreach (var groupDto in request.ChannelGroups)
    {
        var group = await db.ChannelGroups
            .Include(g => g.Channels)
            .FirstOrDefaultAsync(g => g.DiscordId == groupDto.DiscordId);

        if (group is null)
        {
            var villageIndex = await db.ChannelGroups.CountAsync();
            // Skip grid cell (0,0) — reserved for Crossroads hub
            int gridIndex = villageIndex + 1;
            int col = gridIndex % WorldConstants.GridColumns;
            int row = gridIndex / WorldConstants.GridColumns;

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

            await db.SaveChangesAsync();

            // Create GenerationJob and enqueue to Redis
            var villagePayload = new VillageJobPayload(
                group.Id, group.VillageIndex, group.CenterX, group.CenterZ, group.Name);

            var villageJob = new GenerationJob
            {
                Type = WorldGenJobType.CreateVillage.ToString(),
                Payload = JsonSerializer.Serialize(villagePayload, jsonOptions),
                Status = GenerationJobStatus.Pending
            };
            db.GenerationJobs.Add(villageJob);
            await db.SaveChangesAsync();

            var villageWorldGenJob = new WorldGenJob
            {
                JobType = WorldGenJobType.CreateVillage,
                JobId = villageJob.Id,
                Payload = JsonSerializer.Serialize(villagePayload, jsonOptions)
            };
            await redisDb.ListLeftPushAsync(RedisQueues.WorldGen, villageWorldGenJob.ToJson());
        }
        else
        {
            group.Name = groupDto.Name;
            group.Position = groupDto.Position;
            updated++;

            await db.SaveChangesAsync();
        }

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

                await db.SaveChangesAsync();

                // Create GenerationJob and enqueue to Redis
                var buildingPayload = new BuildingJobPayload(
                    group.Id, channel.Id, group.VillageIndex, channel.BuildingIndex,
                    group.CenterX, group.CenterZ, channel.Name, group.Name);

                var buildingJob = new GenerationJob
                {
                    Type = WorldGenJobType.CreateBuilding.ToString(),
                    Payload = JsonSerializer.Serialize(buildingPayload, jsonOptions),
                    Status = GenerationJobStatus.Pending
                };
                db.GenerationJobs.Add(buildingJob);
                await db.SaveChangesAsync();

                var buildingWorldGenJob = new WorldGenJob
                {
                    JobType = WorldGenJobType.CreateBuilding,
                    JobId = buildingJob.Id,
                    Payload = JsonSerializer.Serialize(buildingPayload, jsonOptions)
                };
                await redisDb.ListLeftPushAsync(RedisQueues.WorldGen, buildingWorldGenJob.ToJson());
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

// GET /api/status — World stats (village count, building count)
app.MapGet("/api/status", async (BridgeDbContext db) =>
{
    var villageCount = await db.ChannelGroups.CountAsync(g => !g.IsArchived);
    var buildingCount = await db.Channels.CountAsync(c => !c.IsArchived);

    return Results.Ok(new
    {
        villageCount,
        buildingCount
    });
});

// GET /api/navigate/{discordChannelId} — Get village/building info for a channel
app.MapGet("/api/navigate/{discordChannelId}", async (string discordChannelId, BridgeDbContext db) =>
{
    var channel = await db.Channels
        .Include(c => c.ChannelGroup)
        .FirstOrDefaultAsync(c => c.DiscordId == discordChannelId);

    if (channel is null)
        return Results.NotFound(new { message = "No mapping found for this channel." });

    return Results.Ok(new
    {
        channelName = channel.Name,
        villageName = channel.ChannelGroup.Name,
        buildingIndex = channel.BuildingIndex,
        coordinateX = channel.BuildingX ?? channel.CoordinateX,
        coordinateY = WorldConstants.BaseY + 1,
        coordinateZ = channel.BuildingZ ?? channel.CoordinateZ,
        isArchived = channel.IsArchived,
        villageCenterX = channel.ChannelGroup.VillageX ?? channel.ChannelGroup.CenterX,
        villageCenterZ = channel.ChannelGroup.VillageZ ?? channel.ChannelGroup.CenterZ
    });
});

// GET /api/buildings/search?q={query} — Fuzzy search for buildings by channel name
app.MapGet("/api/buildings/search", async (string q, BridgeDbContext db) =>
{
    var matches = await db.Channels
        .Include(c => c.ChannelGroup)
        .Where(c => !c.IsArchived && c.Name.Contains(q))
        .OrderBy(c => c.Name)
        .Take(10)
        .Select(c => new
        {
            c.Id,
            c.Name,
            villageName = c.ChannelGroup.Name,
            c.BuildingIndex,
            villageCenterX = c.ChannelGroup.CenterX,
            villageCenterZ = c.ChannelGroup.CenterZ
        })
        .ToListAsync();

    return Results.Ok(matches);
});

// GET /api/buildings/{id}/spawn — Get teleport coordinates for a building
app.MapGet("/api/buildings/{id}/spawn", async (int id, BridgeDbContext db) =>
{
    var channel = await db.Channels
        .Include(c => c.ChannelGroup)
        .FirstOrDefaultAsync(c => c.Id == id);

    if (channel is null)
        return Results.NotFound();

    // Calculate building coordinates from village center + building index
    // Match the layout logic from BuildingGenerator
    int row = channel.BuildingIndex % 2;
    int positionInRow = channel.BuildingIndex / 2;
    int bx = channel.ChannelGroup.CenterX + (positionInRow - 3) * 24;
    int bz = row == 0
        ? channel.ChannelGroup.CenterZ - 20
        : channel.ChannelGroup.CenterZ + 20;

    return Results.Ok(new
    {
        x = bx,
        y = WorldConstants.BaseY + 1,
        z = bz + 11, // South entrance
        channelName = channel.Name,
        villageName = channel.ChannelGroup.Name
    });
});

// GET /api/crossroads — Returns info about the Crossroads hub
app.MapGet("/api/crossroads", async (IConnectionMultiplexer redis, IConfiguration configuration) =>
{
    var db = redis.GetDatabase();
    var ready = await db.KeyExistsAsync("crossroads:ready");
    var generatedAt = ready ? (string?)await db.StringGetAsync("crossroads:ready") : null;
    var blueMapBaseUrl = configuration["BlueMap:WebUrl"] ?? "http://localhost:8200";
    var blueMapUrl = $"{blueMapBaseUrl}/#world:0:0:0:64:0:0:0:0:flat";

    return Results.Ok(new
    {
        name = "Crossroads of the World",
        x = 0,
        z = 0,
        y = WorldConstants.BaseY + 1,
        ready,
        generatedAt,
        description = "The central hub connecting all villages via minecart tracks. Features a grand plaza with fountain, four tree-lined avenues, and station platforms for each village.",
        blueMapUrl
    });
});

// GET /api/crossroads/map-url — Returns the BlueMap URL centered on Crossroads
app.MapGet("/api/crossroads/map-url", (IConfiguration configuration) =>
{
    var blueMapBaseUrl = configuration["BlueMap:WebUrl"] ?? "http://localhost:8200";
    var url = $"{blueMapBaseUrl}/#world:0:0:0:64:0:0:0:0:flat";
    return Results.Ok(new { url });
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

// POST /api/buildings/{id}/pin — Pin a Discord message to a building's interior
app.MapPost("/api/buildings/{id}/pin", async (int id, PinRequest request, BridgeDbContext db, IConnectionMultiplexer redis) =>
{
    var channel = await db.Channels
        .Include(c => c.ChannelGroup)
        .FirstOrDefaultAsync(c => c.Id == id);

    if (channel is null)
        return Results.NotFound(new { message = "Building not found." });

    if (channel.IsArchived)
        return Results.BadRequest(new { message = "Cannot pin to an archived building." });

    var redisDb = redis.GetDatabase();

    var pinData = new PinData(request.Author, request.Content, request.Timestamp);
    var updatePayload = new UpdateBuildingJobPayload(
        channel.ChannelGroupId,
        channel.Id,
        channel.BuildingIndex,
        channel.ChannelGroup.CenterX,
        channel.ChannelGroup.CenterZ,
        channel.Name,
        pinData);

    var genJob = new GenerationJob
    {
        Type = WorldGenJobType.UpdateBuilding.ToString(),
        Payload = JsonSerializer.Serialize(updatePayload, jsonOptions),
        Status = GenerationJobStatus.Pending
    };
    db.GenerationJobs.Add(genJob);
    await db.SaveChangesAsync();

    var worldGenJob = new WorldGenJob
    {
        JobType = WorldGenJobType.UpdateBuilding,
        JobId = genJob.Id,
        Payload = JsonSerializer.Serialize(updatePayload, jsonOptions)
    };
    await redisDb.ListLeftPushAsync(RedisQueues.WorldGen, worldGenJob.ToJson());

    return Results.Accepted(value: new { jobId = genJob.Id, buildingId = id });
});

app.Run();

// Request DTOs
public record SyncRequest(string GuildId, List<SyncChannelGroup> ChannelGroups);
public record SyncChannelGroup(string DiscordId, string Name, int Position, List<SyncChannel> Channels);
public record SyncChannel(string DiscordId, string Name, int Position = 0);
public record LinkRequest(string DiscordId);
public record PinRequest(string Author, string Content, DateTimeOffset Timestamp);
