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

## Beyond Sprint 3

Candidates for Sprint 4+:
- Real-time player position sync (show in-game players on a Discord embed)
- Voice channel → proximity chat areas in Minecraft
- Web admin dashboard
- World backup automation
- Multiple Discord server support
- Building interior customization based on channel topic/description
- Dynamic building sizing based on channel member count
