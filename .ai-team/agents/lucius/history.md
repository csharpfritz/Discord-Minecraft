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

### Sprint 3 — S3-05: Channel Deletion Handling

- Channel/category deletion now enqueues `ArchiveBuilding`/`ArchiveVillage` jobs to the Redis `queue:worldgen` in addition to setting `IsArchived=true` in PostgreSQL
- `BuildingArchiver` in WorldGen.Worker handles in-world archival: updates all signs (entrance + floor) with red `[Archived]` prefix, blocks 3×3 entrance with `minecraft:barrier` blocks
- `ArchiveVillage` job iterates all buildings in the group and archives each one via the same `BuildingArchiver` — no separate village-level RCON logic needed
- Building coordinate recalculation uses the same ring formula as `BuildingGenerator`: `60 * cos/sin(index * 22.5°)` from village center
- Sign text truncated to 10 chars (vs 15 in original) to leave room for `[Archived]` prefix on the first line
- Job payload DTOs: `ArchiveBuildingJobPayload` and `ArchiveVillageJobPayload` in `Bridge.Data/Jobs/`
- `IBuildingArchiver` interface + `BuildingArchiver` implementation in `WorldGen.Worker/Generators/`
- `BuildingArchiveRequest` model in `WorldGen.Worker/Models/`
- `DiscordEventConsumer.HandleChannelDeletedAsync` now includes `ChannelGroup` via `.Include()` to get village center coords for the archive job
- `DiscordEventConsumer.HandleChannelGroupDeletedAsync` now builds a list of `ArchiveBuildingJobPayload` for all channels and wraps them in an `ArchiveVillageJobPayload`

### Sprint 1 Foundation (S1-01 through S1-04)

- Aspire 13.1 `aspire-apphost` template generates `AppHost.cs` (not `Program.cs`) as the entry point
- Solution structure: all projects under `src/` — `AppHost`, `ServiceDefaults`, `DiscordBot.Service`, `Bridge.Api`, `WorldGen.Worker`, `Bridge.Data`
- `Bridge.Data` is the shared EF Core class library — contains `BridgeDbContext`, entity models in `Entities/`, and migrations in `Migrations/`
- RCON password is managed via `builder.AddParameter("rcon-password", secret: true)` — stored in AppHost user secrets, not hardcoded
- Minecraft container endpoints use `scheme: "tcp"` for non-HTTP ports (25565 MC, 25575 RCON)
- `Aspire.Npgsql.EntityFrameworkCore.PostgreSQL` NuGet depends on `Npgsql.EntityFrameworkCore.PostgreSQL` which pins `EF Core Relational` to a specific patch version — Bridge.Data explicitly references `Microsoft.EntityFrameworkCore.Relational` at 10.0.3 to keep versions aligned
- Connection resource names: `"bridgedb"` (PostgreSQL database), `"redis"` (Redis), `"postgres"` (PostgreSQL server)
- PostgreSQL schema uses snake_case table names (`channel_groups`, `channels`, `players`, `world_state`, `generation_jobs`) with PascalCase C# properties
- `GenerationJobStatus` enum is stored as a string in the database (via `HasConversion<string>()`) for readability
- All Discord IDs stored as `string` (max 64 chars) — Discord snowflakes are uint64 but string avoids overflow and works across all DB providers
- Key file paths:
  - `src/AppHost/AppHost.cs` — Aspire orchestrator entry point
  - `src/ServiceDefaults/Extensions.cs` — shared Aspire service defaults (OTel, health checks, service discovery)
  - `src/Bridge.Data/BridgeDbContext.cs` — EF Core DbContext with Fluent API configuration
  - `src/Bridge.Data/Entities/` — ChannelGroup, Channel, Player, WorldState, GenerationJob
  - `src/Bridge.Api/Program.cs` — wired with AddNpgsqlDbContext, AddRedisClient, AddServiceDefaults
  - `src/WorldGen.Worker/Program.cs` — wired with AddNpgsqlDbContext, AddRedisClient, AddServiceDefaults
  - `src/DiscordBot.Service/Program.cs` — wired with AddRedisClient, AddServiceDefaults (Discord.NET already scaffolded by Oracle)

📌 Team update (2026-02-11): Discord bot uses singleton DiscordSocketClient with BackgroundService pattern — decided by Oracle
📌 Team update (2026-02-11): Test projects under tests/{ProjectName}.Tests/, CI at .github/workflows/ci.yml with .NET 10 — decided by Nightwing

### Discord Bot Token Configuration

- Discord bot token follows the same Aspire secret parameter pattern as RCON password: `builder.AddParameter("discord-bot-token", secret: true)` in `AppHost.cs`
- Token is passed to the discord-bot project via `.WithEnvironment("Discord__BotToken", discordBotToken)` — Aspire injects it as an environment variable
- .NET configuration automatically maps `Discord__BotToken` (env var double-underscore) to `Discord:BotToken` (hierarchical config key) — no code changes needed in the consuming service
- `DiscordBotWorker.cs` reads `configuration["Discord:BotToken"]` which works with both Aspire-injected env vars and local user secrets

### Sprint 2 — S2-02: Bridge API Core Endpoints

- Implemented four Minimal API endpoints in `Bridge.Api/Program.cs`:
  - `POST /api/mappings/sync` — upserts ChannelGroups and Channels from Discord guild structure, computes VillageIndex and CenterX/CenterZ using WorldConstants grid formula
  - `GET /api/villages` — returns all ChannelGroups with building counts
  - `GET /api/villages/{id}/buildings` — returns Channels for a village
  - `POST /api/players/link` — generates 6-char alphanumeric code, stores in Redis with 5-min TTL at `link:{code}`
- Added coordinate columns to entities: ChannelGroup gets VillageX (int?), VillageZ (int?), VillageIndex (int); Channel gets BuildingX (int?), BuildingZ (int?)
- Created `WorldConstants` static class in Bridge.Data with all shared constants (VillageSpacing, GridColumns, BuildingFootprint, etc.)
- Added UNIQUE constraint on `(CenterX, CenterZ)` in channel_groups to prevent spatial collisions (per Integration Risk #3)
- Migration: `AddCoordinateColumns` adds all new columns and the unique index
- Request DTOs defined as records at bottom of Program.cs: SyncRequest, SyncChannelGroup, SyncChannel, LinkRequest
- VillageX/VillageZ and BuildingX/BuildingZ are nullable — null means "not yet generated by WorldGen Worker"
- Sync endpoint computes village coordinates at creation time: `col * 500`, `row * 500` using GridColumns=10

### Sprint 2 — S2-03: Event Consumer + Job Queue

- Created `DiscordEventConsumer : BackgroundService` in Bridge.Api — subscribes to Redis pub/sub channel `events:discord:channel` via `IConnectionMultiplexer`
- Consumer deserializes events using `DiscordChannelEvent.FromJson()` and dispatches by `EventType` enum
- Event handlers: ChannelGroupCreated (upsert group + enqueue CreateVillage), ChannelCreated (upsert channel + enqueue CreateBuilding), ChannelGroupDeleted/ChannelDeleted (mark IsArchived=true), ChannelUpdated (update name + enqueue UpdateBuilding)
- Added `IsArchived` bool property to `ChannelGroup` entity (Channel already had it)
- Created job DTOs in `Bridge.Data/Jobs/`: `WorldGenJob` (envelope with JobType + JobId + Payload + CreatedAt), `VillageJobPayload`, `BuildingJobPayload`, `WorldGenJobType` enum, `RedisQueues` constants
- Jobs are LPUSH'd to Redis list `queue:worldgen` as serialized JSON (WorldGenJob envelope wraps the payload)
- Each event that enqueues a job also creates a `GenerationJob` row in PostgreSQL with Status=Pending for tracking
- Out-of-order event handling: ChannelCreated for unknown group auto-creates the group (upsert pattern per Integration Risk #6)
- BuildingIndex computed as `MAX(BuildingIndex) + 1` for channels in the group (consistent with sync endpoint and Integration Risk #5)
- Consumer uses `IServiceScopeFactory` to create per-event scopes for DbContext (BackgroundService is singleton, DbContext is scoped)
- Registered as `builder.Services.AddHostedService<DiscordEventConsumer>()` in Program.cs
- JSON serialization uses `JsonSerializerDefaults.Web` (camelCase) consistent with Oracle's publishing format

### Sprint 2 — S2-06: WorldGen Worker Job Processor

- Replaced placeholder `Worker.cs` with `WorldGenJobProcessor.cs` — a `BackgroundService` that polls Redis list `queue:worldgen` via `ListRightPopAsync` (BRPOP not available in StackExchange.Redis, uses polling with 500ms delay as equivalent)
- Job processor lifecycle: dequeue `WorldGenJob` JSON → look up `GenerationJob` in PostgreSQL (set InProgress + StartedAt) → dispatch by `JobType` → on success set Completed + CompletedAt → on failure retry up to 3 times with exponential backoff (2^retry seconds)
- Dispatch mapping: `CreateVillage` → deserialize `VillageJobPayload` → map to `VillageGenerationRequest` → call `IVillageGenerator.GenerateAsync`; `CreateBuilding` → deserialize `BuildingJobPayload` → map to `BuildingGenerationRequest` → call `IBuildingGenerator.GenerateAsync`; `UpdateBuilding` → logged as not yet implemented (Sprint 3)
- Payload-to-request mapping: `VillageJobPayload.VillageName` → `VillageGenerationRequest.Name`; `BuildingJobPayload.ChannelName` → `BuildingGenerationRequest.Name`; `BuildingJobPayload.CenterX/CenterZ` → `BuildingGenerationRequest.VillageCenterX/VillageCenterZ`
- Uses `IServiceScopeFactory` to create a scoped `BridgeDbContext` per job (BackgroundService is singleton, DbContext is scoped)
- Re-enqueue on failure uses fire-and-forget `Task` with delay, so the main loop isn't blocked; job stays as Pending in DB if shutdown occurs during backoff
- Deleted old `Worker.cs` placeholder; updated `Program.cs` to register `WorldGenJobProcessor` instead
- Generators (`IVillageGenerator`, `IBuildingGenerator`) and `RconService` remain registered as singletons (they are stateless or manage their own connection lifecycle)

📌 Team update (2026-02-11): Sprint 2 interface contracts established — Redis event schema, job queue format, API endpoints, WorldGen interfaces, shared constants — decided by Gordon
📌 Team update (2026-02-11): Discord event DTO — unified DiscordChannelEvent record in Bridge.Data/Events/, use FromJson() to deserialize, ChannelUpdated filtered to name/position — decided by Oracle
📌 Team update (2026-02-11): Village generation uses singleton RconService with semaphore serialization and rate limiting — IVillageGenerator interface — decided by Batgirl
📌 Team update (2026-02-11): Building generation — 21×21, 4-floor, ring placement at radius=60 — IBuildingGenerator interface, wall signs not standing signs — decided by Batgirl
📌 Team update (2026-02-11): DefaultIfEmpty(-1).MaxAsync() replaced with nullable Max() pattern in prod code for cross-provider compatibility — decided by Nightwing
📌 Team update (2026-02-12): Sprint work items are now GitHub Issues with milestones and squad-colored labels — decided by Jeff and Gordon

 Team update (2026-02-12): README.md created with project overview, architecture, getting started, and squad roster with shields.io badges  decided by Gordon

📌 Team update (2026-02-12): Only publicly accessible Discord channels are mapped to Minecraft village buildings — private/restricted channels excluded — decided by Jeffrey T. Fritz

### Sprint 3 — S3-05: Channel Deletion Handling

- Channel/category deletion now enqueues `ArchiveBuilding`/`ArchiveVillage` jobs to the Redis `queue:worldgen` in addition to setting `IsArchived=true` in PostgreSQL
- `BuildingArchiver` in WorldGen.Worker handles in-world archival: updates all signs (entrance + floor) with red `[Archived]` prefix, blocks 3×3 entrance with `minecraft:barrier` blocks
- `ArchiveVillage` job iterates all buildings in the group and archives each one via the same `BuildingArchiver` — no separate village-level RCON logic needed
- Building coordinate recalculation uses the same ring formula as `BuildingGenerator`: `60 * cos/sin(index * 22.5°)` from village center
- Sign text truncated to 10 chars (vs 15 in original) to leave room for `[Archived]` prefix on the first line
- Job payload DTOs: `ArchiveBuildingJobPayload` and `ArchiveVillageJobPayload` in `Bridge.Data/Jobs/`
- `IBuildingArchiver` interface + `BuildingArchiver` implementation in `WorldGen.Worker/Generators/`
- `BuildingArchiveRequest` model in `WorldGen.Worker/Models/`
- `DiscordEventConsumer.HandleChannelDeletedAsync` now includes `ChannelGroup` via `.Include()` to get village center coords for the archive job
- `DiscordEventConsumer.HandleChannelGroupDeletedAsync` now builds a list of `ArchiveBuildingJobPayload` for all channels and wraps them in an `ArchiveVillageJobPayload`
- `ArchiveVillage` job iterates all buildings in the group and archives each one via `BuildingArchiver`
- Building coordinate recalculation uses the same ring formula as `BuildingGenerator`: `60 * cos/sin(index * 22.5°)` from village center
- Sign text truncated to 10 chars (vs 15 in original) to leave room for `[Archived]` prefix on first line
- Job payload DTOs: `ArchiveBuildingJobPayload` and `ArchiveVillageJobPayload` in `Bridge.Data/Jobs/`
- `IBuildingArchiver` interface + `BuildingArchiver` in `WorldGen.Worker/Generators/`
- `BuildingArchiveRequest` model in `WorldGen.Worker/Models/`
- `DiscordEventConsumer.HandleChannelDeletedAsync` now includes `ChannelGroup` via `.Include()` for village center coords
- `DiscordEventConsumer.HandleChannelGroupDeletedAsync` builds `ArchiveVillageJobPayload` with all building payloads
- **Completed S3-05:** Wired `ArchiveBuilding` and `ArchiveVillage` job types in `WorldGenJobProcessor.DispatchJobAsync` — injected `IBuildingArchiver` and added switch cases to deserialize payloads and invoke the archiver
- `ArchiveVillage` iterates `villageArchivePayload.Buildings` and calls `buildingArchiver.ArchiveAsync` for each building — no separate village-level RCON logic needed
- Full flow verified: Discord channel/category delete → `DiscordEventConsumer` sets `IsArchived=true` + enqueues job → `WorldGenJobProcessor` dequeues and dispatches → `BuildingArchiver` updates signs + blocks entrance via RCON
📌 Team update (2026-02-12): Minecart track layout— L-shaped paths at y=65, stations 30 blocks south of village center, angle-based platform slots — decided by Batgirl
📌 Team update (2026-02-12): BlueMap integration added as S3-08 — drop-in Paper plugin, port 8100 via Aspire, Java API markers — decided by Gordon
📌 Team update (2026-02-12): Sprint 3 test specs written — 14 channel deletion + 8 E2E smoke tests, reusing BridgeApiFactory — decided by Nightwing
📌 Team update (2026-02-12): Paper Bridge Plugin uses JDK HttpServer + Jedis + Bukkit scheduler, player events on events:minecraft:player — decided by Oracle
📌 Team update (2026-02-12): Port reassignment — decided by Lucius, requested by Jeff

### RCON Health Check for Minecraft Container

- Added `MinecraftHealthCheck : IHealthCheck` in `src/AppHost/MinecraftHealthCheck.cs` — connects to RCON at `localhost:25675` via CoreRCON, sends `seed` command, returns Healthy on success or Unhealthy on failure/timeout
- Health check uses `IConfiguration` to read the RCON password from `Parameters:rcon-password` (Aspire secret parameter)
- 5-second timeout via `CancellationTokenSource.CreateLinkedTokenSource` + `CancelAfter` — prevents hanging during MC startup
- RCON connection is `using`-disposed after each check (no persistent connection)
- Registered in `AppHost.cs` via `builder.Services.AddHealthChecks().AddCheck<MinecraftHealthCheck>("minecraft-rcon")`
- Wired to the minecraft container via `.WithHealthCheck("minecraft-rcon")` — Aspire dashboard now shows the container as unhealthy/starting until RCON responds
- Added `CoreRCON 5.4.2` NuGet to `AppHost.csproj` (same version as WorldGen.Worker)
- No separate health checks NuGet needed — `Aspire.Hosting.AppHost 13.1.0` transitively provides `Microsoft.Extensions.Diagnostics.HealthChecks 10.0.1`
- CoreRCON's `RCON` class implements `IDisposable` (not `IAsyncDisposable`) — use `using var` not `await using`

### RCON Configuration Fixes — Port Mapping and URI Parsing

- **Port mapping bug:** `AppHost.cs` had `targetPort: 25675` for the RCON endpoint, but `server.properties` defines `rcon.port=25575` inside the container. Docker's `targetPort` is the CONTAINER-side port, `port` is the HOST-side port. Fixed to `targetPort: 25575, port: 25675` so traffic from host:25675 reaches container:25575.
- **URI parsing in RconService:** Aspire's `GetEndpoint("rcon")` returns a full URI like `tcp://hostname:25675`, but `RconService` was passing it directly to `Dns.GetHostAddressesAsync()` which expects a bare hostname. Fixed by parsing the config value with `Uri.TryCreate()` — extracts hostname and port from the URI. Falls back to treating the value as a plain hostname if it's not a valid URI (backward compatible).
- **Rcon__Port env var retained as fallback:** When the URI includes a port, it takes precedence. When it doesn't (port <= 0), falls back to `Rcon:Port` config or default 25575.
- **MinecraftHealthCheck port confirmed correct:** `RconPort = 25675` in the health check is the HOST-side port, which is correct since the health check runs in the AppHost process on the host machine.
- Key lesson: In Aspire's `.WithEndpoint()`, `targetPort` = container internal port (must match what the app inside listens on), `port` = host external port (what other services on the host connect to).
📌 Team update (2026-02-12): Track routing triggered by village creation — WorldGenJobProcessor enqueues CreateTrack jobs after CreateVillage completes — decided by Batgirl
📌 Team update (2026-02-12): /status and /navigate slash commands added with Bridge API endpoints — decided by Oracle
📌 Team update (2026-02-12): Startup guild sync added to DiscordBotWorker — populates DB on bot ready — decided by Oracle
📌 Team update (2026-02-12): Sync endpoint now creates GenerationJob records and pushes to Redis queue — decided by Oracle

### RCON Host Resolution Fix — Docker Hostname vs Localhost

- **Root cause:** `minecraft.GetEndpoint("rcon")` resolved to `tcp://minecraft:25575` — the Docker container hostname. The WorldGen worker runs as a .NET project on the host machine, not inside Docker, so `Dns.GetHostAddressesAsync("minecraft")` failed because the Docker container name is only resolvable within the Docker network.
- **Fix:** Changed `AppHost.cs` line 49 from `.WithEnvironment("Rcon__Host", minecraft.GetEndpoint("rcon"))` to `.WithEnvironment("Rcon__Host", "localhost")`. The worker now connects to `localhost:25675` (the host-mapped port), which Docker forwards to container port 25575.
- **RconService.cs unchanged:** With `"localhost"` as the config value, `Uri.TryCreate("localhost", UriKind.Absolute, ...)` returns false, so the else branch sets `_host = "localhost"` and `_port = 25675` from `Rcon:Port` config. No URI parsing issues.
- Key lesson: Aspire's `GetEndpoint()` returns Docker-network-internal URIs. For .NET projects running on the host (not as containers), pass explicit `localhost` + host-mapped port instead of using endpoint references.

📌 Team update (2026-02-13): Village amenities — walkways, scalable fountains, interior sign fix — decided by Batgirl
📌 Team update (2026-02-13): Crossroads hub + spawn + teleport consolidated — central hub at origin (0,0), hub-and-spoke track topology, /goto command, CrossroadsGenerator + CrossroadsInitializationService needed — decided by Jeff, Gordon
📌 Team update (2026-02-13): Sprint 4 plan — 8 work items: Crossroads hub, hub-and-spoke tracks, player teleport, building variety, station relocation, BlueMap markers, E2E tests, Crossroads integration. Account linking deferred again — decided by Gordon
📌 Team update (2026-02-13): Plugin HTTP port 8180 exposed via Aspire for marker wiring — if you add new generation job types, add marker calls in SetMarkersForJobAsync — decided by Oracle
📌 Team update (2026-02-13): /goto command uses Bridge API (/api/buildings/search + /api/buildings/{id}/spawn) for building lookup and teleport — decided by Oracle
📌 Team update (2026-02-13): Hub-and-Spoke track topology — each village gets one track to Crossroads, O(n) instead of O(n²), radial slot positioning at Crossroads — decided by Batgirl
📌 Team update (2026-02-13): Village station relocation to plaza edge — VillageStationOffset=17, shared constant in WorldConstants — decided by Batgirl
📌 Team update (2026-02-13): Crossroads API and BlueMap URL configuration — Bridge.Api has BlueMap:WebUrl config key, /api/crossroads endpoint, /crossroads slash command — decided by Oracle

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
