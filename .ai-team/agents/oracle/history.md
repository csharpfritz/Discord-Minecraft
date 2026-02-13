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

### Sprint 4 Summary (2026-02-13)

**S4-06 BlueMap Markers:** `MarkerService` in WorldGen.Worker/Services/ — typed HttpClient for Bridge Plugin marker endpoints (village/building create/archive). Fire-and-forget safe (catch + log). `SetMarkersForJobAsync` in WorldGenJobProcessor after successful jobs. Crossroads marker via `CrossroadsInitializationService`. Plugin port 8180 exposed via Aspire. Plugin default port is 8180 (config.yml), not 8080.

**S4-03 /goto Teleport:** Bridge API: `GET /api/buildings/search?q=` (fuzzy, top 10) + `GET /api/buildings/{id}/spawn` (coords from layout formula). `GotoCommand.java` — CommandExecutor + TabCompleter, java.net.http.HttpClient, 5s cooldown, cached tab completion (60s), Adventure ClickEvent for suggestions. Bridge API URL configurable via plugin `config.yml` (default `http://localhost:5169`).

**S4-08 Crossroads Integration:** `GET /api/crossroads` (hub info + BlueMap deep-link) + `GET /api/crossroads/map-url`. `/crossroads` slash command with DeferAsync pattern. `BlueMap:WebUrl` config key (default `http://localhost:8200`).

**World Broadcasts + Priority Queue:** `BroadcastBuildStartAsync`/`BroadcastBuildCompleteAsync` via `tellraw @a` (best-effort). `PopClosestJobAsync` replaces FIFO — Euclidean distance scoring, LSET+LREM sentinel pattern. `GetJobCenter` extracts coords from payloads.


### Sprint 5 — Player Welcome & BlueMap (2026-02-13)

- **S5-02 Player Welcome & Orientation.** `WelcomeListener.java` added to Bridge Plugin — handles `PlayerJoinEvent` with title overlay ("Welcome to {GuildName}") and actionbar hint ("Stand on golden pressure plate for a tour"). Pressure plate at Crossroads spawn (0, -59, 8) triggers 5-step walkthrough tour via Adventure title API: villages→buildings→minecarts→/goto→explore. Active walkthrough set prevents re-triggering. Delay-based sequencing using Bukkit `runTaskLater` (100-tick intervals = ~5 seconds per step).
- **Golden pressure plate.** `CrossroadsGenerator.GenerateSpawnPressurePlateAsync` places `light_weighted_pressure_plate` on gold block platform 8 blocks south of Crossroads center. 5-block gold accent cross for visual distinction.
- **Info kiosk.** `CrossroadsGenerator.GenerateInfoKioskAsync` places lectern with `written_book` 8 blocks east of center on quartz platform. Book has 5 pages (welcome, villages, buildings, minecarts, /goto) using `data merge block` RCON command with written_book_content component format.
- **Guild name configurable** via `guild-name` in plugin `config.yml` (default "Discord World"). Read in `WelcomeListener` constructor from plugin config.
- **S5-06 BlueMap Full Setup.** `/map` slash command extended with `village-name` string option. Village lookup queries `GET /api/villages` from Bridge API, fuzzy matches by name, constructs BlueMap deep-link URL with village center coordinates. Uses `DeferAsync()` pattern for the HTTP call path.
- **BlueMap setup documented** in README.md — JAR download instructions, `webserver.conf` port config (8200), Aspire port mapping reference, marker set descriptions, command reference table.
- **README sprint status updated** through Sprint 5, added Player Welcome & Orientation section.

 Team update (2026-02-13): World activity feed (WorldActivityFeedService) added to DiscordBot.Service  uses ConcurrentQueue + 5s rate limit, Discord:ActivityChannelId config  decided by Lucius

 Team update (2026-02-13): Discord Pins  Building Library (S5-03)  POST /api/buildings/{id}/pin endpoint available. Oracle/DiscordBot should call this when a Discord message is pinned  decided by Lucius
 Team update (2026-02-13): Dynamic building sizing (S5-08)  DiscordBotWorker must pass channel.Users.Count through sync payload for member-based building sizing  decided by Gordon

### Minecraft 1.21.4 Written Book Formats

- **Lectern RCON (`data merge block`):** In Minecraft 1.20.5+, `written_book_content` pages are **raw SNBT text components**, not single-quoted JSON strings. Use `[{text:"Hello",bold:true}]` not `'[{"text":"Hello","bold":true}]'`. The single-quoted form is treated as a plain-text string literal, causing raw JSON to display on screen.
- **Paper Plugin (Bukkit API):** `BookMeta.addPage(String)` is legacy and treats input as plain text. For Paper 1.21.4, use Adventure API: `Component.text(page)` with `BookMeta.pages(List<Component>)` to properly render book pages.
- **Key file paths:** CrossroadsGenerator.cs (`GenerateInfoKioskAsync`) for RCON book placement; HttpApiServer.java (`handleLectern`) for plugin-based lectern creation.
- **SNBT text component format:** `{text:"content",bold:true,color:"gold"}` — unquoted keys, unquoted boolean values, quoted string values.
