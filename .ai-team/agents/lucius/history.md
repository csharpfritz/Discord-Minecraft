# Project Context

- **Owner:** Jeffrey T. Fritz (csharpfritz@users.noreply.github.com)
- **Project:** Discord-to-Minecraft bridge — maps Discord channels to Minecraft villages/buildings with minecart navigation between channel groups. Creative/peaceful mode, .NET 10/Aspire 13.1/C#.
- **Stack:** .NET 10, Aspire 13.1, C#, Discord.NET, Minecraft protocol
- **Created:** 2026-02-11

## Core Context (summarized from Sprint 1–2 sessions)

**Sprint 1 Foundation (S1-01–S1-04):** Scaffolded Aspire 13.1 AppHost with 6 projects under `src/`. Bridge.Data holds EF Core DbContext, entities (`Entities/`), and migrations. PostgreSQL uses snake_case tables, PascalCase C# properties, string-stored enums. Secrets via `builder.AddParameter(..., secret: true)` for RCON password and Discord bot token. Connection resources: `"bridgedb"`, `"redis"`, `"postgres"`. MC container uses `scheme: "tcp"` for non-HTTP ports. Discord IDs stored as strings. EF Core Relational pinned to 10.0.3.

**Sprint 2 — S2-02 (Bridge API):** Four Minimal API endpoints (`/api/mappings/sync`, `/api/villages`, `/api/villages/{id}/buildings`, `/api/players/link`). Nullable coordinate columns (VillageX/Z on ChannelGroup, BuildingX/Z on Channel) distinguish planned vs generated. VillageIndex for ordinal grid placement. UNIQUE constraint on (CenterX, CenterZ). WorldConstants in Bridge.Data.

**Sprint 2 — S2-03 (Event Consumer):** `DiscordEventConsumer` BackgroundService subscribes to `events:discord:channel`, dispatches by EventType. `WorldGenJob` envelope pattern on `queue:worldgen` Redis list. IsArchived on ChannelGroup. Upsert for out-of-order events. `IServiceScopeFactory` for per-event DbContext. BuildingIndex = MAX+1.

**Sprint 2 — S2-06 (Job Processor):** `WorldGenJobProcessor` BackgroundService polls Redis via `ListRightPopAsync` (500ms idle). 3 retries with exponential backoff. Payload mapping between Bridge.Data DTOs and WorldGen.Worker models. Generators/RconService are singletons.

**Discord Bot Token:** Aspire secret parameter → env var `Discord__BotToken` → config key `Discord:BotToken`.

## Learnings

### Sprint 2–3 Summary (2026-02-11 → 2026-02-12)

**S3-05 Channel Deletion:** ArchiveBuilding/ArchiveVillage jobs enqueued to Redis worldgen queue (not just DB flag). BuildingArchiver updates signs with `[Archived]` prefix, blocks entrance with barriers. ArchiveVillage iterates all buildings via same archiver. Coordinate recalculation uses ring formula. DTOs: ArchiveBuildingJobPayload, ArchiveVillageJobPayload in Bridge.Data/Jobs/.

**S1 Foundation:** Aspire 13.1 AppHost.cs entry point, 6 projects under src/. Bridge.Data: EF Core DbContext, snake_case tables, PascalCase C#, string-stored enums. Secrets via AddParameter(secret:true). Connection resources: "bridgedb", "redis", "postgres". Discord IDs as strings.

**S2-02 Bridge API:** 4 Minimal API endpoints (sync, villages, villages/{id}/buildings, players/link). Nullable VillageX/Z, BuildingX/Z columns. VillageIndex for grid. UNIQUE on (CenterX, CenterZ). WorldConstants in Bridge.Data.

**S2-03 Event Consumer:** DiscordEventConsumer BackgroundService subscribes to events:discord:channel. WorldGenJob envelope on queue:worldgen. Upsert for out-of-order events. IServiceScopeFactory for per-event DbContext. BuildingIndex = MAX+1.

**S2-06 Job Processor:** WorldGenJobProcessor BackgroundService polls Redis via ListRightPopAsync (500ms idle). 3 retries with exponential backoff. Generators/RconService are singletons.

**Discord Bot Token:** Aspire secret → env var Discord__BotToken → config key Discord:BotToken.

**Key File Paths:** AppHost.cs, ServiceDefaults/Extensions.cs, Bridge.Data/BridgeDbContext.cs, Bridge.Data/Entities/, Bridge.Api/Program.cs, WorldGen.Worker/Program.cs, DiscordBot.Service/Program.cs.

### RCON Configuration Fixes (2026-02-12)

- Port mapping: targetPort=25575 (container), port=25675 (host). MinecraftHealthCheck at localhost:25675.
- URI parsing: Aspire GetEndpoint returns tcp://hostname:port; RconService parses with Uri.TryCreate, falls back to bare hostname.
- Docker hostname fix: Changed Rcon__Host from minecraft.GetEndpoint("rcon") to "localhost" — worker runs on host, not inside Docker.
- Key lesson: Aspire's GetEndpoint() returns Docker-network URIs; use localhost + host-mapped port for host-side processes.

### MinecraftHealthCheck (2026-02-12)

- MinecraftHealthCheck : IHealthCheck — CoreRCON connect to localhost:25675, send `seed`, 5s timeout. Registered via AddHealthChecks + WithHealthCheck("minecraft-rcon").

📌 Team update (2026-02-12): Track routing triggered by village creation — decided by Batgirl
📌 Team update (2026-02-12): /status, /navigate slash commands — decided by Oracle
📌 Team update (2026-02-12): Startup guild sync + sync endpoint job creation — decided by Oracle

### Sprint 4 — Phase 1: RconService Batch Infrastructure

- Added `SendBatchAsync(IReadOnlyList<string>, CancellationToken)` — acquires semaphore once for the entire batch, sends all commands sequentially via CoreRCON, applies ONE delay at the end instead of per-command. Returns `string[]` of responses.
- Added `SendFillBatchAsync` and `SendSetBlockBatchAsync` — typed batch helpers that format fill/setblock commands and delegate to `SendBatchAsync`
- Existing `SendCommandAsync`, `SendFillAsync`, `SendSetBlockAsync` remain unchanged — generators opt into batching incrementally
- Default `Rcon:CommandDelayMs` reduced from 50ms to 10ms — with ~7,100 commands per world, this alone saves significant time
- Adaptive delay: `_currentDelayMs` starts at configured delay, decreases by 1ms on success (min 5ms), doubles on failure (max 100ms). Applied to both single commands and batches.
- Semaphore stays at (1,1) — batch optimization removes inter-command delays within a single semaphore hold, not parallelism
- Batch error handling: on failure, logs batch size, doubles adaptive delay, resets RCON connection (same pattern as single-command error handling)
- Performance projection: a 100-command batch now takes ~10ms total delay instead of 100×50ms = 5000ms

 Team update (2026-02-13): Oracle replaced ListRightPopAsync with PopClosestJobAsync in WorldGenJobProcessor for spawn-proximity priority + added tellraw broadcasts  decided by Oracle
 Team update (2026-02-13): Batgirl adopted SendBatchAsync/SendFillBatchAsync/SendSetBlockBatchAsync across all 4 generators, reducing commands from ~7,100 to ~2,600  decided by Batgirl

### Sprint 5 — S5-07: World Activity Feed in Discord

- **WorldActivityEvent** record added to Bridge.Data/Events/ — Type, Name, X, Z, Timestamp. JSON serialization via `JsonSerializerDefaults.Web`.
- **RedisChannels.WorldActivity** = `"events:world:activity"` added alongside existing channel constants.
- **WorldGenJobProcessor** publishes `WorldActivityEvent` to Redis pub/sub after successful job completion (best-effort, never fails the job). Event types: `village_built`, `building_built`, `track_built`, `building_archived`.
- **WorldActivityFeedService** BackgroundService in DiscordBot.Service subscribes to the Redis channel, posts Discord embeds to a configurable channel (`Discord:ActivityChannelId` config key / `Discord__ActivityChannelId` env var).
- Embed format: 🟢 green for built events, 🔴 red for archived. Title = formatted type + name. Description = coordinates + timestamp. Footer = "Discord-Minecraft World".
- Rate limiting: ConcurrentQueue + 5-second delay between posts to avoid Discord API spam.
- AppHost passes `discord-activity-channel-id` Aspire parameter as env var to the discord-bot project.
- Key design choice: Activity feed lives in DiscordBot.Service (not Bridge.Api) because that's where the DiscordSocketClient singleton exists. Bridge.Api has no Discord.NET dependency.
