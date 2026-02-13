using System.Text.Json;
using Bridge.Data;
using Bridge.Data.Entities;
using Bridge.Data.Events;
using Bridge.Data.Jobs;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;
using WorldGen.Worker.Generators;
using WorldGen.Worker.Models;
using WorldGen.Worker.Services;

namespace WorldGen.Worker;

public sealed class WorldGenJobProcessor(
    IConnectionMultiplexer redis,
    IServiceScopeFactory scopeFactory,
    IVillageGenerator villageGenerator,
    IBuildingGenerator buildingGenerator,
    IBuildingArchiver buildingArchiver,
    ITrackGenerator trackGenerator,
    PinDisplayService pinDisplayService,
    MarkerService markerService,
    RconService rconService,
    ILogger<WorldGenJobProcessor> logger) : BackgroundService
{
    private static readonly JsonSerializerOptions PayloadOptions = new(JsonSerializerDefaults.Web);
    private const int MaxRetries = 3;
    private const string CrossroadsReadyKey = "crossroads:ready";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var db = redis.GetDatabase();

        // Wait for Crossroads hub to be generated before processing any jobs
        await WaitForCrossroadsAsync(db, stoppingToken);

        logger.LogInformation("WorldGen job processor started, listening on {Queue}", RedisQueues.WorldGen);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var job = await PopClosestJobAsync(db, stoppingToken);
                if (job is null)
                {
                    await Task.Delay(500, stoppingToken);
                    continue;
                }

                await ProcessJobAsync(job, db, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unexpected error in job processor loop");
                await Task.Delay(1000, stoppingToken);
            }
        }

        logger.LogInformation("WorldGen job processor shutting down");
    }

    private async Task ProcessJobAsync(WorldGenJob job, IDatabase db, CancellationToken ct)
    {
        logger.LogInformation("Processing job {JobId} of type {JobType}", job.JobId, job.JobType);

        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BridgeDbContext>();

        var genJob = await dbContext.GenerationJobs.FindAsync([job.JobId], ct);
        if (genJob is null)
        {
            logger.LogWarning("GenerationJob {JobId} not found in database, skipping", job.JobId);
            return;
        }

        genJob.Status = GenerationJobStatus.InProgress;
        genJob.StartedAt = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(ct);

        try
        {
            await BroadcastBuildStartAsync(job, ct);

            await DispatchJobAsync(job, ct);

            await BroadcastBuildCompleteAsync(job, ct);

            // Set BlueMap markers (best-effort, never fails the job)
            await SetMarkersForJobAsync(job);

            genJob.Status = GenerationJobStatus.Completed;
            genJob.CompletedAt = DateTime.UtcNow;
            await dbContext.SaveChangesAsync(ct);

            logger.LogInformation("Job {JobId} completed successfully", job.JobId);

            // Publish world activity event for Discord feed (best-effort)
            await PublishActivityEventAsync(job);

            // After village creation completes, enqueue track job to Crossroads
            if (job.JobType == WorldGenJobType.CreateVillage)
            {
                await EnqueueTrackJobsForNewVillageAsync(job, dbContext, db, ct);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Job {JobId} failed (attempt {Attempt})", job.JobId, genJob.RetryCount + 1);

            genJob.RetryCount++;

            if (genJob.RetryCount < MaxRetries)
            {
                genJob.Status = GenerationJobStatus.Pending;
                await dbContext.SaveChangesAsync(ct);

                var delaySeconds = (int)Math.Pow(2, genJob.RetryCount);
                _ = ReenqueueAfterDelayAsync(job, db, delaySeconds, ct);

                logger.LogInformation("Job {JobId} re-enqueued with {Delay}s backoff (retry {Retry}/{Max})",
                    job.JobId, delaySeconds, genJob.RetryCount, MaxRetries);
            }
            else
            {
                genJob.Status = GenerationJobStatus.Failed;
                genJob.ErrorMessage = ex.Message.Length > 2000 ? ex.Message[..2000] : ex.Message;
                await dbContext.SaveChangesAsync(ct);

                logger.LogError("Job {JobId} permanently failed after {Max} retries", job.JobId, MaxRetries);
            }
        }
    }

    private async Task DispatchJobAsync(WorldGenJob job, CancellationToken ct)
    {
        switch (job.JobType)
        {
            case WorldGenJobType.CreateVillage:
                var villagePayload = JsonSerializer.Deserialize<VillageJobPayload>(job.Payload, PayloadOptions)
                    ?? throw new InvalidOperationException("Failed to deserialize VillageJobPayload");
                var villageRequest = new VillageGenerationRequest(
                    JobId: job.JobId,
                    ChannelGroupId: villagePayload.ChannelGroupId,
                    Name: villagePayload.VillageName,
                    VillageIndex: villagePayload.VillageIndex,
                    CenterX: villagePayload.CenterX,
                    CenterZ: villagePayload.CenterZ);
                await villageGenerator.GenerateAsync(villageRequest, ct);
                break;

            case WorldGenJobType.CreateBuilding:
                var buildingPayload = JsonSerializer.Deserialize<BuildingJobPayload>(job.Payload, PayloadOptions)
                    ?? throw new InvalidOperationException("Failed to deserialize BuildingJobPayload");
                var buildingRequest = new BuildingGenerationRequest(
                    JobId: job.JobId,
                    ChannelGroupId: buildingPayload.ChannelGroupId,
                    ChannelId: buildingPayload.ChannelId,
                    VillageCenterX: buildingPayload.CenterX,
                    VillageCenterZ: buildingPayload.CenterZ,
                    BuildingIndex: buildingPayload.BuildingIndex,
                    Name: buildingPayload.ChannelName,
                    ChannelTopic: buildingPayload.ChannelTopic);
                await buildingGenerator.GenerateAsync(buildingRequest, ct);
                break;

            case WorldGenJobType.UpdateBuilding:
                var updatePayload = JsonSerializer.Deserialize<UpdateBuildingJobPayload>(job.Payload, PayloadOptions)
                    ?? throw new InvalidOperationException("Failed to deserialize UpdateBuildingJobPayload");
                await pinDisplayService.DisplayPinAsync(updatePayload, ct);
                break;

            case WorldGenJobType.CreateTrack:
                var trackPayload = JsonSerializer.Deserialize<TrackJobPayload>(job.Payload, PayloadOptions)
                    ?? throw new InvalidOperationException("Failed to deserialize TrackJobPayload");
                var trackRequest = new TrackGenerationRequest(
                    JobId: job.JobId,
                    SourceChannelGroupId: trackPayload.SourceChannelGroupId,
                    DestinationChannelGroupId: trackPayload.DestinationChannelGroupId,
                    SourceVillageName: trackPayload.SourceVillageName,
                    DestinationVillageName: trackPayload.DestinationVillageName,
                    SourceCenterX: trackPayload.SourceCenterX,
                    SourceCenterZ: trackPayload.SourceCenterZ,
                    DestCenterX: trackPayload.DestCenterX,
                    DestCenterZ: trackPayload.DestCenterZ);
                await trackGenerator.GenerateAsync(trackRequest, ct);
                break;

            case WorldGenJobType.ArchiveBuilding:
                var archivePayload = JsonSerializer.Deserialize<ArchiveBuildingJobPayload>(job.Payload, PayloadOptions)
                    ?? throw new InvalidOperationException("Failed to deserialize ArchiveBuildingJobPayload");
                var archiveRequest = new BuildingArchiveRequest(
                    JobId: job.JobId,
                    ChannelGroupId: archivePayload.ChannelGroupId,
                    ChannelId: archivePayload.ChannelId,
                    BuildingIndex: archivePayload.BuildingIndex,
                    VillageCenterX: archivePayload.CenterX,
                    VillageCenterZ: archivePayload.CenterZ,
                    ChannelName: archivePayload.ChannelName);
                await buildingArchiver.ArchiveAsync(archiveRequest, ct);
                break;

            case WorldGenJobType.ArchiveVillage:
                var villageArchivePayload = JsonSerializer.Deserialize<ArchiveVillageJobPayload>(job.Payload, PayloadOptions)
                    ?? throw new InvalidOperationException("Failed to deserialize ArchiveVillageJobPayload");
                logger.LogInformation("Archiving village '{Name}' with {Count} buildings",
                    villageArchivePayload.VillageName, villageArchivePayload.Buildings.Count);
                foreach (var bldg in villageArchivePayload.Buildings)
                {
                    var bldgRequest = new BuildingArchiveRequest(
                        JobId: job.JobId,
                        ChannelGroupId: bldg.ChannelGroupId,
                        ChannelId: bldg.ChannelId,
                        BuildingIndex: bldg.BuildingIndex,
                        VillageCenterX: bldg.CenterX,
                        VillageCenterZ: bldg.CenterZ,
                        ChannelName: bldg.ChannelName);
                    await buildingArchiver.ArchiveAsync(bldgRequest, ct);
                }
                break;

            default:
                throw new InvalidOperationException($"Unknown job type: {job.JobType}");
        }
    }

    /// <summary>
    /// Publishes a world activity event to Redis pub/sub for the Discord feed. Best-effort — never throws.
    /// </summary>
    private async Task PublishActivityEventAsync(WorldGenJob job)
    {
        try
        {
            var activityEvent = job.JobType switch
            {
                WorldGenJobType.CreateVillage =>
                    JsonSerializer.Deserialize<VillageJobPayload>(job.Payload, PayloadOptions) is { } vp
                        ? new WorldActivityEvent { Type = "village_built", Name = vp.VillageName, X = vp.CenterX, Z = vp.CenterZ }
                        : null,
                WorldGenJobType.CreateBuilding =>
                    JsonSerializer.Deserialize<BuildingJobPayload>(job.Payload, PayloadOptions) is { } bp
                        ? new WorldActivityEvent { Type = "building_built", Name = bp.ChannelName, X = bp.CenterX, Z = bp.CenterZ }
                        : null,
                WorldGenJobType.CreateTrack =>
                    JsonSerializer.Deserialize<TrackJobPayload>(job.Payload, PayloadOptions) is { } tp
                        ? new WorldActivityEvent { Type = "track_built", Name = $"{tp.SourceVillageName} ↔ {tp.DestinationVillageName}", X = tp.SourceCenterX, Z = tp.SourceCenterZ }
                        : null,
                WorldGenJobType.ArchiveBuilding =>
                    JsonSerializer.Deserialize<ArchiveBuildingJobPayload>(job.Payload, PayloadOptions) is { } abp
                        ? new WorldActivityEvent { Type = "building_archived", Name = abp.ChannelName, X = abp.CenterX, Z = abp.CenterZ }
                        : null,
                _ => null
            };

            if (activityEvent is not null)
            {
                var subscriber = redis.GetSubscriber();
                await subscriber.PublishAsync(RedisChannel.Literal(RedisChannels.WorldActivity), activityEvent.ToJson());
                logger.LogDebug("Published world activity event: {Type} — {Name}", activityEvent.Type, activityEvent.Name);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to publish world activity event for job {JobId} — continuing", job.JobId);
        }
    }

    /// <summary>
    /// Sets BlueMap markers after successful generation. Best-effort — never throws.
    /// </summary>
    private async Task SetMarkersForJobAsync(WorldGenJob job)
    {
        try
        {
            switch (job.JobType)
            {
                case WorldGenJobType.CreateVillage:
                    var vp = JsonSerializer.Deserialize<VillageJobPayload>(job.Payload, PayloadOptions);
                    if (vp is not null)
                        await markerService.SetVillageMarkerAsync(
                            vp.ChannelGroupId.ToString(), vp.VillageName, vp.CenterX, vp.CenterZ, CancellationToken.None);
                    break;

                case WorldGenJobType.CreateBuilding:
                    var bp = JsonSerializer.Deserialize<BuildingJobPayload>(job.Payload, PayloadOptions);
                    if (bp is not null)
                    {
                        // Compute building position matching BuildingGenerator layout
                        int bx = bp.CenterX + (bp.BuildingIndex / 2 - 3) * 24;
                        int bz = bp.BuildingIndex % 2 == 0 ? bp.CenterZ - 20 : bp.CenterZ + 20;
                        await markerService.SetBuildingMarkerAsync(
                            bp.ChannelId.ToString(), bp.ChannelName, bx, bz, CancellationToken.None);
                    }
                    break;

                case WorldGenJobType.ArchiveBuilding:
                    var abp = JsonSerializer.Deserialize<ArchiveBuildingJobPayload>(job.Payload, PayloadOptions);
                    if (abp is not null)
                        await markerService.ArchiveBuildingMarkerAsync(abp.ChannelId.ToString(), CancellationToken.None);
                    break;

                case WorldGenJobType.ArchiveVillage:
                    var avp = JsonSerializer.Deserialize<ArchiveVillageJobPayload>(job.Payload, PayloadOptions);
                    if (avp is not null)
                        await markerService.ArchiveVillageMarkerAsync(avp.ChannelGroupId.ToString(), CancellationToken.None);
                    break;
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to set BlueMap marker for job {JobId} — continuing", job.JobId);
        }
    }

    /// <summary>
    /// After a village is fully built, enqueue a single CreateTrack job connecting it
    /// to the Crossroads hub at (0, 0). Hub-and-spoke topology: every village gets
    /// exactly one track to Crossroads.
    /// </summary>
    private async Task EnqueueTrackJobsForNewVillageAsync(
        WorldGenJob completedJob, BridgeDbContext dbContext, IDatabase redisDb, CancellationToken ct)
    {
        var villagePayload = JsonSerializer.Deserialize<VillageJobPayload>(completedJob.Payload, PayloadOptions)
            ?? throw new InvalidOperationException("Failed to deserialize VillageJobPayload for track routing");

        logger.LogInformation(
            "Enqueuing hub-and-spoke track job connecting '{Name}' to Crossroads",
            villagePayload.VillageName);

        var trackPayload = new TrackJobPayload(
            SourceChannelGroupId: villagePayload.ChannelGroupId,
            DestinationChannelGroupId: 0, // Crossroads is not a ChannelGroup
            SourceVillageName: villagePayload.VillageName,
            DestinationVillageName: "Crossroads",
            SourceCenterX: villagePayload.CenterX,
            SourceCenterZ: villagePayload.CenterZ,
            DestCenterX: 0,
            DestCenterZ: 0);

        var genJob = new GenerationJob
        {
            Type = WorldGenJobType.CreateTrack.ToString(),
            Payload = JsonSerializer.Serialize(trackPayload, PayloadOptions),
            Status = GenerationJobStatus.Pending
        };
        dbContext.GenerationJobs.Add(genJob);
        await dbContext.SaveChangesAsync(ct);

        var worldGenJob = new WorldGenJob
        {
            JobType = WorldGenJobType.CreateTrack,
            JobId = genJob.Id,
            Payload = JsonSerializer.Serialize(trackPayload, PayloadOptions)
        };
        await redisDb.ListLeftPushAsync(RedisQueues.WorldGen, worldGenJob.ToJson());

        logger.LogInformation(
            "Enqueued CreateTrack job {JobId}: '{Source}' \u2194 Crossroads",
            genJob.Id, villagePayload.VillageName);
    }

    /// <summary>
    /// Wait for the Crossroads hub to be generated before processing jobs.
    /// Polls the Redis key every 500ms, logging every 10th check.
    /// </summary>
    private async Task WaitForCrossroadsAsync(IDatabase db, CancellationToken ct)
    {
        int checks = 0;
        while (!ct.IsCancellationRequested)
        {
            if (await db.KeyExistsAsync(CrossroadsReadyKey))
            {
                logger.LogInformation("Crossroads hub is ready \u2014 proceeding with job processing");
                return;
            }

            checks++;
            if (checks % 10 == 0)
            {
                logger.LogInformation("Waiting for Crossroads hub generation... (check #{Check})", checks);
            }

            await Task.Delay(500, ct);
        }
    }

    private static async Task ReenqueueAfterDelayAsync(WorldGenJob job, IDatabase db, int delaySeconds, CancellationToken ct)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(delaySeconds), ct);
            await db.ListLeftPushAsync(RedisQueues.WorldGen, job.ToJson());
        }
        catch (OperationCanceledException)
        {
            // Shutting down \u2014 job stays as Pending in DB for reconciliation on next startup
        }
    }

    private async Task<WorldGenJob?> PopClosestJobAsync(IDatabase db, CancellationToken ct)
    {
        var length = await db.ListLengthAsync(RedisQueues.WorldGen);
        if (length == 0) return null;

        if (length == 1)
        {
            var single = await db.ListRightPopAsync(RedisQueues.WorldGen);
            return single.IsNullOrEmpty ? null : WorldGenJob.FromJson(single!);
        }

        // Peek all items, find the closest to spawn
        var items = await db.ListRangeAsync(RedisQueues.WorldGen, 0, -1);
        if (items.Length == 0) return null;

        int bestIndex = 0;
        double bestDistance = double.MaxValue;

        for (int i = 0; i < items.Length; i++)
        {
            var candidate = WorldGenJob.FromJson(items[i]!);
            if (candidate is null) continue;

            var (cx, cz) = GetJobCenter(candidate);
            double dist = Math.Sqrt((double)cx * cx + (double)cz * cz);
            if (dist < bestDistance)
            {
                bestDistance = dist;
                bestIndex = i;
            }
        }

        // Remove the selected item using LSET + LREM sentinel pattern
        var sentinel = "__PICKED__";
        await db.ListSetByIndexAsync(RedisQueues.WorldGen, bestIndex, sentinel);
        await db.ListRemoveAsync(RedisQueues.WorldGen, sentinel, 1);

        return WorldGenJob.FromJson(items[bestIndex]!);
    }

    private (int cx, int cz) GetJobCenter(WorldGenJob job)
    {
        try
        {
            return job.JobType switch
            {
                WorldGenJobType.CreateVillage =>
                    JsonSerializer.Deserialize<VillageJobPayload>(job.Payload, PayloadOptions) is { } vp
                        ? (vp.CenterX, vp.CenterZ) : (int.MaxValue, int.MaxValue),
                WorldGenJobType.CreateBuilding =>
                    JsonSerializer.Deserialize<BuildingJobPayload>(job.Payload, PayloadOptions) is { } bp
                        ? (bp.CenterX, bp.CenterZ) : (int.MaxValue, int.MaxValue),
                WorldGenJobType.CreateTrack =>
                    JsonSerializer.Deserialize<TrackJobPayload>(job.Payload, PayloadOptions) is { } tp
                        ? (tp.SourceCenterX, tp.SourceCenterZ) : (int.MaxValue, int.MaxValue),
                _ => (int.MaxValue, int.MaxValue)
            };
        }
        catch { return (int.MaxValue, int.MaxValue); }
    }

    private async Task BroadcastBuildStartAsync(WorldGenJob job, CancellationToken ct)
    {
        try
        {
            string? message = job.JobType switch
            {
                WorldGenJobType.CreateVillage => GetVillageName(job) is string name
                    ? $"tellraw @a [{{\"text\":\"⚒ Building village \",\"color\":\"yellow\"}},{{\"text\":\"{name}\",\"color\":\"gold\",\"bold\":true}},{{\"text\":\"...\",\"color\":\"yellow\"}}]"
                    : null,
                WorldGenJobType.CreateBuilding => GetBuildingName(job) is string name
                    ? $"tellraw @a [{{\"text\":\"🏗 Constructing \",\"color\":\"aqua\"}},{{\"text\":\"{name}\",\"color\":\"white\",\"bold\":true}},{{\"text\":\"...\",\"color\":\"aqua\"}}]"
                    : null,
                WorldGenJobType.CreateTrack => GetTrackNames(job) is (string src, string dst)
                    ? $"tellraw @a [{{\"text\":\"🚂 Laying tracks: \",\"color\":\"green\"}},{{\"text\":\"{src}\",\"color\":\"white\"}},{{\"text\":\" → \",\"color\":\"green\"}},{{\"text\":\"{dst}\",\"color\":\"white\"}}]"
                    : null,
                _ => null
            };
            if (message is not null)
                await rconService.SendCommandAsync(message, ct);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed to broadcast build start — continuing");
        }
    }

    private async Task BroadcastBuildCompleteAsync(WorldGenJob job, CancellationToken ct)
    {
        try
        {
            string? message = job.JobType switch
            {
                WorldGenJobType.CreateVillage => GetVillageName(job) is string name
                    ? $"tellraw @a [{{\"text\":\"✅ Village \",\"color\":\"green\"}},{{\"text\":\"{name}\",\"color\":\"gold\",\"bold\":true}},{{\"text\":\" is ready!\",\"color\":\"green\"}}]"
                    : null,
                WorldGenJobType.CreateBuilding => GetBuildingName(job) is string name
                    ? $"tellraw @a [{{\"text\":\"✅ \",\"color\":\"green\"}},{{\"text\":\"{name}\",\"color\":\"white\",\"bold\":true}},{{\"text\":\" construction complete!\",\"color\":\"green\"}}]"
                    : null,
                WorldGenJobType.CreateTrack => GetTrackNames(job) is (string src, string dst)
                    ? $"tellraw @a [{{\"text\":\"✅ Rail line open: \",\"color\":\"green\"}},{{\"text\":\"{src}\",\"color\":\"white\"}},{{\"text\":\" ↔ \",\"color\":\"green\"}},{{\"text\":\"{dst}\",\"color\":\"white\"}}]"
                    : null,
                _ => null
            };
            if (message is not null)
                await rconService.SendCommandAsync(message, ct);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed to broadcast build complete — continuing");
        }
    }

    private string? GetVillageName(WorldGenJob job)
    {
        try
        {
            return JsonSerializer.Deserialize<VillageJobPayload>(job.Payload, PayloadOptions)?.VillageName;
        }
        catch { return null; }
    }

    private string? GetBuildingName(WorldGenJob job)
    {
        try
        {
            return JsonSerializer.Deserialize<BuildingJobPayload>(job.Payload, PayloadOptions)?.ChannelName;
        }
        catch { return null; }
    }

    private (string src, string dst)? GetTrackNames(WorldGenJob job)
    {
        try
        {
            var tp = JsonSerializer.Deserialize<TrackJobPayload>(job.Payload, PayloadOptions);
            return tp is not null ? (tp.SourceVillageName, tp.DestinationVillageName) : null;
        }
        catch { return null; }
    }
}
