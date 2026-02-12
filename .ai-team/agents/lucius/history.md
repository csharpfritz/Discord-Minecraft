# Project Context

- **Owner:** Jeffrey T. Fritz (csharpfritz@users.noreply.github.com)
- **Project:** Discord-to-Minecraft bridge — maps Discord channels to Minecraft villages/buildings with minecart navigation between channel groups. Creative/peaceful mode, .NET 10/Aspire 13.1/C#.
- **Stack:** .NET 10, Aspire 13.1, C#, Discord.NET, Minecraft protocol
- **Created:** 2026-02-11

## Learnings

<!-- Append new learnings below. Each entry is something lasting about the project. -->

📌 Team update (2026-02-11): System architecture established — 3 .NET services (Discord Bot, Bridge API, WorldGen Worker) + Paper MC + PostgreSQL + Redis, orchestrated by Aspire 13.1 — decided by Gordon
📌 Team update (2026-02-11): Paper MC chosen as Minecraft server platform (itzg/minecraft-server Docker container, orchestrated by Aspire) — decided by Gordon
📌 Team update (2026-02-11): Sprint plan defined — 3 sprints: Foundation, Core Features, Integration & Navigation — decided by Gordon
📌 Team update (2026-02-11): Channel deletion archives buildings (does not destroy them) — decided by Gordon
📌 Team update (2026-02-11): Account linking via one-time 6-char codes with 5-min Redis TTL (no OAuth) — decided by Gordon

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
