# Discord-Minecraft Bridge — Sprint Plans

> **Author:** Gordon (Lead / Architect)  
> **Date:** 2026-02-11  
> **Sprint cadence:** 2 weeks  
> **Team:** Gordon (Architect), Oracle (Integration), Lucius (Backend), Batgirl (World Builder), Nightwing (QA)

---

## Sprint 1: Foundation

**Goal:** Project scaffolding, Aspire orchestration, core infrastructure running locally. By sprint end, `dotnet run` on the AppHost starts PostgreSQL, Redis, and a Paper MC server in containers alongside empty .NET service shells.

| ID | Title | Description | Assigned | Size | Dependencies |
|----|-------|-------------|----------|------|--------------|
| S1-01 | Aspire AppHost scaffolding | Create the .NET Aspire 13.1 solution structure: `AppHost`, `ServiceDefaults`, and empty projects for `DiscordBot.Service`, `Bridge.Api`, `WorldGen.Worker`. Wire up `Program.cs` in AppHost with project references. Use `dotnet new aspire-starter` as base. | Lucius | M | — |
| S1-02 | PostgreSQL + Redis resources | Add PostgreSQL and Redis container resources to the AppHost. Define connection string references. Verify both start and are reachable from service projects via Aspire service discovery. | Lucius | S | S1-01 |
| S1-03 | Paper MC container resource | Add the `itzg/minecraft-server` container to Aspire AppHost with Paper type, creative mode, peaceful difficulty, flat world, RCON enabled. Verify server starts and accepts RCON connections. Define volume mount for world data persistence. | Lucius | M | S1-01 |
| S1-04 | EF Core data model + migrations | Design and implement the PostgreSQL schema using EF Core: `ChannelGroups`, `Channels`, `Players`, `WorldState`, `GenerationJobs` tables. Create initial migration. Seed with test data. Place in a shared `Bridge.Data` class library project. | Lucius | M | S1-02 |
| S1-05 | RCON connectivity proof-of-concept | Create a minimal console test that connects to the Paper MC server via CoreRCON, sends a `/list` command, and receives a response. This validates the RCON integration path. Place in a `tools/RconTest` project. | Oracle | S | S1-03 |
| S1-06 | Discord.NET bot shell | Create the Discord Bot Service as a .NET Worker Service with Discord.NET. Implement gateway connection, logging, and a single `/ping` slash command. Bot token configured via user secrets. Does not need to do anything useful yet — just proves connectivity. | Oracle | M | S1-01 |
| S1-07 | CI pipeline | Set up GitHub Actions workflow: build all projects, run `dotnet test` (even though tests are minimal), verify solution compiles on .NET 10. | Nightwing | S | S1-01 |

**Sprint 1 Definition of Done:**
- `dotnet run --project AppHost` starts all services + containers
- Aspire dashboard shows all 3 .NET services + PostgreSQL + Redis + Paper MC
- RCON test successfully sends a command to Paper MC
- Discord bot connects to a test server and responds to `/ping`
- CI pipeline passes on push

---

## Sprint 2: Core Features

**Goal:** Discord bot monitors channel structure, world generation produces villages and buildings, Bridge API serves as the coordination point. By sprint end, creating a channel category in Discord results in a village appearing in the Minecraft world.

| ID | Title | Description | Assigned | Size | Dependencies |
|----|-------|-------------|----------|------|--------------|
| S2-01 | Discord event handlers | Implement Discord.NET event handlers for: `ChannelCreated`, `ChannelDestroyed`, `ChannelUpdated`, `CategoryCreated`. Publish structured events to Redis pub/sub. Events include channel ID, name, category, guild ID, timestamp. | Oracle | M | S1-06 |
| S2-02 | Bridge API core endpoints | Implement REST endpoints: `POST /api/mappings/sync` (full Discord→DB sync), `GET /api/villages` (list villages), `GET /api/villages/{id}/buildings` (list buildings in village), `POST /api/players/link` (initiate account link). Wire up EF Core context. | Lucius | L | S1-04 |
| S2-03 | Event consumer + job queue | Bridge API subscribes to Discord events on Redis. On channel group create → insert `ChannelGroup` record, enqueue a `CreateVillage` job. On channel create → insert `Channel` record, enqueue a `CreateBuilding` job. Job payloads are JSON in a Redis list. | Lucius | M | S2-01, S2-02 |
| S2-04 | Village generation algorithm | Implement the village spatial planner in WorldGen Worker: given a village index, calculate center coordinates (500-block grid). Generate village plaza (stone brick platform, signs, lighting). Execute via RCON `/fill` commands. | Batgirl | L | S1-05 |
| S2-05 | Building generation algorithm | Implement building generation: given village coordinates and a building index, calculate placement position within the village ring. Generate a 21×21, 4-floor building with interior space, stairs, doors, signs. Use RCON `/fill` for walls/floors, `/setblock` for details. | Batgirl | L | S2-04 |
| S2-06 | WorldGen Worker job processor | Implement the background job processor in WorldGen Worker. Dequeue jobs from Redis, dispatch to village/building generators, update job status in PostgreSQL. Include retry logic with exponential backoff (3 attempts). | Lucius | M | S2-04, S2-05 |
| S2-07 | Integration tests — channel to village | Write integration tests that simulate a Discord channel group creation event, verify it flows through Redis → Bridge API → WorldGen Worker → RCON commands. Use test containers for PostgreSQL and Redis. Mock RCON responses. | Nightwing | M | S2-03, S2-06 |

**Sprint 2 Definition of Done:**
- Creating a category in Discord triggers village generation in Minecraft
- Creating a channel within a category triggers building generation
- Village and building coordinates are persisted in PostgreSQL
- Buildings have visible channel names on signs
- Integration test passes end-to-end (with mocked RCON)

---

## Sprint 3: Integration & Navigation

**Goal:** Account linking works, minecart tracks connect villages, and players can navigate the world meaningfully. By sprint end, a Discord user can link their account, join the Minecraft server, and ride minecarts between channel group villages.

| ID | Title | Description | Assigned | Size | Dependencies |
|----|-------|-------------|----------|------|--------------|
| S3-01 | Paper Bridge Plugin (Java) | Create a Paper plugin (`discord-bridge-plugin`) that: exposes an HTTP API on a configurable port for receiving commands from the .NET services, handles `/link <code>` in-game command, reports player join/leave events to Redis. Build with Gradle, output JAR to the Aspire-mounted plugins directory. | Oracle | L | S1-03 |
| S3-02 | Account linking flow | Implement full account linking: Discord `/link` command generates a 6-character code stored in Redis (5-min TTL). Player types `/link <code>` in Minecraft. Bridge Plugin sends code to Bridge API for validation. On success, creates player record in PostgreSQL and teleports player to their channel group's village. | Oracle | L | S3-01, S2-02 |
| S3-03 | Minecart track generation | Implement rail track generator: calculate direct paths between village station platforms. Lay powered rails every 8 blocks on a slightly elevated trackbed (y=65). Place station structures at each village with departure platforms, destination signs, and button-activated minecart dispensers. | Batgirl | L | S2-04 |
| S3-04 | Track routing on village creation | When a new village is created, WorldGen Worker automatically generates tracks to all existing villages. Update station signs at existing villages to include the new destination. Track generation is queued as a follow-up job after village creation completes. | Batgirl | M | S3-03, S2-06 |
| S3-05 | Channel deletion handling | When a Discord channel is deleted: mark the building as archived in PostgreSQL, update the building's signs to show "[Archived]", block the entrance with barrier blocks. When a category is deleted: archive all buildings in the village, update station signs. Do NOT destroy structures. | Lucius | M | S2-03 |
| S3-06 | Discord slash commands | Implement remaining slash commands: `/status` (shows world stats — village count, player count, link status), `/navigate <channel>` (shows which village and building a channel maps to, with coordinates), `/unlink` (removes account link). | Oracle | M | S2-02 |
| S3-07 | End-to-end smoke tests | Write end-to-end test scenarios: (1) Full sync of a Discord server's channel structure → world generation, (2) Account link flow, (3) Channel create → building appears, (4) Channel delete → building archived. Use Aspire's testing support with real containers. | Nightwing | L | S3-02, S3-04, S3-05 |

**Sprint 3 Definition of Done:**
- Player can link Discord and Minecraft accounts via code exchange
- Linked player spawns at their channel group's village
- Minecart tracks connect all villages with working stations
- New villages automatically get track connections to existing villages
- Channel deletion archives buildings gracefully
- Slash commands provide useful world information
- E2E smoke tests pass with real containers

---

## Sprint 4: World Polish & Player Experience

**Goal:** Crossroads hub as the central spawn, hub-and-spoke track topology, building variety, player teleportation, BlueMap markers, and E2E test wiring. By sprint end, players spawn at Crossroads, see three medieval building styles, ride minecarts to any village, and use `/goto` to teleport to specific channels.

| ID | Title | Description | Assigned | Size | Dependencies |
|----|-------|-------------|----------|------|--------------|
| S4-01 | Crossroads Hub Generation | Generate the central Crossroads hub at world origin (0,0): 61×61 checkerboard plaza, multi-tier fountain, four tree-lined avenues, welcome signs, banners, and world spawn point. Set `crossroads:ready` Redis key on completion so WorldGen Worker can begin processing village jobs. | Batgirl | L | — |
| S4-02 | Hub-and-Spoke Track Topology | Replace O(n²) star track topology with hub-and-spoke: each village gets exactly one track to Crossroads. Radial station slot positioning at Crossroads hub (16 slots). Automatic track enqueue after village creation completes. | Batgirl | L | S4-01 |
| S4-03 | Player Teleport /goto Command | Add `/goto <channel>` Discord slash command: Bridge API search endpoint (`/api/buildings/search`) + spawn coordinate endpoint (`/api/buildings/{id}/spawn`). Paper plugin `/goto` in-game command with HTTP API for teleportation. | Oracle | M | — |
| S4-04 | Station Relocation Near Plaza | Move village station platforms from 30 blocks south to plaza edge (VillageStationOffset=17, PlazaInnerRadius+2). Update WorldConstants and track/station generators. Players should walk a short distance from plaza to station, not trek across empty terrain. | Batgirl | S | S4-02 |
| S4-05 | Building Style Variety | Add three medieval building styles: MedievalCastle (stone brick, turrets), TimberCottage (oak/spruce frame, cozy), StoneWatchtower (cobblestone, narrow/tall). Style selected deterministically from channel ID. Each style has distinct wall materials, roofing, and entrance design. | Batgirl | L | — |
| S4-06 | BlueMap Marker Wiring | Implement MarkerService in WorldGen Worker: HTTP calls to Paper Bridge Plugin's marker API (`/api/markers/village`, `/api/markers/building`). Best-effort marker placement after village/building generation. Archive markers on channel deletion. | Oracle | M | S4-01 |
| S4-07 | E2E Smoke Tests Aspire Wiring | Build the Aspire-based full-stack test infrastructure: FullStackFixture that launches entire Aspire stack (Bridge API, Redis, PostgreSQL, Paper MC, WorldGen Worker), DiscordEventPublisher for simulating Discord events, BlueMapClient for validating markers. Acceptance.Tests project with xUnit + Aspire testing. | Nightwing | L | — |
| S4-08 | Crossroads BlueMap & Discord Integration | Wire Crossroads info into Discord (`/crossroads` slash command with coordinates, status, BlueMap link) and Bridge API (`/api/crossroads` endpoint). Configure BlueMap web URL in Aspire AppHost. BlueMap deep-link URL format for Crossroads view. | Oracle | M | S4-01, S4-06 |

**Post-Sprint 4 — RCON Performance Optimization (unplanned):**
- Batch API: `SendBatchAsync` for multiple RCON commands in a single semaphore acquisition
- Adaptive delay: 50ms→10ms base, min 5ms, exponential backoff on failure
- Fill consolidation: ~7,100→~2,600 commands per village (6.5 min→~30 sec)
- World broadcast messages: `tellraw @a` during build start/complete
- Spawn-proximity priority queue: `PopClosestJobAsync` builds nearest-to-origin first

**Sprint 4 Definition of Done:**
- Players spawn at Crossroads hub with fountain, avenues, and welcome signs
- Hub-and-spoke tracks connect each village to Crossroads (O(n) topology)
- `/goto <channel>` teleports player to the correct building entrance
- Three distinct building styles visible in the world
- Village stations are near the plaza, not 30 blocks away
- BlueMap markers appear for villages and buildings
- `/crossroads` Discord command shows hub info with BlueMap link
- Full-stack test infrastructure runs against real Aspire containers
- ✅ All items completed

---

## Sprint 5: Immersion & Onboarding

**Goal:** Make the world feel alive and welcoming. New players get guided into the experience, buildings have furnished interiors that reflect their Discord channels, the world has ambient life, and Discord↔Minecraft communication goes deeper. By sprint end, a player joining for the first time understands where they are and what to do, buildings feel inhabited rather than hollow, and the Discord community can interact with the Minecraft world in richer ways.

| ID | Title | Description | Assigned | Size | Dependencies |
|----|-------|-------------|----------|------|--------------|
| S5-01 | Building Interior Furnishing | Buildings are currently hollow shells. Add furnished interiors based on building style: MedievalCastle gets throne room + armory + banquet table, TimberCottage gets hearth + bookshelves + beds, StoneWatchtower gets map table + brewing stands + observation deck. Each floor should have a distinct purpose. Use channel topic (if set) as a wall sign description on the ground floor. Extend `BuildingGenerator` with per-style interior methods. | Batgirl | L | — |
| S5-02 | Player Welcome & Orientation | First-time experience when a player joins: title screen overlay ("Welcome to {GuildName}"), actionbar hint ("Stand on the golden pressure plate for a tour"), a golden pressure plate at Crossroads spawn that triggers a guided walkthrough via command blocks — explains villages, buildings, minecarts, and `/goto`. Add a Crossroads info kiosk (lectern with written_book containing world guide). Extend the Paper Bridge Plugin's player join handler. | Oracle | L | — |
| S5-03 | Discord Pins → Building Library | Hybrid display for pinned Discord messages inside buildings. **Wall signs** (4-6 stacked on interior wall) show a headline/preview of the most recent pin (~240 chars across signs). **Lectern with written book** on the ground floor holds full pinned message history (up to 256 pages, ~50K chars). Bridge API endpoint `POST /api/buildings/{id}/pin` accepts pin data, enqueues an `UpdateBuilding` job. WorldGenJobProcessor handles the job: updates wall signs via RCON setblock with NBT text, updates lectern book via Paper plugin HTTP API (new endpoint `POST /plugin/lectern`). Max 6 preview signs + 1 lectern per building. | Lucius | M | S5-01 |
| S5-04 | Village Ambient Life | Villages feel empty. Add ambient details during village generation: villager NPCs (2-3 per village, profession matches village theme), cats and dogs near buildings, crop farms on village outskirts (wheat, carrots, potatoes — 8×8 plots), flower gardens between buildings, and ambient lighting (lanterns on fence posts along walkways). Extend `VillageGenerator` with an `AddAmbientDetails` phase after plaza generation. | Batgirl | M | — |
| S5-05 | E2E Test Scenarios | Carry-forward from #7. Write the actual end-to-end test scenarios using the S4-07 infrastructure: (1) Full guild sync → villages + buildings appear with correct styles, (2) Channel create → building generated + BlueMap marker set, (3) Channel delete → building archived + marker updated, (4) Track generation → hub-and-spoke verified, (5) `/goto` teleport flow. Target: 5 passing scenarios covering the core player-visible features. | Nightwing | L | — |
| S5-06 | BlueMap Full Setup | Carry-forward from #10. Complete BlueMap integration: configure BlueMap JAR in Paper MC plugins directory via Aspire volume mount, expose BlueMap web server port (8200) through Aspire, verify map renders after world generation, add `/map` village deep-links (click village name → BlueMap zooms to that village). Document BlueMap setup in README. | Oracle | M | — |
| S5-07 | World Activity Feed in Discord | Add a `#world-activity` Discord channel feed: post embeds when villages are built, buildings are constructed, tracks are laid, and players join/leave. Use Discord.NET's embed builder with color-coded borders (🟢 built, 🔴 archived, 🔵 player). WorldGen Worker publishes events to a `events:world:activity` Redis channel, Discord bot subscribes and posts. Rate-limit to max 1 embed per 5 seconds to avoid spam. | Lucius | M | — |
| S5-08 | Dynamic Building Sizing | Buildings currently have a fixed 21×21 footprint regardless of channel size. Scale building footprint based on member count in the Discord channel: <10 members → Small (15×15, 2 floors), 10-30 → Medium (21×21, 3 floors, current default), 30+ → Large (27×27, 4 floors). Update `BuildingGenerator` to accept a size parameter. Sync endpoint passes member count. Existing buildings are not resized — only new buildings use dynamic sizing. | Gordon | M | S5-01 |

**Sprint 5 Definition of Done:**
- Buildings have style-appropriate furnished interiors (not hollow shells)
- New players see a welcome screen and can find orientation info at Crossroads
- Pinned Discord messages appear as signs inside the corresponding building
- Villages have villagers, animals, farms, and ambient lighting
- 5+ E2E test scenarios pass against real Aspire containers
- BlueMap renders the world and is accessible via exposed port
- Discord `#world-activity` channel receives live build notifications
- Building size scales with Discord channel member count

---

## Risk Register

| Risk | Impact | Mitigation |
|------|--------|------------|
| RCON command limits for complex structures | Building generation too slow or hitting command rate limits | Bridge Plugin HTTP API handles complex structure placement server-side, reducing RCON chatter |
| Paper MC container startup time | Slow dev inner loop | Pre-generate world data, use Aspire `WaitFor` to sequence startup |
| Discord.NET rate limiting | Event flood during bulk channel changes | Debounce events in Discord Bot Service, batch Redis publishes |
| Java plugin development adds Java to the stack | Team is .NET-focused | Keep plugin minimal — thin HTTP API, defer logic to .NET services where possible |
| World coordinate collisions | Buildings overlap or tracks intersect | Central coordinate registry in PostgreSQL, WorldGen Worker validates placement before execution |

---

## Velocity Assumptions

- **Small (S):** ~2-4 hours of focused work
- **Medium (M):** ~1-2 days
- **Large (L):** ~3-4 days
- Sprint capacity per person: ~8 working days (accounting for meetings, reviews, context switching)
- Sprint 1 is heavier on Lucius (infrastructure), Sprint 2-3 distribute more evenly

---

## Beyond Sprint 5

Candidates for future sprints:
- Account linking (deferred from Sprint 3 — full `/link` flow with Redis TTL codes)
- Real-time player position sync (show in-game players on a Discord embed)
- Voice channel → proximity chat areas in Minecraft
- Web admin dashboard
- World backup automation
- Multiple Discord server support (Issue #11)
- Monitoring website + multi-tenant architecture (Issue #11)
- Building renovation on channel rename/topic change (re-generate signs, update markers)
- Seasonal world events (holiday decorations, special structures)
- Player stats & leaderboards (blocks placed, distance traveled)
