# Project Context

- **Owner:** Jeffrey T. Fritz (csharpfritz@users.noreply.github.com)
- **Project:** Discord-to-Minecraft bridge — maps Discord channels to Minecraft villages/buildings with minecart navigation between channel groups. Creative/peaceful mode, .NET 10/Aspire 13.1/C#.
- **Stack:** .NET 10, Aspire 13.1, C#, Discord.NET, Minecraft protocol
- **Created:** 2026-02-11

## Learnings

<!-- Append new learnings below. Each entry is something lasting about the project. -->

### Sprint 1–3 Summary (2026-02-11 → 2026-02-12)

**S2-01 Discord Event Handlers:** Three handlers (ChannelCreated, ChannelDestroyed, ChannelUpdated) publish DiscordChannelEvent DTOs to Redis `events:discord:channel`. Shared DTOs in Bridge.Data/Events/. JsonSerializerDefaults.Web (camelCase) + JsonStringEnumConverter. GatewayIntents.Guilds sufficient. ChannelUpdated filtered to name/position changes.

**Discord Bot Foundation:** DiscordSocketClient singleton + BackgroundService pattern. Slash commands via SlashCommandBuilder in Ready event. Config: Discord:BotToken. Graceful shutdown via Task.Delay(Timeout.Infinite, stoppingToken). Solution file: DiscordMinecraft.slnx.

**S3-01 Paper Bridge Plugin:** Java/Gradle, Paper API 1.21.4, Java 21. JDK HttpServer + Jedis 5.2.0 + Bukkit scheduler. HTTP endpoints: /health, /api/command, /api/players. Player events on events:minecraft:player. All Bukkit API calls dispatched to main thread. Gradle copies JAR to plugins/. Concurrent git: use read-tree/write-tree plumbing with GIT_INDEX_FILE.

**S3-08 BlueMap Integration:** BlueMapAPI 2.7.2 compileOnly. BlueMapIntegration class with two marker sets (discord-villages, discord-buildings). HTTP marker endpoints: POST /api/markers/{village,building,building/archive,village/archive}. Port 8200 via Aspire. /map slash command with channel deep-linking.

**S3-06 Slash Commands:** /ping, /status, /navigate, /map, /unlink (stub). DeferAsync+FollowupAsync pattern for HTTP calls. HttpClient via Aspire service discovery ("BridgeApi", https+http://bridge-api).

**Startup Guild Sync:** SyncGuildsAsync in Ready handler after command registration. Filters by @everyone ViewChannel permission. POSTs SyncRequest per guild. Sync endpoint fixed to create GenerationJobs + push to Redis (not just DB records).

**Key Learnings:**
- Redis pub/sub: IConnectionMultiplexer → ISubscriber, PublishAsync with RedisChannel.Literal
- Concurrent branch hazard: verify git branch before committing in shared workdir
- Discord.NET: LogSeverity→LogLevel bridge via switch expression; GatewayIntents.Guilds is minimum

📌 Team update (2026-02-13): Village amenities — walkways, scalable fountains, interior sign fix — decided by Batgirl
📌 Team update (2026-02-13): Crossroads hub + spawn + teleport consolidated — central hub at origin (0,0), hub-and-spoke, /goto command in Paper plugin, new API endpoints needed — decided by Jeff, Gordon
📌 Team update (2026-02-13): Sprint 4 plan — 8 work items: Crossroads hub, hub-and-spoke tracks, player teleport, building variety, station relocation, BlueMap markers, E2E tests, Crossroads integration. Account linking deferred again — decided by Gordon
📌 Team update (2026-02-13): Plugin HTTP port 8180 exposed via Aspire for marker wiring — Plugin HTTP API now reachable from WorldGen.Worker — decided by Oracle
📌 Team update (2026-02-13): /goto command uses Bridge API (/api/buildings/search + /api/buildings/{id}/spawn) for building lookup and teleport — decided by Oracle

- **BlueMap marker wiring (S4-06).** Created `MarkerService` in `WorldGen.Worker/Services/` — typed HttpClient calling the Bridge Plugin's marker HTTP endpoints (POST `/api/markers/village`, `/api/markers/building`, `/api/markers/building/archive`, `/api/markers/village/archive`). All methods are fire-and-forget safe (catch + log, never throw). Registered via `AddHttpClient<MarkerService>` with `Plugin:BaseUrl` config (default `http://localhost:8180`).
- **Marker calls after generation.** `WorldGenJobProcessor.SetMarkersForJobAsync()` dispatches marker calls after successful `CreateVillage`, `CreateBuilding`, `ArchiveBuilding`, and `ArchiveVillage` jobs. Building marker coordinates computed using the same layout formula as `BuildingGenerator` (row/position from `BuildingIndex`, spacing of 24, row offset of ±20). Wrapped in outer try/catch for belt-and-suspenders safety.
- **Crossroads marker.** `CrossroadsInitializationService` calls `SetVillageMarkerAsync("crossroads", "⭐ Crossroads", 0, 0)` after successful hub generation.
- **Plugin HTTP port exposed via Aspire.** Added `.WithEndpoint(targetPort: 8180, port: 8180, name: "plugin-http", scheme: "http")` to the minecraft container in `AppHost.cs`. WorldGen.Worker receives `Plugin__BaseUrl` env var via `.WithEnvironment("Plugin__BaseUrl", minecraft.GetEndpoint("plugin-http"))`.
- **Plugin default port is 8180** (configured in `src/discord-bridge-plugin/src/main/resources/config.yml`). The README says 8080 but that's outdated — the actual config.yml default is 8180.

- **S4-03 /goto teleport command implemented.** Two new Bridge API endpoints: `GET /api/buildings/search?q={query}` (fuzzy channel name search, returns top 10 non-archived matches with village info) and `GET /api/buildings/{id}/spawn` (calculates teleport coordinates from village center + building index using BuildingGenerator layout logic: row=index%2, posInRow=index/2, bx=centerX+(posInRow-3)*24, bz=centerZ±20, entrance at bz+11, y=BaseY+1=-59).
- **GotoCommand.java** added to Paper Bridge Plugin. Implements `CommandExecutor` + `TabCompleter`. Uses `java.net.http.HttpClient` (no external deps) to query Bridge API. 5-second per-player cooldown via `ConcurrentHashMap<UUID, Long>`. Tab completion caches channel names from search API, refreshes every 60s. Multiple matches shown as clickable suggestions using Adventure `ClickEvent.runCommand`. All HTTP calls run on Bukkit async scheduler; teleport dispatched back to main thread.
- **Bridge API URL configurable** via `bridge-api-url` in plugin `config.yml` (default `http://localhost:5169`). Read in `BridgePlugin.onEnable()` and passed to `GotoCommand` constructor.
- **plugin.yml updated** with `goto` command definition including description, usage, and `discordbridge.goto` permission.

- **S4-08 Crossroads BlueMap & Discord integration.** Two new Bridge API endpoints: `GET /api/crossroads` (returns Crossroads hub info including name, coordinates, ready status from Redis `crossroads:ready` key, description, and BlueMap deep-link URL) and `GET /api/crossroads/map-url` (returns BlueMap URL centered on Crossroads at origin). BlueMap URL constructed from `BlueMap:WebUrl` config key (default `http://localhost:8200`) with hash fragment `#world:0:0:0:64:0:0:0:0:flat`.
- **`/crossroads` slash command** added to `DiscordBotWorker`. Uses `DeferAsync()` + `FollowupAsync()` pattern (consistent with `/status` and `/navigate`). Calls `GET /api/crossroads`, displays rich embed with name, description, coordinates, ready status, getting-there instructions (`/goto crossroads` or minecart), and clickable BlueMap link. `CrossroadsResponse` record added for deserialization.
- **BlueMap:WebUrl config key** — the Bridge API reads `BlueMap:WebUrl` (default `http://localhost:8200`) for constructing BlueMap deep-link URLs in the crossroads response. This is separate from the Discord bot's `BlueMap:BaseUrl` since the API returns URLs for external consumption. BlueMap port 8200 is exposed via Aspire in AppHost.cs.
📌 Team update (2026-02-13): Hub-and-Spoke track topology — each village gets one track to Crossroads, O(n) instead of O(n²), radial slot positioning at Crossroads — decided by Batgirl
📌 Team update (2026-02-13): Village station relocation to plaza edge — VillageStationOffset=17 (PlazaRadius+2), shared constant in WorldConstants — decided by Batgirl

- **World broadcast messages via RCON `tellraw @a`.** Added `BroadcastBuildStartAsync` and `BroadcastBuildCompleteAsync` methods to `WorldGenJobProcessor`. Each uses a switch on `JobType` to build colorful JSON text components (⚒ yellow for village start, 🏗 aqua for building, 🚂 green for tracks, ✅ green for completions). Best-effort pattern — exceptions caught and logged at Debug level, never fail the job.
- **Helper methods for payload name extraction.** `GetVillageName(job)` → `VillageJobPayload.VillageName`, `GetBuildingName(job)` → `BuildingJobPayload.ChannelName`, `GetTrackNames(job)` → `(TrackJobPayload.SourceVillageName, DestinationVillageName)`. All return null on deserialization failure.
- **Spawn-proximity priority queue.** `PopClosestJobAsync` replaces FIFO `ListRightPopAsync` in `ExecuteAsync`. For queues with >1 item, peeks all entries via `ListRangeAsync`, scores by Euclidean distance from origin using `GetJobCenter`, and removes the closest via LSET+LREM sentinel pattern. Single-item queues still use simple pop.
- **`GetJobCenter` extracts coordinates** from job payloads: `(CenterX, CenterZ)` for villages/buildings, `(SourceCenterX, SourceCenterZ)` for tracks. Returns `(int.MaxValue, int.MaxValue)` on failure so unknown jobs sort to end.
- **Crossroads completion broadcast.** `CrossroadsInitializationService` now injects `RconService` and sends `tellraw @a` with gold ⭐ after successful hub generation. Same best-effort catch pattern.
- **RconService already registered as singleton** in `Program.cs` — no DI registration changes needed. Primary constructor injection in both services.
