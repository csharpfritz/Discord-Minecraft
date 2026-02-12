# Decisions

> Shared decision log. All agents read this. Scribe merges new entries from the inbox.

### 2026-02-11: System architecture for Discord-Minecraft bridge
**By:** Gordon
**What:** Established the service decomposition (3 .NET services + Paper MC container), communication patterns (Redis pub/sub + job queue), state storage (PostgreSQL), and world generation strategy (superflat, 500-block grid villages, RCON + Bridge Plugin hybrid). Full details in `docs/architecture.md`.
**Why:** Need clear service boundaries and technology choices before any implementation begins. Event-driven architecture decouples Discord's event rate from Minecraft's world generation rate. Hybrid RCON + plugin approach balances simplicity (RCON for basic commands) with capability (plugin API for complex structures). Superflat world eliminates terrain complexity and gives predictable coordinates.

### 2026-02-11: Paper MC as the Minecraft server platform
**By:** Gordon
**What:** Using Paper MC (not Vanilla, Fabric, or Forge) running in the `itzg/minecraft-server` Docker container, orchestrated by Aspire.
**Why:** Paper has the best plugin API for server-side world manipulation, excellent performance, and the Docker image is battle-tested with env-var configuration. Aspire's container support makes this a first-class citizen in our service graph.

### 2026-02-11: Sprint plan for first 3 sprints
**By:** Gordon
**What:** Defined 3 sprints (Foundation, Core Features, Integration & Navigation) with 7 work items each, assigned to team members by expertise. Full details in `docs/sprints.md`.
**Why:** Sprint 1 establishes the infrastructure so all subsequent work has a running environment. Sprint 2 delivers the core Discord→Minecraft pipeline. Sprint 3 adds player-facing features (account linking, navigation). This ordering minimizes blocked work items.

### 2026-02-11: Channel deletion archives buildings, does not destroy them
**By:** Gordon
**What:** When a Discord channel is deleted, its corresponding Minecraft building is marked archived (signs updated, entrance blocked with barriers) but NOT demolished.
**Why:** Destroying structures while players might be inside is dangerous. Archived buildings preserve world continuity and can be repurposed if channels are recreated. This is the safe default — we can add a `/demolish` admin command later if needed.

### 2026-02-11: Account linking via one-time codes, not OAuth
**By:** Gordon
**What:** Players link Discord↔Minecraft accounts by generating a 6-char code via Discord `/link`, then typing `/link <code>` in-game within 5 minutes.
**Why:** Avoids OAuth complexity. Player proves ownership of both accounts by being present in both systems simultaneously. Redis TTL handles expiry automatically. Simple for players to understand.

### 2026-02-11: Discord bot uses singleton DiscordSocketClient with BackgroundService pattern
**By:** Oracle
**What:** The Discord bot registers `DiscordSocketClient` as a singleton in DI and injects it into a `DiscordBotWorker : BackgroundService`. The client stays alive via `Task.Delay(Timeout.Infinite, stoppingToken)` and shuts down gracefully on host cancellation. Slash commands are registered globally in the `Ready` event and handled via `SlashCommandExecuted`.
**Why:** Discord.NET's `DiscordSocketClient` manages its own WebSocket connection and must be a singleton — multiple instances would create duplicate gateway connections. The `BackgroundService` pattern integrates cleanly with .NET's generic host and Aspire's lifecycle management. Global command registration (vs. guild-specific) ensures commands work across all guilds without per-guild setup, though they take up to an hour to propagate on first registration.

### 2026-02-11: Test project structure and CI pipeline conventions
**By:** Nightwing
**What:** Established test project convention: test projects live under `tests/{ProjectName}.Tests/`, added to solution under `/tests/` folder. CI workflow at `.github/workflows/ci.yml` runs restore → build → test on ubuntu-latest with .NET 10. The test project `Bridge.Api.Tests` uses xUnit and is the first test project in the solution.
**Why:** Consistent test project placement makes it easy to add more test projects later (e.g., `DiscordBot.Service.Tests`, `WorldGen.Worker.Tests`). Running CI at the solution root means new projects are automatically included in CI when added to the `.slnx`. Using xUnit aligns with Aspire ecosystem conventions.

### 2026-02-11: Snake_case PostgreSQL table names with PascalCase C# entities
**By:** Lucius
**What:** Database tables use snake_case naming (`channel_groups`, `generation_jobs`) while C# entity classes use PascalCase (`ChannelGroup`, `GenerationJob`). Column names remain PascalCase (EF Core default for Npgsql).
**Why:** Snake_case tables follow PostgreSQL conventions and make raw SQL queries natural. PascalCase C# follows .NET conventions. The Fluent API `.ToTable()` call makes the mapping explicit without requiring a global naming convention.

### 2026-02-11: RCON password as Aspire secret parameter
**By:** Lucius
**What:** RCON password for the Minecraft container is provisioned via `builder.AddParameter("rcon-password", secret: true)` rather than a hardcoded string or environment variable.
**Why:** Aspire secret parameters integrate with user secrets in development and can be mapped to proper secret stores in production. No secrets in source control, no magic strings, and the Aspire dashboard masks the value.

### 2026-02-11: EF Core enum-to-string conversion for GenerationJobStatus
**By:** Lucius
**What:** The `GenerationJobStatus` enum is stored as a string column in PostgreSQL (via `HasConversion<string>()`) rather than as an integer.
**Why:** String storage makes the database human-readable when querying directly, and it's resilient to enum reordering. The performance difference is negligible for a job audit table.

### 2026-02-11: Discord bot token as Aspire secret parameter
**By:** Lucius
**What:** Discord bot token is provisioned via `builder.AddParameter("discord-bot-token", secret: true)` in AppHost.cs and passed to the discord-bot project via `.WithEnvironment("Discord__BotToken", discordBotToken)`. The worker reads it from `configuration["Discord:BotToken"]` — .NET config maps double-underscore env vars to hierarchical keys automatically.
**Why:** Follows the same pattern established for RCON password. Aspire secret parameters integrate with user secrets in dev and proper secret stores in production. The env var naming convention (`__` → `:`) is a built-in .NET feature, so no custom mapping code is needed. Keeps secrets out of source control and masked in the Aspire dashboard.

### 2026-02-11: Sprint 2 interface contracts
**By:** Gordon
**What:**
1. **Redis Event Schema** — All Discord events on single channel `events:discord:channel`, camelCase JSON via `System.Text.Json`, shared DTOs in `Bridge.Data/Events/`. Event types: ChannelGroupCreated/Deleted, ChannelCreated/Deleted, ChannelUpdated.
2. **Job Queue** — Redis list `queue:worldgen`, LPUSH to enqueue, BRPOP to dequeue (FIFO). Job types: CreateVillage, CreateBuilding. Village coords pre-computed via grid formula. Building placement via ring formula (radius 60, 16 slots). Shared DTOs in `Bridge.Data/Jobs/`.
3. **Bridge API Endpoints** — `GET /api/villages`, `GET /api/villages/{id}/buildings`, `POST /api/mappings/sync`, `POST /api/players/link`.
4. **WorldGen Worker Interfaces** — `IVillageGenerator.GenerateAsync`, `IBuildingGenerator.GenerateAsync`. Generators throw on failure; worker retries up to 3 times.
5. **GenerationJob Lifecycle** — Pending → InProgress → Completed or Failed.
6. **Shared Constants** — `WorldConstants` in Bridge.Data (VillageSpacing=500, GridColumns=10, BuildingFootprint=21, etc.).
7. **Data Model** — UNIQUE constraint on (CenterX, CenterZ). Building coords persisted after generation.
8. **Integration Safeguards** — Upsert pattern, startup reconciliation, buildingIndex = MAX+1.
**Why:** Design Review ceremony — agents need shared contracts before parallel Sprint 2 work. Four agents modifying shared systems (Redis, PostgreSQL, Bridge.Data) require explicit interface agreements to avoid serialization mismatches, coordinate collisions, and integration failures.

### 2026-02-11: Discord event DTO design (S2-01)
**By:** Oracle
**What:** Implemented unified `DiscordChannelEvent` record in `Bridge.Data/Events/` covering all 5 event types via nullable properties. `DiscordChannelEventType` enum, `RedisChannels` constants class. Serialization: `JsonSerializerDefaults.Web` (camelCase) + `JsonStringEnumConverter`. `ChannelUpdated` filtered to name/position changes only.
**Why:** Single record with nullable fields is simpler than separate classes — one deserialization path, switch on EventType. Reduces code and maintenance. camelCase policy matches contract exactly.

### 2026-02-11: Bridge API endpoint contracts and coordinate schema (S2-02)
**By:** Lucius
**What:** Four Minimal API endpoints implemented. Added nullable `VillageX`/`VillageZ` (ChannelGroup) and `BuildingX`/`BuildingZ` (Channel) columns to distinguish planned vs generated coordinates. `VillageIndex` on ChannelGroup for ordinal tracking. UNIQUE constraint on `(CenterX, CenterZ)`.
**Why:** Nullable coordinate columns let us distinguish "scheduled" from "built" status. VillageIndex needed for grid formula. UNIQUE constraint prevents spatial collisions (Integration Risk #3).

### 2026-02-11: Event consumer architecture and job queue design (S2-03)
**By:** Lucius
**What:**
1. `DiscordEventConsumer` as BackgroundService with `IServiceScopeFactory` for per-message DbContext scopes.
2. `WorldGenJob` envelope pattern — single Redis list, typed payloads, JobId links to PostgreSQL.
3. `IsArchived` added to ChannelGroup entity. ChannelGroupDeleted archives group and all channels.
4. Auto-create group on out-of-order ChannelCreated (upsert pattern per Integration Risk #6).
5. `RedisQueues` constants in Bridge.Data for queue name sharing.
**Why:** Scoped DbContext prevents entity accumulation. Envelope pattern routes without payload inspection. Upsert handles out-of-order Discord events gracefully.

### 2026-02-11: Village generation RCON architecture (S2-04)
**By:** Batgirl
**What:** Singleton `RconService` wrapping CoreRCON with semaphore serialization, configurable rate limiting (50ms default), auto-reconnection. `VillageGenerator` registered as `IVillageGenerator` singleton. Config via `Rcon:Host/Port/Password`.
**Why:** Single RCON connection avoids pool exhaustion (Integration Risk #4). Rate limiting prevents overwhelming Paper MC. Singleton matches single-worker architecture.

### 2026-02-11: Building generation structure and layout (S2-05)
**By:** Batgirl
**What:** 21×21 footprint, 4-floor buildings (y=65-84). Stone brick walls, oak plank floors, slab roof, 3-wide south entrance, NE switchback stairs, glass pane windows, glowstone lighting, colored carpet borders, oak wall signs (not standing signs). Ring placement at radius=60 using angle formula.
**Why:** 19×19 interior holds 10+ players per floor. Ring layout with 16 slots provides even spacing. Wall signs are more visible and don't obstruct foot traffic.

### 2026-02-11: WorldGen job processor implementation (S2-06)
**By:** Lucius
**What:** `WorldGenJobProcessor` BackgroundService polls Redis `queue:worldgen` via `ListRightPopAsync` with 500ms idle delay. Retry: up to 3 attempts with exponential backoff (2, 4, 8s), then Failed. Payload mapping between Bridge.Data DTOs and WorldGen.Worker request models. `UpdateBuilding` stubbed for Sprint 3.
**Why:** StackExchange.Redis lacks BRPOP — polling with idle delay is idiomatic .NET. Fire-and-forget re-enqueue keeps main loop unblocked. Pending status enables crash recovery.

### 2026-02-11: Integration test infrastructure for Bridge.Api (S2-07)
**By:** Nightwing
**What:** WebApplicationFactory + Testcontainers Redis + SQLite in-memory. Tests swap Aspire's Npgsql/Redis registrations. Fixed `DefaultIfEmpty(-1).MaxAsync()` cross-provider incompatibility by switching to nullable `Max()` pattern.
**Why:** WebApplicationFactory provides real HTTP pipeline without Aspire overhead. SQLite avoids Docker dependency for DB layer. Testcontainers Redis needed for real pub/sub testing. Nullable Max() pattern works on all EF Core providers.

### 2026-02-12: User directive — Sprint work items as GitHub Issues with milestones and squad labels
**By:** Jeffrey T. Fritz (via Copilot)
**What:** Going forward, all work items for a Sprint must be created as GitHub Issues. Each Sprint's issues must be grouped under a properly named GitHub milestone. Create color-coordinated labels for each squad member and apply them to issues assigned to that member.
**Why:** User request — captured for team memory

### 2026-02-12: Sprint 3 work items created as GitHub Issues
**By:** Gordon
**What:** Created milestone "Sprint 3: Integration & Navigation" (milestone #1) with 7 issues (#1–#7) covering: Paper Bridge Plugin, account linking, minecart tracks, track routing, channel deletion handling, slash commands, and E2E smoke tests. Created 5 color-coded squad labels (`squad:gordon`, `squad:oracle`, `squad:lucius`, `squad:batgirl`, `squad:nightwing`) for issue assignment tracking. Each issue includes full description, size, dependencies, and acceptance criteria derived from Sprint 3 Definition of Done.
**Why:** Jeff requested sprint work items be tracked as GitHub Issues with milestones and squad-colored labels going forward. This provides visibility into sprint progress, enables per-squad filtering, and integrates with GitHub project boards.
