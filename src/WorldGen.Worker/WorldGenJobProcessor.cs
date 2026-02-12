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
    ILogger<WorldGenJobProcessor> logger) : BackgroundService
{
    private static readonly JsonSerializerOptions PayloadOptions = new(JsonSerializerDefaults.Web);
    private const int MaxRetries = 3;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var db = redis.GetDatabase();
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

            default:
                throw new InvalidOperationException($"Unknown job type: {job.JobType}");
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
            // Shutting down — job stays as Pending in DB for reconciliation on next startup
        }
    }
}
