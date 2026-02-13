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

### 2026-02-13: Channel deletion archives buildings via WorldGen job queue (consolidated)
**By:** Gordon, Lucius, Nightwing
**What:** When a Discord channel is deleted, its corresponding Minecraft building is marked archived — NOT demolished. Policy (Gordon): signs updated, entrance blocked with barriers, structures preserved for world continuity. Implementation (Lucius): channel/category deletion events enqueue `ArchiveBuilding`/`ArchiveVillage` jobs to the Redis worldgen queue (not just `IsArchived=true` in PostgreSQL). WorldGen Worker processes via `BuildingArchiver` — updates signs with red `[Archived]` prefix, blocks entrances with barrier blocks. Gap identified by Nightwing: prior implementation only set the DB flag without RCON commands. Test specs D-07 and D-08 document expected behavior.
**Why:** Destroying structures while players are inside is dangerous. Archived buildings preserve continuity and can be repurposed. The job queue pattern keeps archival async and retryable, consistent with creation jobs. Without RCON commands, players would see no visual change in-game.

### 2026-02-11 → 2026-02-12: Account linking — designed then deferred
**By:** Gordon (design), Jeffrey T. Fritz (deferral)
**What:** Original design: players link Discord↔Minecraft accounts via 6-char one-time code (Discord `/link` → in-game `/link <code>`, 5-min Redis TTL). **Deferred (2026-02-12):** Account linking removed from Sprint 3 scope. S3-02 closed, `/link` removed from Paper Bridge Plugin (S3-01), `/unlink` removed from Discord slash commands (S3-06). Feature will be revisited in a future sprint.
**Why:** Design avoided OAuth complexity. Deferral per user request — not ready to facilitate account linking yet.

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

### 2026-02-12: README.md with project overview and squad roster
**By:** Gordon
**What:** Created `README.md` at repository root covering: project description (Discord→Minecraft bridge concept), architecture table (3 services + infra), getting started (prerequisites, user-secrets setup, `dotnet run`), squad roster with shields.io color-coded badges matching GitHub label colors, and project status linking to Sprint 3 milestone.
**Why:** Jeff requested a README with squad roster. The badge colors match the `squad:{name}` GitHub label colors for visual consistency. Shields.io badges render well on GitHub and provide instant visual identification of team members.

### 2026-02-12: Only map publicly accessible Discord channels to villages
**By:** Jeffrey T. Fritz (via Copilot)
**What:** Only create village buildings for publicly accessible channels in the Discord server. Private channels, restricted channels, and channels not visible to @everyone should be excluded from the channel-to-village mapping.
**Why:** User request — ensures the Minecraft world only reflects the public structure of the Discord server.

### 2026-02-12: User directive — Add BlueMap integration for interactive web map
**By:** Jeffrey T. Fritz (via Copilot)
**What:** Add BlueMap to the Minecraft server so Discord users can browse an interactive web map showing their channels and channel groups allocated in the Minecraft world. The map should be accessible from Discord.
**Why:** User request — provides visual discovery of the Discord-to-Minecraft world mapping without requiring players to join the server.

### 2026-02-12: BlueMap integration architecture for interactive web map
**By:** Gordon (architecture), Oracle (implementation)
**What:** BlueMap (Paper plugin) added to Sprint 3 as S3-08 (Issue #10). Architecture and implementation decisions:
1. **Deployment** — BlueMap JAR installed in the same Aspire-mounted `plugins/` directory as the Bridge Plugin. No separate container; it runs inside the Paper MC server process. `softdepend` in `plugin.yml` ensures graceful degradation if BlueMap JAR is absent.
2. **Port exposure** — BlueMap's built-in web server (port 8100) exposed through Aspire container port mapping on the Paper MC container, same pattern as RCON (25575) and MC (25565). Web server binds `0.0.0.0:8100` for Docker accessibility.
3. **Marker integration** — `BlueMapIntegration` class manages two marker sets (`discord-villages`, `discord-buildings`) via BlueMapAPI 2.7.2 (`compileOnly` dependency from `https://repo.bluecolored.de/releases`). Uses `BlueMapAPI.onEnable`/`onDisable` lifecycle hooks. `ConcurrentHashMap` cache for API reload resilience. Only overworld maps receive markers.
4. **Marker HTTP endpoints** — .NET services call Bridge Plugin HTTP API: `POST /api/markers/village`, `POST /api/markers/building`, `POST /api/markers/building/archive`, `POST /api/markers/village/archive`. Schema: `{ id, label, x, z }` for create; `{ id }` for archive.
5. **Marker lifecycle** — Markers created on village/building generation, updated on archive (visual change or removal). Bridge Plugin handles these events via its HTTP API from .NET services.
6. **Discord `/map` command** — Returns a deterministic URL (Aspire host + BlueMap port). Optional channel argument deep-links to a building marker via `#discord-buildings:{channelId}` hash fragment. `BlueMap__BaseUrl` env var passed to Discord bot, resolving to `configuration["BlueMap:BaseUrl"]` with fallback `http://localhost:8100`.
7. **BlueMap config** — version-controlled at `src/AppHost/minecraft-data/plugins/BlueMap/{core,webserver}.conf`. `accept-download: true` enables auto-download of BlueMap web assets.
8. **Ownership** — Oracle owns this item (squad:oracle label) since it spans Paper plugin integration and Discord bot commands.
**Why:** BlueMap is the lightest integration path — it's a drop-in Paper plugin that auto-renders the world and exposes a marker API. No separate rendering service, no additional database, no custom web frontend. The superflat world renders cleanly. The HTTP endpoints exist because .NET services (which trigger village/building creation) need to notify the plugin to update markers. ConcurrentHashMap + restore-on-reload handles BlueMap's API lifecycle. Port mapping through Aspire keeps the web server discoverable without manual Docker config.

### 2026-02-12: Sprint 3 test specs and channel deletion test architecture
**By:** Nightwing
**What:** Created comprehensive test specifications for all Sprint 3 features (Plugin, Tracks, Track Routing, Channel Deletion, Slash Commands, E2E) plus 14 concrete xUnit integration tests for channel deletion (S3-05) and 8 E2E smoke tests (S3-07). Tests reuse existing `BridgeApiFactory` infrastructure. Two tests are `Skip`-ped pending `/api/status` and `/api/navigate` endpoint implementation. Channel deletion tests validate archival idempotency, API response accuracy, building index continuity, null-coordinate handling, and rapid-fire event processing.
**Why:** Sprint 3 implementation is in parallel. Getting test specs and concrete test cases written now means: (1) implementers know exactly what behavior is expected, (2) edge cases are documented before code is written (shift-left), (3) 14 green tests on the channel deletion path provide immediate regression coverage.

### 2026-02-12: Paper Bridge Plugin architecture — JDK HttpServer + Jedis + Bukkit scheduler
**By:** Oracle
**What:** The Paper Bridge Plugin uses JDK's built-in `com.sun.net.httpserver.HttpServer` for the HTTP API (no external web framework), Jedis 5.2.0 for Redis pub/sub, and Gson for JSON serialization. All Bukkit API calls from HTTP handlers are dispatched to the main server thread via `Bukkit.getScheduler().runTask()`. Redis publishes are dispatched asynchronously via `runTaskAsynchronously()`. The HTTP port is configurable via `config.yml`. Dependencies are shaded into the plugin JAR via Gradle's `from(configurations.runtimeClasspath)` pattern.
**Why:** JDK HttpServer has zero dependencies and is already available in the runtime — keeps the plugin lightweight without pulling in Netty, Spark, or Javalin. Jedis is the simplest Redis client for Java with connection pooling. Main-thread dispatch is mandatory because Bukkit's API is not thread-safe. Async Redis publish prevents blocking the server tick loop. Shading dependencies avoids classpath conflicts with other plugins.

### 2026-02-12: Player event Redis channel — events:minecraft:player
**By:** Oracle
**What:** Player join/leave events are published to Redis channel `events:minecraft:player` with schema `{ eventType: "PlayerJoined"|"PlayerLeft", playerUuid, playerName, timestamp }`. The channel name is registered in both Java (`RedisPublisher.CHANNEL_PLAYER_EVENT`) and .NET (`RedisChannels.MinecraftPlayer`). A corresponding `MinecraftPlayerEvent` DTO was added to `Bridge.Data/Events/` for .NET-side deserialization.
**Why:** Follows the established pattern from Sprint 2 — channel name constants shared between producers and consumers, matching DTO records on both sides. The `events:minecraft:*` namespace was already reserved in the architecture doc. camelCase JSON matches the existing `DiscordChannelEvent` serialization convention.

### 2026-02-12: Port reassignment for multi-project coexistence
**By:** Jeffrey T. Fritz (directive), Lucius (implementation)
**What:** Jeff requested distinct ports so this project can run alongside other Minecraft projects on the same machine. Lucius reassigned all service ports using a +100 offset convention:
- Minecraft game: 25565 → 25665
- Minecraft RCON: 25575 → 25675
- BlueMap web server: 8100 → 8200
- Plugin HTTP API: 8080 → 8180

Changes applied across branches: `main`, `squad/10-bluemap`, `squad/1-paper-bridge-plugin`, `squad/6-discord-slash-commands`. Files updated: `AppHost.cs`, `server.properties`, `webserver.conf`, `config.yml`, `DiscordBotWorker.cs`.
**Why:** Jeff runs multiple Minecraft integration projects simultaneously. Default ports would conflict. The +100 offset keeps ports recognizable while avoiding collisions.

### 2026-02-12: Track routing triggered by village creation completion
**By:** Batgirl
**What:** WorldGenJobProcessor now automatically enqueues CreateTrack jobs after each CreateVillage job completes successfully. For each new village, one CreateTrack job is enqueued per existing non-archived village, connecting the new village to the entire network. The first village in the world gets no track jobs (handled gracefully with an informational log). Track jobs are enqueued AFTER village generation is marked Completed, ensuring the village structure is fully built before any track generation begins. Station signs at existing villages are updated naturally — each CreateTrack job invokes TrackGenerator which creates new station platforms with destination signs at both the source and destination village.
**Why:** Track routing must be a post-completion concern of the job processor, not the event consumer, because the village must be fully built before tracks can connect to it. Using the existing WorldGenJob queue pattern keeps track generation async, retryable, and consistent with all other generation work. The TrackGenerator already builds platforms with signs at both ends of each track, so no separate "update signs" mechanism is needed — connecting a new village to an existing one naturally creates a new platform with signs at the existing village.

### 2026-02-12: /status and /navigate slash commands with Bridge API endpoints
**By:** Oracle
**What:**
1. **Two new slash commands** — `/status` (village count, building count) and `/navigate <channel>` (village name, building index, XYZ coordinates). Registered globally in the `Ready` event alongside existing `/ping` and `/map` commands.
2. **Two new Bridge API endpoints** — `GET /api/status` returns `{ villageCount, buildingCount }` (non-archived only). `GET /api/navigate/{discordChannelId}` returns full mapping including coordinates, archived status, and village center. Returns 404 for unmapped channels.
3. **HttpClient via Aspire service discovery** — Named `HttpClient("BridgeApi")` with `https+http://bridge-api` base address registered in `DiscordBot.Service/Program.cs`. Leverages Aspire's existing `WithReference(bridgeApi)` and `AddServiceDefaults()` service discovery.
4. **Defer/Followup pattern** — Both commands use `DeferAsync()` + `FollowupAsync()` since they make HTTP calls to Bridge API. This avoids Discord's 3-second interaction timeout.
5. **Edge cases handled** — Unmapped channel shows clear message mentioning public-text-channel-in-category requirement. API unavailability shows warning. Invalid channel option handled gracefully.
**Why:** Slash commands are the primary Discord user interface for world discovery. Using Aspire service discovery avoids hardcoded URLs and works across development/production environments. The Defer pattern is mandatory for any command making external HTTP calls due to Discord's strict response deadline. The `/api/navigate` endpoint was designed to return enough data for a rich embed without requiring multiple API calls.

### 2026-02-12: Startup guild sync in DiscordBotWorker
**By:** Oracle
**What:** Added `SyncGuildsAsync()` to the Discord bot's `Ready` event handler. On startup, the bot iterates all guilds, collects publicly accessible categories and their text channels (filtering out channels where @everyone has ViewChannel explicitly denied), and POSTs a `SyncRequest` to `/api/mappings/sync` for each guild. This populates the Bridge API database with the current Discord channel structure so villages/buildings appear immediately. Key details:
1. Called AFTER `RegisterSlashCommandsAsync` in the Ready handler
2. Uses `GetPermissionOverwrite(guild.EveryoneRole)` to check `ViewChannel` — only explicit `Deny` is filtered; inherit/allow pass through
3. Only `SocketTextChannel` types within categories are included (no voice, forum, or uncategorized channels)
4. Wrapped in try/catch — sync failure logs an error but does NOT prevent the bot from running
5. Sync DTOs (`SyncRequest`, `SyncChannelGroup`, `SyncChannel`) are private records in `DiscordBotWorker`
**Why:** The `/api/mappings/sync` endpoint existed but was never called. Without an initial sync, zero villages/buildings would appear until individual channel events fired. This startup sync ensures the Minecraft world reflects the current Discord server structure immediately on bot ready.

### 2026-02-12: Sync endpoint now creates GenerationJob records and pushes to Redis queue
**By:** Oracle
**What:** Fixed `/api/mappings/sync` to create `GenerationJob` records and push `WorldGenJob` envelopes to Redis `queue:worldgen` for NEW records (not updates). Previously, synced channels appeared in `/status` counts but nothing was built in Minecraft because the sync endpoint only created DB records without job enqueueing. Now matches the exact pattern from `DiscordEventConsumer.cs`. Idempotent — re-running sync won't duplicate jobs for existing records.
**Why:** Without job enqueueing, the startup sync would populate the database but never trigger actual village/building generation in the Minecraft world. The sync endpoint must mirror the event consumer's behavior for new records.

### 2026-02-12: RCON configuration fixes — port mapping and URI parsing
**By:** Lucius
**What:** Fixed two critical RCON issues preventing WorldGen from connecting to Minecraft:
1. **Port mapping bug** — `AppHost.cs` had `targetPort: 25675` for RCON, but `server.properties` defines `rcon.port=25575` inside the container. Docker's `targetPort` is the container-side port. Fixed to `targetPort: 25575, port: 25675`.
2. **URI parsing in RconService** — Aspire's `GetEndpoint("rcon")` returns `tcp://hostname:port`, but `RconService` was passing the full URI to `Dns.GetHostAddressesAsync()`. Fixed by parsing with `Uri.TryCreate()` to extract hostname and port. Falls back to plain hostname if not a URI.
**Why:** Both issues prevented RCON connectivity. The port mismatch meant traffic was routed to the wrong internal port. The URI parsing issue meant DNS resolution failed on a URI string instead of a hostname.

### 2026-02-12: MinecraftHealthCheck for Aspire dashboard
**By:** Lucius
**What:** Added `MinecraftHealthCheck : IHealthCheck` in `src/AppHost/MinecraftHealthCheck.cs`. Connects to RCON at `localhost:25675`, sends `seed` command, returns Healthy on success or Unhealthy on failure/timeout (5s). Registered via `builder.Services.AddHealthChecks().AddCheck<MinecraftHealthCheck>("minecraft-rcon")` and wired to the minecraft container via `.WithHealthCheck("minecraft-rcon")`.
**Why:** Without a health check, Aspire dashboard showed the Minecraft container as healthy immediately on container start, before the server was actually ready to accept RCON commands. Dependent services would fail connecting during startup.

### 2026-02-12: Village perimeter fence with gates
**By:** Batgirl
**What:** Added oak fence around the entire village at **radius 150 blocks**. Encompasses all buildings (max building edge at ~142 blocks from center). 3-wide oak fence gates at the 4 cardinal entrances. Corner fence posts with lanterns for nighttime visibility.
**Why:** Village needed a defined perimeter and entry points for navigation.

### 2026-02-12: WorldConstants.cs corrections
**By:** Batgirl
**What:** Fixed constants that drifted from generator implementations: `BaseY`: 64 → -60 (superflat surface level), `BuildingFloors`: 4 → 2 (castle redesign), `FloorHeight`: 4 → 5 (actual spacing).
**Why:** Constants were out of sync with actual generator code.

### 2026-02-12: Acceptance test harness architecture
**By:** Nightwing
**What:** Created `tests/Acceptance.Tests/` project using `Aspire.Hosting.Testing` to launch the full stack. The harness uses: `FullStackFixture` (IAsyncLifetime + IClassFixture pattern) to manage Aspire app lifecycle, `BlueMapClient` to query BlueMap's static JSON marker files (no REST API exists), `DiscordEventPublisher` to simulate Discord events via Redis pub/sub, job completion detection via polling `queue:worldgen` Redis list length, and serial test execution (single Minecraft container) via xUnit collection + runsettings.
**Why:** Need end-to-end verification that Discord events → WorldGen jobs → Minecraft structures → BlueMap markers. Unit and integration tests can't catch issues at the Minecraft/BlueMap boundary.

### 2026-02-12: Acceptance test expansion — 54 tests across 7 categories
**By:** Nightwing
**What:** Organized acceptance tests into 6 distinct test classes: SmokeTests (6), VillageCreationTests (6), TrackRoutingTests (5), ArchivalTests (9), EdgeCaseTests (8), NegativeTests (13), ConcurrencyTests (7). Total: 54 acceptance tests covering track routing, edge cases (out-of-order events, duplicates, unicode), negative tests (malformed JSON, missing fields, unknown event types), and concurrency (parallel villages, high-volume bursts).
**Why:** Sprint 3 implementation is in parallel — comprehensive test specs document expected behavior before code is written.

### 2026-02-12: /unlink command stubbed for deferred account linking
**By:** Oracle
**What:** The `/unlink` Discord slash command is implemented as a stub that responds with "Account linking is not yet available" (ephemeral message). Command registered and handled, but simply informs the user the feature is coming.
**Why:** Per the Sprint 3 decision to defer account linking. Provides better UX than an unrecognized command error, and the handler is ready to be filled in when account linking is implemented.

### 2026-02-12: Village amenities and walkway system
**By:** Batgirl
**What:** Interior entrance signs removed (were floating in doorways). Cobblestone walkway system added — BuildingGenerator creates L-shaped 3-wide paths from village center to each building entrance; VillageGenerator creates perimeter walkway at FenceRadius-5. Scalable fountain: simple 3×3 basin (default) or 7×7 decorative fountain with center pillar + glowstone cap for 4+ buildings. Generation order: walkways BEFORE building foundation so foundation overwrites overlap cleanly.
**Why:** Villages needed better infrastructure — floating signs, no walkways between buildings, fountain didn't scale, buildings felt disconnected from center.

### 2026-02-13: Crossroads hub, spawn, and player teleport (consolidated)
**By:** Jeff (via Copilot), Gordon
**What:** Central "Crossroads of the World" hub at world origin (0, 0) — ornate with decorative fountains, trees lining streets, and Minecraft decorative elements. All village train lines connect here via hub-and-spoke topology (replaces point-to-point). Grid cell (0,0) reserved for Crossroads; villages start at grid position (1,0). Grand plaza 61×61 blocks, 15×15 multi-tier fountain, 4 tree-lined avenues, lampposts, benches, flower beds, banners, welcome signs. 16 radial platform slots (atan2 angle-based). New `CrossroadsGenerator` + `CrossroadsInitializationService` (BackgroundService, generates at startup). World spawn set to (0, -59, 0) via `/setworldspawn`. Player teleport via in-game `/goto <channel-name>` (Paper plugin, fuzzy match). New Bridge API endpoints: `GET /api/buildings/search`, `GET /api/buildings/{id}/spawn`. Plugin endpoint `POST /api/teleport`. Tab completion, 5s cooldown recommended. `/goto` works without account linking.
**Why:** User request (Jeff) — creates a natural central spawn point, simplifies navigation (hub-and-spoke, 2 rides max between any villages), scalable track topology (N tracks vs N×(N-1)/2). Beautiful spawn experience for new players. `/goto` reuses existing Bridge API infrastructure with minimal new endpoints.

### 2026-02-13: Building design, layout, and aesthetics (consolidated)
**By:** Batgirl
**What:** BuildingGenerator creates medieval castle-style buildings on a 4x4 grid layout:
- **Layout:** 4x4 grid with 27-block center spacing (21-block footprint + 6-block buffer). GridStartOffset 50 blocks from village center. Supports up to 16 buildings per village. Replaced original ring layout (radius 60) which caused overlap.
- **Aesthetics:** Medieval castle keep -- cobblestone walls with stone brick trim, oak log corner turrets with slab caps, crenellated parapet, arrow slit windows, 3-wide arched entrance, 3-wide staircase, wall-mounted torch lighting. 2 floors (reduced from original 4 for better proportions). 21x21 footprint.
- **Entrance sign:** Placed outside doorway at `(bx, BaseY+5, maxZ+1)` facing south -- visible to approaching players. Interior floating signs removed.
- **Block placement order:** foundation -> walls -> turrets -> clear interior -> floors -> stairs -> roof -> windows -> entrance -> lighting -> signs (last, on solid walls).
- **BuildingArchiver** updated to match 2-floor castle dimensions.
**Why:** Original design had overlapping buildings (ring layout), floating blocks (glowstone in air after interior clear), unusable 1-block stairs, floating signs, and generic aesthetics. Grid layout provides proper spacing. Medieval castle style gives distinctive character. Entrance sign outside doorway is visible to approaching players.

### 2026-02-13: Station platform design and placement (consolidated)
**By:** Batgirl, Jeff (via Copilot)
**What:** Station platforms are welcoming transit hubs positioned near the village plaza:
- **Placement:** Near the village plaza per Jeff's directive -- the plaza is "village central" so stations should feel connected to it. Original 30-block-south offset being adjusted closer.
- **Platform design:** 9x5 block platforms with shelter structure (oak fence corner posts with oak slab roof), hanging lanterns, oak stair benches, potted flowers. Improved signage with bold headers, destination AND origin village names, Minecraft color codes.
- **Track layout:** L-shaped rail paths (Minecraft rails don't support diagonals) at y=65 with stone brick trackbed at y=64. Powered rails every 8 blocks with redstone blocks underneath for permanent activation.
- **Platform slots:** Angle-deterministic using Atan2 -- each destination gets a unique slot so platforms don't overlap. Coordinate-based hash offset at L-path corners for track collision mitigation.
- **Amenities:** Button-activated dispensers with 64 minecarts per platform.
**Why:** User directive (Jeff) to position stations near plaza for village cohesion. Original bare slab platforms redesigned with shelter and amenities for a sense of arrival. L-shaped paths are the only option for Minecraft rails. Angle-based slot assignment is deterministic and stable.

### 2026-02-13: Sprint 4 plan
**By:** Gordon
**What:** Sprint 4: World & Experience — 8 work items focusing on the Crossroads hub, hub-and-spoke track topology, player teleport, village amenity improvements, E2E test completion, BlueMap marker wiring, and building variety. Account linking deferred again.
**Why:** Sprint 3 delivered the core pipeline (Discord→villages→tracks→commands). Sprint 4 shifts focus to making the world feel alive and navigable. The Crossroads hub (Jeff's request) is the centerpiece — it creates a beautiful spawn point, simplifies navigation with hub-and-spoke topology, and anchors the player experience. Carrying forward #7 and #10 ensures we don't accumulate test and integration debt. Building variety and village amenities make the world visually distinctive. Account linking remains deferred per Jeff's original request — the `/goto` command provides teleportation without it.

### 2026-02-13: Plugin HTTP port 8180 exposed via Aspire for marker wiring
**By:** Oracle
**What:** Bridge Plugin's HTTP API (port 8180) is now exposed as an Aspire endpoint (`plugin-http`) on the minecraft container. WorldGen.Worker receives `Plugin__BaseUrl` env var pointing to this endpoint, read as `Plugin:BaseUrl` in .NET config. `MarkerService` registered via `AddHttpClient<MarkerService>` with this base URL. All marker calls are best-effort (catch + log) — BlueMap markers are nice-to-have, never critical path.
**Why:** The WorldGen.Worker needs to call the plugin's marker endpoints after generation completes. Using Aspire's `GetEndpoint()` + `WithEnvironment()` pattern keeps service discovery consistent with the rest of the system. Fire-and-forget pattern ensures generation jobs never fail due to marker issues.

### 2026-02-13: /goto command uses Bridge API for building lookup
**By:** Oracle
**What:** The `/goto <channel-name>` in-game command queries the Bridge API (`/api/buildings/search` + `/api/buildings/{id}/spawn`) to find and teleport players to Discord channel buildings. No account linking required. Bridge API URL is configurable in the plugin's `config.yml` (`bridge-api-url`, default `http://localhost:5169`). Uses `java.net.http.HttpClient` (no external deps). 5-second cooldown prevents API spam.
**Why:** Keeps coordinate calculation logic server-side in the .NET API (single source of truth matching BuildingGenerator layout). The plugin stays thin — just HTTP calls + teleport.

### 2026-02-13: Hub-and-Spoke Track Topology (S4-02)
**By:** Batgirl
**What:** Track routing changed from point-to-point (N×(N-1)/2 tracks) to hub-and-spoke (1 track per village to Crossroads at origin). WorldGenJobProcessor creates exactly one CreateTrack job per village with destination Crossroads. TrackGenerator detects Crossroads destination and uses radial slot positioning via Atan2-based angle mapping to 16 slots at radius 35 (`WorldConstants.CrossroadsStationRadius`). No schema changes to TrackJobPayload/TrackGenerationRequest.
**Why:** Point-to-point created O(n²) tracks, causing overlapping rails, excessive forceloaded chunks, and confusing station sprawl. Hub-and-spoke gives O(n) tracks with clean radial arrivals at the central hub.

### 2026-02-13: Village station relocation to plaza edge (S4-04)
**By:** Batgirl
**What:** Village stations relocated from 20 blocks south of center to 17 blocks south (PlazaRadius + 2). VillageGenerator builds a 9×5 stone brick station area at the south plaza edge with a cobblestone walkway and directional sign. TrackGenerator's StationOffset now references `WorldConstants.VillageStationOffset` instead of a hardcoded value. New constants: `VillageStationOffset = 17`, `VillagePlazaInnerRadius = 15`.
**Why:** Jeff's directive — stations should be visible from the village plaza, not hidden behind buildings.

### 2026-02-13: Crossroads API and BlueMap URL configuration (S4-08)
**By:** Oracle
**What:** Added `BlueMap:WebUrl` config key to Bridge.Api for constructing BlueMap deep-link URLs in API responses (default `http://localhost:8200`). Separate from the Discord bot's `BlueMap:BaseUrl`. The `/api/crossroads` endpoint returns a `blueMapUrl` field with a deep-link centered on Crossroads origin. The `/crossroads` slash command calls this endpoint and displays the link in an embed.
**Why:** API responses need a publicly-accessible BlueMap URL for embedding in Discord messages. The Crossroads hub is always at world origin (0, 0) so the URL is deterministic.
