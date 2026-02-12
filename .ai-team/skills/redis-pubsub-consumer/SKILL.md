# SKILL: Redis Pub/Sub Consumer as BackgroundService

## When to Use
When implementing a .NET BackgroundService that subscribes to Redis pub/sub channels and processes messages that require scoped services (e.g., EF Core DbContext).

## Pattern

```csharp
public sealed class MyConsumer(
    IConnectionMultiplexer redis,
    IServiceScopeFactory scopeFactory,
    ILogger<MyConsumer> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var subscriber = redis.GetSubscriber();
        var channel = await subscriber.SubscribeAsync(
            RedisChannel.Literal("my:channel"));

        channel.OnMessage(async msg =>
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<MyDbContext>();
                // Process message using db...
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error processing message");
            }
        });

        try { await Task.Delay(Timeout.Infinite, stoppingToken); }
        catch (OperationCanceledException) { }

        await subscriber.UnsubscribeAsync(RedisChannel.Literal("my:channel"));
    }
}
```

## Key Details
- **IServiceScopeFactory** creates per-message scopes — required because BackgroundService is singleton but DbContext is scoped.
- **RedisChannel.Literal()** for exact channel name matching (not pattern).
- **Task.Delay(Timeout.Infinite)** keeps the service alive; cancellation token triggers graceful shutdown.
- **Unsubscribe** in the finally path for clean disconnection.
- Register with `builder.Services.AddHostedService<MyConsumer>()`.

## Gotchas
- Do NOT inject scoped services (DbContext) directly into the constructor — use IServiceScopeFactory.
- The `OnMessage` callback runs on a ThreadPool thread; wrap in try/catch to prevent unobserved exceptions.
- Redis pub/sub is fire-and-forget — if the consumer is down, messages are lost. For durability, combine with a persistent queue (Redis list LPUSH/BRPOP).
