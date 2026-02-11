---
name: "discord-net-worker-service"
description: "Pattern for hosting Discord.NET bot inside a .NET generic host BackgroundService"
domain: "integration"
confidence: "low"
source: "earned"
---

## Context
When building a Discord bot as part of a larger .NET service architecture (Aspire, microservices), the bot needs to integrate with the generic host lifecycle rather than running standalone.

## Patterns
- Register `DiscordSocketConfig` and `DiscordSocketClient` as singletons in DI
- Create a `BackgroundService` that receives the client via constructor injection
- In `ExecuteAsync`: wire events → `LoginAsync` → `StartAsync` → `Task.Delay(Infinite, token)` → `StopAsync`
- Bridge Discord.NET's `LogSeverity` to `Microsoft.Extensions.Logging.LogLevel` via the `client.Log` event
- Register slash commands in the `client.Ready` event (gateway is fully connected at that point)
- Handle slash commands via `client.SlashCommandExecuted` event
- Bot token from `IConfiguration["Discord:BotToken"]` — works with user secrets and env vars
- Wire channel events via `client.ChannelCreated`, `client.ChannelDestroyed`, `client.ChannelUpdated` — detect category vs text channel with `is ICategoryChannel`
- Inject `IConnectionMultiplexer` (from Aspire's `AddRedisClient`) to get `ISubscriber` for Redis pub/sub
- Use `RedisChannel.Literal(channelName)` for publish — avoid pattern-matching overhead
- Filter `ChannelUpdated` events to only publish when meaningful properties change (name, position) — Discord fires this event for many property changes

## Examples
```csharp
// Program.cs
builder.Services.AddSingleton(new DiscordSocketConfig
{
    GatewayIntents = GatewayIntents.Guilds,
    LogLevel = LogSeverity.Info
});
builder.Services.AddSingleton<DiscordSocketClient>();
builder.Services.AddHostedService<DiscordBotWorker>();
```

```csharp
// Graceful shutdown in ExecuteAsync
try { await Task.Delay(Timeout.Infinite, stoppingToken); }
catch (OperationCanceledException) { }
await client.StopAsync();
```

## Anti-Patterns
- Do NOT create `DiscordSocketClient` per-request or as transient — it manages a persistent WebSocket
- Do NOT register slash commands outside the `Ready` event — the client isn't connected yet
- Do NOT use `while (!token.IsCancellationRequested) { await Task.Delay(...) }` — `Task.Delay(Infinite)` is cleaner and avoids unnecessary wake-ups
- Do NOT use guild-specific commands for dev convenience without documenting the tradeoff (instant registration vs. global's 1-hour propagation)
