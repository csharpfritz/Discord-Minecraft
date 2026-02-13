using System.Net;
using Aspire.Hosting;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Polly;
using StackExchange.Redis;

namespace Acceptance.Tests.Infrastructure;

/// <summary>
/// Shared fixture that launches the full Aspire AppHost including:
/// - PostgreSQL
/// - Redis
/// - Bridge.Api
/// - WorldGen.Worker
/// - Minecraft container with BlueMap
///
/// Implements IAsyncLifetime to manage startup/teardown.
/// Uses xUnit's IAsyncLifetime pattern — tests wait for world to be ready.
/// </summary>
public sealed class FullStackFixture : IAsyncLifetime
{
    private DistributedApplication? _app;
    private HttpClient? _bridgeApiClient;
    private HttpClient? _blueMapClient;
    private IConnectionMultiplexer? _redis;

    /// <summary>
    /// How long to wait for the full stack to become ready.
    /// Minecraft/Paper can take 1-3 minutes on first start.
    /// </summary>
    public static readonly TimeSpan StartupTimeout = TimeSpan.FromMinutes(5);

    /// <summary>
    /// How long to wait for BlueMap to be ready after Minecraft starts.
    /// BlueMap renders take time; we just check the web server is up.
    /// </summary>
    public static readonly TimeSpan BlueMapReadyTimeout = TimeSpan.FromMinutes(3);

    /// <summary>
    /// HttpClient configured to talk to Bridge.Api.
    /// </summary>
    public HttpClient BridgeApiClient =>
        _bridgeApiClient ?? throw new InvalidOperationException("Fixture not initialized");

    /// <summary>
    /// HttpClient configured to talk to BlueMap's web server.
    /// </summary>
    public HttpClient BlueMapClient =>
        _blueMapClient ?? throw new InvalidOperationException("Fixture not initialized");

    /// <summary>
    /// Redis connection for pushing test events.
    /// </summary>
    public IConnectionMultiplexer Redis =>
        _redis ?? throw new InvalidOperationException("Fixture not initialized");

    /// <summary>
    /// The base URL of BlueMap web server.
    /// </summary>
    public string BlueMapBaseUrl { get; private set; } = string.Empty;

    /// <summary>
    /// The base URL of Bridge.Api.
    /// </summary>
    public string BridgeApiBaseUrl { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        using var startupCts = new CancellationTokenSource(StartupTimeout);

        try
        {
            var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.AppHost>(
                args: [],
                cancellationToken: startupCts.Token);

            // Provide dummy values for required secret parameters so the
            // AppHost builder doesn't hang waiting for interactive input.
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Parameters:discord-bot-token"] = "test-token-not-a-real-bot",
                ["Parameters:rcon-password"] = "test-rcon-password",
            });

            builder.Services.AddLogging(logging =>
            {
                logging.SetMinimumLevel(LogLevel.Information);
                logging.AddFilter("Aspire.Hosting", LogLevel.Warning);
            });

            _app = await builder.BuildAsync(startupCts.Token);

            Console.WriteLine("[FullStackFixture] Starting Aspire AppHost...");
            await _app.StartAsync(startupCts.Token);
            Console.WriteLine("[FullStackFixture] AppHost started");

            // Get the endpoint URLs for Bridge.Api
            var bridgeApiEndpoint = _app.GetEndpoint("bridge-api", "http");
            BridgeApiBaseUrl = bridgeApiEndpoint.ToString();
            _bridgeApiClient = new HttpClient { BaseAddress = new Uri(BridgeApiBaseUrl) };

            // Get the endpoint URL for BlueMap
            var blueMapEndpoint = _app.GetEndpoint("minecraft", "bluemap");
            BlueMapBaseUrl = blueMapEndpoint.ToString();
            _blueMapClient = new HttpClient { BaseAddress = new Uri(BlueMapBaseUrl) };

            // Get Redis connection
            var redisConnStr = await _app.GetConnectionStringAsync("redis")
                ?? throw new InvalidOperationException("Redis connection string not available");
            _redis = await ConnectionMultiplexer.ConnectAsync(redisConnStr);

            // Wait for BlueMap to be ready
            await WaitForBlueMapReadyAsync();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Console.WriteLine($"[FullStackFixture] Startup failed: {ex}");
            throw;
        }
        catch (OperationCanceledException)
        {
            throw new TimeoutException(
                $"FullStackFixture startup did not complete within {StartupTimeout}. " +
                "Ensure Docker is running and containers can be pulled.");
        }
    }

    /// <summary>
    /// Polls BlueMap's web server until it responds with 200 OK.
    /// </summary>
    private async Task WaitForBlueMapReadyAsync()
    {
        var retryPolicy = Policy
            .Handle<HttpRequestException>()
            .Or<TaskCanceledException>()
            .WaitAndRetryAsync(
                retryCount: 36, // 36 * 5s = 3 min
                sleepDurationProvider: _ => TimeSpan.FromSeconds(5),
                onRetry: (ex, timespan, retryCount, _) =>
                {
                    Console.WriteLine($"[BlueMap] Waiting for readiness (attempt {retryCount})...");
                });

        await retryPolicy.ExecuteAsync(async () =>
        {
            // BlueMap serves its web app at the root
            var response = await BlueMapClient.GetAsync("/");
            response.EnsureSuccessStatusCode();
            Console.WriteLine("[BlueMap] Web server is ready");
        });
    }

    /// <summary>
    /// Waits for world generation jobs to complete by polling the job queue.
    /// </summary>
    public async Task WaitForJobsToCompleteAsync(TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        var db = Redis.GetDatabase();

        while (!cts.IsCancellationRequested)
        {
            var queueLength = await db.ListLengthAsync("queue:worldgen");
            if (queueLength == 0)
            {
                // Give the processor time to finish any in-progress job
                await Task.Delay(TimeSpan.FromSeconds(2), cts.Token);

                // Double-check queue is still empty
                queueLength = await db.ListLengthAsync("queue:worldgen");
                if (queueLength == 0)
                {
                    Console.WriteLine("[WorldGen] Job queue is empty — all jobs completed");
                    return;
                }
            }

            Console.WriteLine($"[WorldGen] {queueLength} job(s) remaining in queue...");
            await Task.Delay(TimeSpan.FromSeconds(3), cts.Token);
        }

        throw new TimeoutException($"WorldGen jobs did not complete within {timeout}");
    }

    public async Task DisposeAsync()
    {
        _bridgeApiClient?.Dispose();
        _blueMapClient?.Dispose();
        _redis?.Dispose();

        if (_app != null)
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }
}
