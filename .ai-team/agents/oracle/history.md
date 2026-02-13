# Project Context

- **Owner:** Jeffrey T. Fritz (csharpfritz@users.noreply.github.com)
- **Project:** Discord-to-Minecraft bridge — maps Discord channels to Minecraft villages/buildings with minecart navigation between channel groups. Creative/peaceful mode, .NET 10/Aspire 13.1/C#.
- **Stack:** .NET 10, Aspire 13.1, C#, Discord.NET, Minecraft protocol
- **Created:** 2026-02-11

## Learnings

<!-- Append new learnings below. Each entry is something lasting about the project. -->

- **S2-01 Discord event handlers implemented.** Three handlers wired: `ChannelCreated`, `ChannelDestroyed`, `ChannelUpdated`. Each detects category vs text channel via `ICategoryChannel` pattern match and publishes a `DiscordChannelEvent` DTO to Redis `events:discord:channel`.
- **Shared event DTOs in `Bridge.Data/Events/`.** `DiscordChannelEvent` is a unified record covering all 5 event types (ChannelGroupCreated, ChannelGroupDeleted, ChannelCreated, ChannelDeleted, ChannelUpdated). Uses `System.Text.Json` with `JsonSerializerDefaults.Web` (camelCase) and `JsonStringEnumConverter` for the enum.
- **Redis pub/sub via `IConnectionMultiplexer`** — injected into `DiscordBotWorker` from Aspire's `AddRedisClient("redis")`. Get `ISubscriber` once at startup, then use `PublishAsync(RedisChannel.Literal(...), json)` for each event.
- **`RedisChannels` constants class** in `Bridge.Data/Events/` — single source of truth for pub/sub channel names shared between producer (Oracle) and consumer (Lucius).
- **`GatewayIntents.Guilds`** is sufficient for channel create/destroy/update events — no additional intents needed for S2-01.
- **ChannelUpdated filtering** — Discord fires `ChannelUpdated` for many property changes (permissions, topic, etc.). We only publish when `Name` or `Position` actually changed to reduce noise for the consumer.

📌 Team update (2026-02-11): System architecture established — 3 .NET services (Discord Bot, Bridge API, WorldGen Worker) + Paper MC + PostgreSQL + Redis, orchestrated by Aspire 13.1 — decided by Gordon
📌 Team update (2026-02-11): Paper MC chosen as Minecraft server platform (itzg/minecraft-server Docker container, orchestrated by Aspire) — decided by Gordon
📌 Team update (2026-02-11): Sprint plan defined — 3 sprints: Foundation, Core Features, Integration & Navigation — decided by Gordon
📌 Team update (2026-02-11): Channel deletion archives buildings (does not destroy them) — decided by Gordon
📌 Team update (2026-02-11): Account linking via one-time 6-char codes with 5-min Redis TTL (no OAuth) — decided by Gordon

- **CoreRCON 5.4.2** — `RCON(IPAddress, ushort port, string password)` constructor, then `ConnectAsync()`, then `SendCommandAsync(string)`. Requires `System.Net.Dns.GetHostAddressesAsync` to resolve hostnames to IPAddress. Implements `IDisposable`.
- **Discord.Net 3.18.0** — `DiscordSocketClient` is the gateway client. Config via `DiscordSocketConfig` (set `GatewayIntents`, `LogLevel`). Register as singleton, inject into `BackgroundService`. Login with `LoginAsync(TokenType.Bot, token)` + `StartAsync()`.
- Discord.NET logging bridge: map `LogSeverity` enum to `Microsoft.Extensions.Logging.LogLevel` via switch expression. Wire `client.Log` event.
- Slash commands: use `SlashCommandBuilder` → `client.CreateGlobalApplicationCommandAsync()` in the `Ready` event. Handle via `client.SlashCommandExecuted` event with `SocketSlashCommand` parameter.
- Bot token config key: `Discord:BotToken` (reads from user secrets or env var `Discord__BotToken`)
- Gateway intents: `GatewayIntents.Guilds` is minimum for slash commands. Future S2-01 work will need `GatewayIntents.GuildMessages` etc.
- Graceful shutdown pattern: `Task.Delay(Timeout.Infinite, stoppingToken)` in a try/catch for `OperationCanceledException`, then `client.StopAsync()`.
- **File paths:**
  - `tools/RconTest/Program.cs` — RCON connectivity PoC (S1-05). Usage: `dotnet run -- <host> <port> <password>`
  - `src/DiscordBot.Service/Program.cs` — DI setup, registers `DiscordSocketClient` and `DiscordBotWorker`
  - `src/DiscordBot.Service/DiscordBotWorker.cs` — `BackgroundService` with gateway connection, logging, `/ping` command
- Solution file is `DiscordMinecraft.slnx` (XML-based .slnx format, not classic .sln)
- RconTest added to solution under `/tools/` folder

📌 Team update (2026-02-11): Test projects under tests/{ProjectName}.Tests/, CI at .github/workflows/ci.yml with .NET 10 — decided by Nightwing
📌 Team update (2026-02-11): Snake_case PostgreSQL table names with PascalCase C# entities — decided by Lucius
📌 Team update (2026-02-11): RCON password as Aspire secret parameter via builder.AddParameter("rcon-password", secret: true) — decided by Lucius
📌 Team update (2026-02-11): EF Core enum-to-string conversion for GenerationJobStatus — decided by Lucius
📌 Team update (2026-02-11): Discord bot token as Aspire secret parameter — passed via env var Discord__BotToken, reads as Discord:BotToken in .NET config — decided by Lucius
📌 Team update (2026-02-11): Sprint 2 interface contracts established — Redis event schema, job queue format, API endpoints, WorldGen interfaces, shared constants — decided by Gordon
📌 Team update (2026-02-11): Bridge API endpoints + nullable coordinate columns (VillageX/Z, BuildingX/Z) — sync endpoint available for bot Ready event — decided by Lucius
📌 Team update (2026-02-11): Event consumer uses DiscordChannelEvent.FromJson() from Bridge.Data.Events — ChannelUpdated filtered to name/position changes only — decided by Lucius
📌 Team update (2026-02-11): DefaultIfEmpty(-1).MaxAsync() replaced with nullable Max() pattern in prod code for cross-provider compatibility — decided by Nightwing
📌 Team update (2026-02-12): Sprint work items are now GitHub Issues with milestones and squad-colored labels — decided by Jeff and Gordon

 Team update (2026-02-12): README.md created with project overview, architecture, getting started, and squad roster with shields.io badges  decided by Gordon

📌 Team update (2026-02-12): Account linking deferred from Sprint 3 — S3-02 closed, /link removed from S3-01 (Paper Bridge Plugin), /unlink removed from S3-06 (Discord slash commands) — decided by Jeffrey T. Fritz
📌 Team update (2026-02-12): Only publicly accessible Discord channels are mapped to Minecraft village buildings — private/restricted channels excluded — decided by Jeffrey T. Fritz

- **Paper Bridge Plugin (S3-01)** created under `src/discord-bridge-plugin/`. Java/Gradle project targeting Paper API 1.21.4, Java 21 toolchain. Package: `com.discordminecraft.bridge`.
- **Plugin architecture:** `BridgePlugin` (JavaPlugin) → `HttpApiServer` (JDK HttpServer on configurable port), `RedisPublisher` (Jedis pool), `PlayerEventListener` (Bukkit events).
- **HTTP API endpoints:** `GET /health` (server status), `POST /api/command` (execute server commands on main thread), `GET /api/players` (list online players with coordinates).
- **Redis player events:** Published to `events:minecraft:player` channel. Schema: `{ eventType, playerUuid, playerName, timestamp }` in camelCase JSON via Gson. Async publish via Bukkit scheduler to avoid blocking main thread.
- **RedisPublisher uses Jedis 5.2.0** with `JedisPool` (maxTotal=4, maxIdle=2). Channel constant `CHANNEL_PLAYER_EVENT` must match `RedisChannels.MinecraftPlayer` in Bridge.Data.
- **`MinecraftPlayerEvent` DTO** added to `Bridge.Data/Events/` — mirrors Java-side schema for .NET consumer deserialization. Uses same `JsonSerializerDefaults.Web` pattern as `DiscordChannelEvent`.
- **`RedisChannels.MinecraftPlayer`** constant added: `"events:minecraft:player"` — shared between Java plugin (producer) and .NET services (consumer).
- **Plugin config:** `plugins/DiscordBridge/config.yml` — `http-port` (default 8080), `redis.host`, `redis.port`. Loaded via Paper's `saveDefaultConfig()`/`getConfig()`.
- **Gradle build** copies output JAR to `src/AppHost/minecraft-data/plugins/` via `tasks.jar { doLast { ... } }`.
- **Thread safety pattern:** All Bukkit API calls in HTTP handlers dispatched to main thread via `Bukkit.getScheduler().runTask()`. Redis publishes dispatched async via `runTaskAsynchronously()`.
- **Concurrent git environment:** When multiple agents share a working directory, use git plumbing (`read-tree`, `write-tree`, `commit-tree`, `update-ref`) with `GIT_INDEX_FILE` to avoid branch-switching races.

- **BlueMap integration (S3-08)** added to Bridge Plugin on branch `squad/10-bluemap`. BlueMapAPI 2.7.2 as `compileOnly` dependency (BlueMap JAR provides the classes at runtime). Maven repo: `https://repo.bluecolored.de/releases`.
- **BlueMapIntegration class** manages two marker sets: `discord-villages` and `discord-buildings`. Uses `BlueMapAPI.onEnable`/`onDisable` lifecycle hooks. Markers cached in `ConcurrentHashMap` and restored on API reload. Only overworld maps receive markers (filters out nether/end).
- **Marker HTTP endpoints** added to Bridge Plugin's `HttpApiServer`: `POST /api/markers/village`, `POST /api/markers/building`, `POST /api/markers/building/archive`, `POST /api/markers/village/archive`. Body schema: `{ "id", "label", "x", "z" }` for create; `{ "id" }` for archive.
- **BlueMap port mapping** — port 8100 exposed on the Paper MC container via Aspire's `.WithEndpoint(targetPort: 8100, port: 8100, name: "bluemap", scheme: "http")`. HTTP scheme (not TCP) because BlueMap serves a web UI.
- **BlueMap base URL wiring** — `BlueMap__BaseUrl` env var passed to Discord bot via `.WithEnvironment("BlueMap__BaseUrl", minecraft.GetEndpoint("bluemap"))`. Discord bot reads it as `configuration["BlueMap:BaseUrl"]` with fallback to `http://localhost:8100`.
- **BlueMap config files** version-controlled at `src/AppHost/minecraft-data/plugins/BlueMap/core.conf` and `webserver.conf`. Key settings: `accept-download: true`, `ip: "0.0.0.0"` (required for Docker port mapping), `port: 8100`.
- **`plugin.yml` softdepend** — BlueMap declared as `softdepend` so the plugin loads after BlueMap but doesn't fail if BlueMap is absent. The `BlueMapIntegration.isAvailable()` check gates marker operations.
- **`/map` slash command** already existed in the codebase from S3-06 work — reads `BlueMap:BaseUrl` from config, supports optional `channel` parameter for deep-linking to building markers via `#discord-buildings:{channelId}` hash fragment.
- **Slash commands: /status and /navigate (S3-06).** Added two new slash commands to `DiscordBotWorker`. `/status` calls `GET /api/status` returning village and building counts. `/navigate <channel>` calls `GET /api/navigate/{discordChannelId}` returning village name, building index, and XYZ coordinates. Both use `DeferAsync()` + `FollowupAsync()` pattern since they make HTTP calls. Edge cases: unmapped channels get a clear "no mapping" message, API failures show a warning.
- **Bridge API endpoints for slash commands.** Added `GET /api/status` (village/building count) and `GET /api/navigate/{discordChannelId}` (channel→village/building mapping with coordinates) to `Bridge.Api/Program.cs`. Navigate endpoint includes `ChannelGroup` via `Include()` join.
- **HttpClient via Aspire service discovery.** `IHttpClientFactory` with named client `"BridgeApi"` using `https+http://bridge-api` base address. Aspire's `WithReference(bridgeApi)` in AppHost makes the service discoverable. `AddServiceDefaults()` already configures service discovery and resilience on all HttpClients.
- **Slash command response records.** `StatusResponse` and `NavigateResponse` are private records inside `DiscordBotWorker` for `ReadFromJsonAsync<T>()` deserialization. Uses `System.Net.Http.Json` (built into .NET 10).
- **Concurrent branch hazard in shared workdir.** When multiple agents run simultaneously, another agent can switch the `HEAD` branch mid-work. Always verify `git branch` before committing — cherry-pick to correct branch if HEAD was moved.
📌 Team update (2026-02-12): Track routing triggered by village creation — WorldGenJobProcessor enqueues CreateTrack jobs after CreateVillage completes — decided by Batgirl
📌 Team update (2026-02-12): RCON config fixes — port mapping (targetPort: 25575, port: 25675) and URI parsing in RconService — decided by Lucius
📌 Team update (2026-02-12): MinecraftHealthCheck added — Aspire dashboard shows MC as unhealthy until RCON responds — decided by Lucius
📌 Team update (2026-02-12): Sync endpoint now creates GenerationJob records and pushes to Redis queue — decided by Oracle
📌 Team update (2026-02-12): Minecart track layout — L-shaped paths at y=65, stations 30 blocks south of village center, angle-based platform slots — decided by Batgirl
📌 Team update (2026-02-12): Channel deletion now enqueues ArchiveBuilding/ArchiveVillage jobs to Redis worldgen queue — BuildingArchiver updates signs + blocks entrances — decided by Lucius
📌 Team update (2026-02-12): BlueMap integration added as S3-08 — drop-in Paper plugin, port 8100 via Aspire, Java API markers, /map Discord command (Oracle owns) — decided by Gordon
📌 Team update (2026-02-12): Sprint 3 test specs written — 14 channel deletion + 8 E2E smoke tests, reusing BridgeApiFactory — decided by Nightwing
📌 Team update (2026-02-12): Port reassignment — decided by Lucius, requested by Jeff

- **Startup guild sync (`SyncGuildsAsync`).** Added to `DiscordBotWorker.Ready` handler, called AFTER `RegisterSlashCommandsAsync`. Iterates `client.Guilds`, collects publicly accessible category+text channels (filters out channels where @everyone has `ViewChannel` explicitly denied via `GetPermissionOverwrite`), builds `SyncRequest` payload, and POSTs to `/api/mappings/sync` per guild. Wrapped in try/catch so sync failure doesn't prevent bot operation. Uses `IHttpClientFactory.CreateClient("BridgeApi")` with Aspire service discovery.
- **Public channel filtering pattern.** `guild.EveryoneRole` → `channel.GetPermissionOverwrite(everyoneRole)` → check `PermValue.Deny` on `ViewChannel`. Only explicit denies are filtered; inherit/allow both pass through. Applied at both category and individual text channel level.
- **Local sync DTOs.** `SyncRequest`, `SyncChannelGroup`, `SyncChannel` records defined as private nested types in `DiscordBotWorker` to match the Bridge API's endpoint contract. Not shared via Bridge.Data since they're only used by the bot's HTTP call.
- **Sync endpoint now creates WorldGen jobs.** The `/api/mappings/sync` POST endpoint was only creating DB records (ChannelGroup, Channel) but NOT creating GenerationJob records or pushing to the Redis WorldGen queue — meaning synced channels appeared in `/status` counts but nothing got built in Minecraft. Fixed by injecting `IConnectionMultiplexer`, creating `VillageJobPayload`/`BuildingJobPayload` + `GenerationJob` + `WorldGenJob` for NEW records only (not updates), matching the exact pattern from `DiscordEventConsumer.cs`. This ensures bot startup sync triggers actual village/building generation. Idempotent — re-running sync won't duplicate jobs for existing records.
- **S3-06 slash commands verified complete.** Commands implemented: `/ping`, `/status`, `/navigate`, `/map`, `/unlink`. The `/status` and `/navigate` commands use `DeferAsync()` + `FollowupAsync()` pattern with Bridge API calls. `/unlink` is a stub returning "Account linking not yet available" (feature deferred per Sprint 3 decisions). All commands registered as global in `RegisterSlashCommandsAsync` during client Ready event.
