using StackExchange.Redis;
using WorldGen.Worker.Generators;

namespace WorldGen.Worker;

/// <summary>
/// Background service that generates the Crossroads hub ONCE at startup,
/// before any village generation jobs are processed.
/// Sets the "crossroads:ready" Redis key when complete.
/// </summary>
public sealed class CrossroadsInitializationService(
    ICrossroadsGenerator crossroadsGenerator,
    IConnectionMultiplexer redis,
    ILogger<CrossroadsInitializationService> logger) : BackgroundService
{
    private const string ReadyKey = "crossroads:ready";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var db = redis.GetDatabase();

        // Check if Crossroads has already been generated (idempotent across restarts)
        if (await db.KeyExistsAsync(ReadyKey))
        {
            logger.LogInformation("Crossroads already generated (Redis key '{Key}' exists), skipping", ReadyKey);
            return;
        }

        logger.LogInformation("Crossroads generation starting...");

        try
        {
            await crossroadsGenerator.GenerateAsync(stoppingToken);

            await db.StringSetAsync(ReadyKey, DateTime.UtcNow.ToString("O"));
            logger.LogInformation("Crossroads generation complete — '{Key}' flag set in Redis", ReadyKey);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            logger.LogWarning("Crossroads generation cancelled during shutdown");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Crossroads generation failed — job processor will wait until retry succeeds");
            throw; // Let the host restart the service
        }
    }
}
