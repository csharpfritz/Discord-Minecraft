using System.Text.Json;
using Bridge.Data;
using Bridge.Data.Entities;
using Bridge.Data.Events;
using Bridge.Data.Jobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Bridge.Api;

/// <summary>
/// Subscribes to Discord channel events on Redis pub/sub and processes them:
/// upserts entities in PostgreSQL and enqueues world generation jobs.
/// </summary>
public sealed class DiscordEventConsumer(
    IConnectionMultiplexer redis,
    IServiceScopeFactory scopeFactory,
    ILogger<DiscordEventConsumer> logger) : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var subscriber = redis.GetSubscriber();

        var channel = await subscriber.SubscribeAsync(RedisChannel.Literal(RedisChannels.DiscordChannel));

        channel.OnMessage(async msg =>
        {
            try
            {
                var json = msg.Message.ToString();
                var evt = DiscordChannelEvent.FromJson(json);
                if (evt is null)
                {
                    logger.LogWarning("Failed to deserialize Discord event: {Json}", json);
                    return;
                }

                logger.LogInformation("Processing {EventType} event for guild {GuildId}",
                    evt.EventType, evt.GuildId);

                await using var scope = scopeFactory.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<BridgeDbContext>();
                var redisDb = redis.GetDatabase();

                await HandleEventAsync(evt, db, redisDb);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error processing Discord event");
            }
        });

        // Keep the service alive until cancellation
        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Graceful shutdown
        }

        await subscriber.UnsubscribeAsync(RedisChannel.Literal(RedisChannels.DiscordChannel));
        logger.LogInformation("Discord event consumer stopped");
    }

    private async Task HandleEventAsync(DiscordChannelEvent evt, BridgeDbContext db, IDatabase redisDb)
    {
        switch (evt.EventType)
        {
            case DiscordChannelEventType.ChannelGroupCreated:
                await HandleChannelGroupCreatedAsync(evt, db, redisDb);
                break;

            case DiscordChannelEventType.ChannelGroupDeleted:
                await HandleChannelGroupDeletedAsync(evt, db, redisDb);
                break;

            case DiscordChannelEventType.ChannelCreated:
                await HandleChannelCreatedAsync(evt, db, redisDb);
                break;

            case DiscordChannelEventType.ChannelDeleted:
                await HandleChannelDeletedAsync(evt, db, redisDb);
                break;

            case DiscordChannelEventType.ChannelUpdated:
                await HandleChannelUpdatedAsync(evt, db, redisDb);
                break;

            default:
                logger.LogWarning("Unknown event type: {EventType}", evt.EventType);
                break;
        }
    }

    private async Task HandleChannelGroupCreatedAsync(
        DiscordChannelEvent evt, BridgeDbContext db, IDatabase redisDb)
    {
        var discordId = evt.ChannelGroupId ?? evt.ChannelId!;
        var name = evt.Name ?? evt.ChannelGroupName ?? "unnamed";

        var group = await db.ChannelGroups.FirstOrDefaultAsync(g => g.DiscordId == discordId);

        if (group is null)
        {
            var villageIndex = await db.ChannelGroups.CountAsync();
            // Skip grid cell (0,0) — reserved for Crossroads hub
            int gridIndex = villageIndex + 1;
            int col = gridIndex % WorldConstants.GridColumns;
            int row = gridIndex / WorldConstants.GridColumns;

            group = new ChannelGroup
            {
                Name = name,
                DiscordId = discordId,
                Position = evt.Position ?? 0,
                VillageIndex = villageIndex,
                CenterX = col * WorldConstants.VillageSpacing,
                CenterZ = row * WorldConstants.VillageSpacing
            };
            db.ChannelGroups.Add(group);
        }
        else
        {
            group.Name = name;
            group.Position = evt.Position ?? group.Position;
        }

        await db.SaveChangesAsync();

        // Create GenerationJob in DB and enqueue to Redis
        var payload = new VillageJobPayload(
            group.Id, group.VillageIndex, group.CenterX, group.CenterZ, group.Name);

        var job = new GenerationJob
        {
            Type = WorldGenJobType.CreateVillage.ToString(),
            Payload = JsonSerializer.Serialize(payload, JsonOptions),
            Status = GenerationJobStatus.Pending
        };
        db.GenerationJobs.Add(job);
        await db.SaveChangesAsync();

        var worldGenJob = new WorldGenJob
        {
            JobType = WorldGenJobType.CreateVillage,
            JobId = job.Id,
            Payload = JsonSerializer.Serialize(payload, JsonOptions)
        };
        await redisDb.ListLeftPushAsync(RedisQueues.WorldGen, worldGenJob.ToJson());

        logger.LogInformation("Enqueued CreateVillage job {JobId} for group {GroupName}",
            job.Id, group.Name);
    }

    private async Task HandleChannelGroupDeletedAsync(
        DiscordChannelEvent evt, BridgeDbContext db, IDatabase redisDb)
    {
        var discordId = evt.ChannelGroupId ?? evt.ChannelId!;
        var group = await db.ChannelGroups
            .Include(g => g.Channels)
            .FirstOrDefaultAsync(g => g.DiscordId == discordId);

        if (group is null)
        {
            logger.LogWarning("ChannelGroupDeleted for unknown group {DiscordId}", discordId);
            return;
        }

        group.IsArchived = true;
        foreach (var channel in group.Channels)
        {
            channel.IsArchived = true;
        }

        await db.SaveChangesAsync();

        // Enqueue ArchiveVillage job with all building info
        var buildings = group.Channels
            .Select(c => new ArchiveBuildingJobPayload(
                group.Id, c.Id, c.BuildingIndex, group.CenterX, group.CenterZ, c.Name))
            .ToList();

        var payload = new ArchiveVillageJobPayload(
            group.Id, group.CenterX, group.CenterZ, group.Name, buildings);

        var job = new GenerationJob
        {
            Type = WorldGenJobType.ArchiveVillage.ToString(),
            Payload = JsonSerializer.Serialize(payload, JsonOptions),
            Status = GenerationJobStatus.Pending
        };
        db.GenerationJobs.Add(job);
        await db.SaveChangesAsync();

        var worldGenJob = new WorldGenJob
        {
            JobType = WorldGenJobType.ArchiveVillage,
            JobId = job.Id,
            Payload = JsonSerializer.Serialize(payload, JsonOptions)
        };
        await redisDb.ListLeftPushAsync(RedisQueues.WorldGen, worldGenJob.ToJson());

        logger.LogInformation("Archived channel group {GroupName} and {Count} channels, enqueued ArchiveVillage job {JobId}",
            group.Name, group.Channels.Count, job.Id);
    }

    private async Task HandleChannelCreatedAsync(
        DiscordChannelEvent evt, BridgeDbContext db, IDatabase redisDb)
    {
        var channelDiscordId = evt.ChannelId!;
        var groupDiscordId = evt.ChannelGroupId!;
        var name = evt.Name ?? "unnamed";

        // Ensure the parent group exists (upsert for out-of-order delivery)
        var group = await db.ChannelGroups.FirstOrDefaultAsync(g => g.DiscordId == groupDiscordId);
        if (group is null)
        {
            var villageIndex = await db.ChannelGroups.CountAsync();
            // Skip grid cell (0,0) — reserved for Crossroads hub
            int gridIndex = villageIndex + 1;
            int col = gridIndex % WorldConstants.GridColumns;
            int row = gridIndex / WorldConstants.GridColumns;

            group = new ChannelGroup
            {
                Name = evt.ChannelGroupName ?? "auto-created",
                DiscordId = groupDiscordId,
                Position = 0,
                VillageIndex = villageIndex,
                CenterX = col * WorldConstants.VillageSpacing,
                CenterZ = row * WorldConstants.VillageSpacing
            };
            db.ChannelGroups.Add(group);
            await db.SaveChangesAsync();

            logger.LogInformation("Auto-created channel group {DiscordId} for out-of-order ChannelCreated",
                groupDiscordId);
        }

        var channel = await db.Channels.FirstOrDefaultAsync(c => c.DiscordId == channelDiscordId);

        if (channel is null)
        {
            var maxIndex = await db.Channels
                .Where(c => c.ChannelGroupId == group.Id)
                .Select(c => (int?)c.BuildingIndex)
                .MaxAsync();
            var buildingIndex = (maxIndex ?? -1) + 1;

            channel = new Channel
            {
                Name = name,
                DiscordId = channelDiscordId,
                ChannelGroupId = group.Id,
                BuildingIndex = buildingIndex,
                Topic = evt.Topic
            };
            db.Channels.Add(channel);
        }
        else
        {
            channel.Name = name;
        }

        await db.SaveChangesAsync();

        // Create GenerationJob and enqueue
        var payload = new BuildingJobPayload(
            group.Id, channel.Id, group.VillageIndex, channel.BuildingIndex,
            group.CenterX, group.CenterZ, channel.Name, group.Name, channel.Topic);

        var job = new GenerationJob
        {
            Type = WorldGenJobType.CreateBuilding.ToString(),
            Payload = JsonSerializer.Serialize(payload, JsonOptions),
            Status = GenerationJobStatus.Pending
        };
        db.GenerationJobs.Add(job);
        await db.SaveChangesAsync();

        var worldGenJob = new WorldGenJob
        {
            JobType = WorldGenJobType.CreateBuilding,
            JobId = job.Id,
            Payload = JsonSerializer.Serialize(payload, JsonOptions)
        };
        await redisDb.ListLeftPushAsync(RedisQueues.WorldGen, worldGenJob.ToJson());

        logger.LogInformation("Enqueued CreateBuilding job {JobId} for channel {ChannelName}",
            job.Id, channel.Name);
    }

    private async Task HandleChannelDeletedAsync(
        DiscordChannelEvent evt, BridgeDbContext db, IDatabase redisDb)
    {
        var channelDiscordId = evt.ChannelId!;
        var channel = await db.Channels
            .Include(c => c.ChannelGroup)
            .FirstOrDefaultAsync(c => c.DiscordId == channelDiscordId);

        if (channel is null)
        {
            logger.LogWarning("ChannelDeleted for unknown channel {DiscordId}", channelDiscordId);
            return;
        }

        channel.IsArchived = true;
        await db.SaveChangesAsync();

        // Enqueue ArchiveBuilding job
        var group = channel.ChannelGroup;
        var payload = new ArchiveBuildingJobPayload(
            group.Id, channel.Id, channel.BuildingIndex,
            group.CenterX, group.CenterZ, channel.Name);

        var job = new GenerationJob
        {
            Type = WorldGenJobType.ArchiveBuilding.ToString(),
            Payload = JsonSerializer.Serialize(payload, JsonOptions),
            Status = GenerationJobStatus.Pending
        };
        db.GenerationJobs.Add(job);
        await db.SaveChangesAsync();

        var worldGenJob = new WorldGenJob
        {
            JobType = WorldGenJobType.ArchiveBuilding,
            JobId = job.Id,
            Payload = JsonSerializer.Serialize(payload, JsonOptions)
        };
        await redisDb.ListLeftPushAsync(RedisQueues.WorldGen, worldGenJob.ToJson());

        logger.LogInformation("Archived channel {ChannelName}, enqueued ArchiveBuilding job {JobId}",
            channel.Name, job.Id);
    }

    private async Task HandleChannelUpdatedAsync(
        DiscordChannelEvent evt, BridgeDbContext db, IDatabase redisDb)
    {
        var channelDiscordId = evt.ChannelId!;
        var newName = evt.NewName ?? evt.Name ?? "unnamed";

        var channel = await db.Channels
            .Include(c => c.ChannelGroup)
            .FirstOrDefaultAsync(c => c.DiscordId == channelDiscordId);

        if (channel is null)
        {
            logger.LogWarning("ChannelUpdated for unknown channel {DiscordId}", channelDiscordId);
            return;
        }

        channel.Name = newName;
        channel.Topic = evt.Topic ?? channel.Topic;
        await db.SaveChangesAsync();

        // Enqueue UpdateBuilding job for sign updates
        var group = channel.ChannelGroup;
        var payload = new BuildingJobPayload(
            group.Id, channel.Id, group.VillageIndex, channel.BuildingIndex,
            group.CenterX, group.CenterZ, channel.Name, group.Name, channel.Topic);

        var job = new GenerationJob
        {
            Type = WorldGenJobType.UpdateBuilding.ToString(),
            Payload = JsonSerializer.Serialize(payload, JsonOptions),
            Status = GenerationJobStatus.Pending
        };
        db.GenerationJobs.Add(job);
        await db.SaveChangesAsync();

        var worldGenJob = new WorldGenJob
        {
            JobType = WorldGenJobType.UpdateBuilding,
            JobId = job.Id,
            Payload = JsonSerializer.Serialize(payload, JsonOptions)
        };
        await redisDb.ListLeftPushAsync(RedisQueues.WorldGen, worldGenJob.ToJson());

        logger.LogInformation("Enqueued UpdateBuilding job {JobId} for channel {ChannelName}",
            job.Id, channel.Name);
    }
}
