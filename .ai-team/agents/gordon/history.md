# Project Context

- **Owner:** Jeffrey T. Fritz (csharpfritz@users.noreply.github.com)
- **Project:** Discord-to-Minecraft bridge — maps Discord channels to Minecraft villages/buildings with minecart navigation between channel groups. Creative/peaceful mode, .NET 10/Aspire 13.1/C#.
- **Stack:** .NET 10, Aspire 13.1, C#, Discord.NET, Minecraft protocol
- **Created:** 2026-02-11

## Learnings

<!-- Append new learnings below. Each entry is something lasting about the project. -->

- Architecture uses 3 .NET services: `DiscordBot.Service` (Worker + Discord.NET), `Bridge.Api` (ASP.NET Minimal API), `WorldGen.Worker` (BackgroundService). Plus a `Bridge.Data` shared class library for EF Core.
- Paper MC runs as a Docker container via Aspire's `AddContainer` with the `itzg/minecraft-server` image. RCON on port 25575, MC on 25565.
- Hybrid Minecraft control: CoreRCON for simple commands (`/fill`, `/setblock`), custom Paper plugin with HTTP API for complex operations (structure placement, rail systems).
- Redis serves dual purpose: pub/sub event bus for Discord→Minecraft events, and list-based job queue for world generation tasks.
- PostgreSQL stores all persistent state: channel→village mappings, player identity links, world coordinates, generation job audit trail.
- World is superflat. Villages on 500-block grid starting at origin. Buildings are 21×21 footprint, 4 floors, ring placement around village plaza.
- Account linking uses one-time 6-character codes with 5-minute TTL in Redis. No OAuth needed.
- Channel deletion archives buildings (signs updated, entrance blocked) rather than destroying them.
- Minecart tracks use powered rails every 8 blocks, star topology between villages.
- `docs/architecture.md` — full system architecture document
- `docs/sprints.md` — first 3 sprint plans with work item assignments
- Jeff prefers .NET 10, Aspire 13.1, C# — no negotiation on stack choices.
- Team roles: Oracle handles Discord+Minecraft protocol, Lucius handles .NET/Aspire/DB, Batgirl handles world gen algorithms, Nightwing handles tests.

📌 Team update (2026-02-11): Discord bot uses singleton DiscordSocketClient with BackgroundService pattern — decided by Oracle
📌 Team update (2026-02-11): Test projects under tests/{ProjectName}.Tests/, CI at .github/workflows/ci.yml with .NET 10 — decided by Nightwing
📌 Team update (2026-02-11): Snake_case PostgreSQL table names with PascalCase C# entities — decided by Lucius
📌 Team update (2026-02-11): RCON password as Aspire secret parameter via builder.AddParameter("rcon-password", secret: true) — decided by Lucius
📌 Team update (2026-02-11): EF Core enum-to-string conversion for GenerationJobStatus — decided by Lucius
📌 Team update (2026-02-11): Discord event DTO — unified DiscordChannelEvent record in Bridge.Data/Events/ — decided by Oracle
📌 Team update (2026-02-11): Bridge API endpoints + nullable coordinate schema — decided by Lucius
📌 Team update (2026-02-11): Event consumer architecture — BackgroundService + job envelope + upsert pattern — decided by Lucius
📌 Team update (2026-02-11): Village generation — singleton RconService with semaphore + rate limiting — decided by Batgirl
📌 Team update (2026-02-11): Building generation — 21×21, 4-floor, ring placement, wall signs — decided by Batgirl
📌 Team update (2026-02-11): Job processor — polls queue:worldgen, 3 retries with exponential backoff — decided by Lucius
📌 Team update (2026-02-11): Integration test infra — WebApplicationFactory + Testcontainers Redis + SQLite, nullable Max() fix — decided by Nightwing
- Sprint 3 GitHub milestone number: **1** ("Sprint 3: Integration & Navigation")
- Sprint 3 issue numbers: #1 (S3-01), #2 (S3-02), #3 (S3-03), #4 (S3-04), #5 (S3-05), #6 (S3-06), #7 (S3-07)
- Label naming convention: `squad:{name}` — one label per squad member, used to tag issues by assignee
- Label color assignments: gordon=#0052CC (blue), oracle=#7B61FF (purple), lucius=#0E8A16 (green), batgirl=#D93F0B (orange-red), nightwing=#006B75 (teal)
📌 Team update (2026-02-12): Sprint work items are now GitHub Issues with milestones and squad-colored labels — decided by Jeff and Gordon
- README.md created at repo root with project description, architecture overview, getting started, squad roster, and project status
- Squad badges use shields.io flat-square format: `https://img.shields.io/badge/{emoji}_{name}-{role}-{hex}?style=flat-square&labelColor={hex}&color={hex}`

 Team update (2026-02-12): README.md created with project overview, architecture, getting started, and squad roster with shields.io badges  decided by Gordon
- BlueMap integration added as S3-08 (Issue #10) in Sprint 3 milestone. BlueMap JAR in plugins dir, web server on port 8100 via Aspire port mapping, markers via BlueMap Java API from Bridge Plugin, `/map` Discord command. Assigned to Oracle (squad:oracle).
📌 Team update (2026-02-12): BlueMap architecture — drop-in Paper plugin, Aspire port mapping, Java API markers, deterministic `/map` URL — decided by Gordon

📌 Team update (2026-02-12): Minecart track layout — L-shaped paths at y=65, stations 30 blocks south of village center, angle-based platform slots — decided by Batgirl
📌 Team update (2026-02-12): Channel deletion now enqueues ArchiveBuilding/ArchiveVillage jobs to Redis worldgen queue — decided by Lucius
📌 Team update (2026-02-12): Sprint 3 test specs written for all features — decided by Nightwing
📌 Team update (2026-02-12): Paper Bridge Plugin uses JDK HttpServer + Jedis + Bukkit scheduler, player events on events:minecraft:player — decided by Oracle
📌 Team update (2026-02-12): Port reassignment — decided by Lucius, requested by Jeff
📌 Team update (2026-02-12): Track routing triggered by village creation — WorldGenJobProcessor enqueues CreateTrack jobs after CreateVillage completes — decided by Batgirl
📌 Team update (2026-02-12): RCON config fixes — port mapping (targetPort: 25575, port: 25675) and URI parsing in RconService — decided by Lucius
📌 Team update (2026-02-12): MinecraftHealthCheck added — Aspire dashboard shows MC as unhealthy until RCON responds — decided by Lucius
📌 Team update (2026-02-12): /status and /navigate slash commands added with Bridge API endpoints — decided by Oracle
📌 Team update (2026-02-12): Startup guild sync added to DiscordBotWorker — populates DB on bot ready — decided by Oracle
📌 Team update (2026-02-12): Sync endpoint now creates GenerationJob records and pushes to Redis queue — decided by Oracle

📌 Team update (2026-02-13): Village amenities — walkways, scalable fountains, interior sign fix — decided by Batgirl
📌 Team update (2026-02-13): Crossroads hub + spawn + teleport consolidated — central hub at origin (0,0), hub-and-spoke track topology, /goto command, world spawn at (0,-59,0) — decided by Jeff, Gordon
📌 Team update (2026-02-13): Train stations should be near village plaza, not far away — decided by Jeff
