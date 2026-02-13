using System.Text.Json;
using Bridge.Data;
using Bridge.Data.Entities;
using Bridge.Data.Jobs;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;
using WorldGen.Worker.Generators;
using WorldGen.Worker.Models;

namespace WorldGen.Worker;

public sealed class WorldGenJobProcessor(
    IConnectionMultiplexer redis,
    IServiceScopeFactory scopeFactory,
    IVillageGenerator villageGenerator,
    IBuildingGenerator buildingGenerator,
    IBuildingArchiver buildingArchiver,
    ITrackGenerator trackGenerator,
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
                var result = await db.ListRightPopAsync(RedisQueues.WorldGen);
                if (result.IsNullOrEmpty)
                {
                    await Task.Delay(500, stoppingToken);
                    continue;
                }

                var job = WorldGenJob.FromJson(result!);
                if (job is null)
                {
                    logger.LogWarning("Failed to deserialize job from queue: {Raw}", (string?)result);
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
            await DispatchJobAsync(job, ct);

            genJob.Status = GenerationJobStatus.Completed;
            genJob.CompletedAt = DateTime.UtcNow;
            await dbContext.SaveChangesAsync(ct);

            logger.LogInformation("Job {JobId} completed successfully", job.JobId);

            // After village creation completes, enqueue track jobs to all existing villages
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
                    Name: buildingPayload.ChannelName);
                await buildingGenerator.GenerateAsync(buildingRequest, ct);
                break;

            case WorldGenJobType.UpdateBuilding:
                logger.LogWarning("UpdateBuilding not yet implemented (Sprint 3 scope), JobId={JobId}", job.JobId);
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
    /// After a village is fully built, enqueue CreateTrack jobs connecting it to every other
    /// non-archived village. The first village gets no tracks (no destinations yet).
    /// </summary>
    private async Task EnqueueTrackJobsForNewVillageAsync(
        WorldGenJob completedJob, BridgeDbContext dbContext, IDatabase redisDb, CancellationToken ct)
    {
        var villagePayload = JsonSerializer.Deserialize<VillageJobPayload>(completedJob.Payload, PayloadOptions)
            ?? throw new InvalidOperationException("Failed to deserialize VillageJobPayload for track routing");

        var newGroupId = villagePayload.ChannelGroupId;

        // Find all other non-archived villages
        var existingVillages = await dbContext.ChannelGroups
            .Where(g => g.Id != newGroupId && !g.IsArchived)
            .ToListAsync(ct);

        if (existingVillages.Count == 0)
        {
            logger.LogInformation(
                "Village '{Name}' is the first village \u2014 no track connections needed",
                villagePayload.VillageName);
            return;
        }

        logger.LogInformation(
            "Enqueuing {Count} track job(s) connecting '{Name}' to existing villages",
            existingVillages.Count, villagePayload.VillageName);

        foreach (var existing in existingVillages)
        {
            var trackPayload = new TrackJobPayload(
                SourceChannelGroupId: newGroupId,
                DestinationChannelGroupId: existing.Id,
                SourceVillageName: villagePayload.VillageName,
                DestinationVillageName: existing.Name,
                SourceCenterX: villagePayload.CenterX,
                SourceCenterZ: villagePayload.CenterZ,
                DestCenterX: existing.CenterX,
                DestCenterZ: existing.CenterZ);

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
                "Enqueued CreateTrack job {JobId}: '{Source}' \u2194 '{Dest}'",
                genJob.Id, villagePayload.VillageName, existing.Name);
        }
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
                logger.LogInformation("Crossroads hub is ready — proceeding with job processing");
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
}
