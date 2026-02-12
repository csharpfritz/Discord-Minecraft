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
