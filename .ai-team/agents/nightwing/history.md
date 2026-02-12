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
- CI workflow lives at `.github/workflows/ci.yml` — triggers on push to main and PRs, runs restore → build → test on ubuntu-latest with .NET 10 SDK
- Test projects go under `tests/` directory, organized in a `/tests/` solution folder in `DiscordMinecraft.slnx`
- `tests/Bridge.Api.Tests/` is the xUnit test project for Bridge.Api — uses xunit 2.9.3, xunit.runner.visualstudio 3.1.4, Microsoft.NET.Test.Sdk 17.14.1, coverlet.collector 6.0.4
- Solution file is `DiscordMinecraft.slnx` (XML-based `.slnx` format, not legacy `.sln`) — use `<Folder Name="/tests/">` structure for test projects
- Global `<Using Include="Xunit" />` in the test csproj eliminates need for `using Xunit;` in test files
- CI runs `dotnet restore/build/test` at repo root, which discovers the `.slnx` file automatically

📌 Team update (2026-02-11): Discord bot uses singleton DiscordSocketClient with BackgroundService pattern — decided by Oracle
📌 Team update (2026-02-11): Snake_case PostgreSQL table names with PascalCase C# entities — decided by Lucius
📌 Team update (2026-02-11): RCON password as Aspire secret parameter via builder.AddParameter("rcon-password", secret: true) — decided by Lucius
📌 Team update (2026-02-11): EF Core enum-to-string conversion for GenerationJobStatus — decided by Lucius
- Integration tests for Bridge.Api use WebApplicationFactory + Testcontainers Redis + SQLite in-memory — pattern established in `BridgeApiFactory.cs`
- Aspire's `AddNpgsqlDbContext` and `AddRedisClient` validate connection strings at registration time; tests must provide fake connection strings via `UseSetting()` before service registration runs
- To swap Npgsql for SQLite, must remove ALL EF Core + Npgsql service descriptors (matching by FullName) before re-registering — partial removal causes "multiple database providers" error
- SQLite in-memory with `Cache=Shared` requires a keep-alive `SqliteConnection` open for the entire fixture lifetime; without it, the shared DB is destroyed when the last connection closes
- `DefaultIfEmpty(-1).MaxAsync()` doesn't translate in SQLite provider — fixed to use `Select(c => (int?)c.BuildingIndex).MaxAsync()` with nullable cast, which works on both Npgsql and SQLite
- 20 integration tests covering: event consumer (pub/sub → DB + job queue), API endpoints (sync, villages, buildings, player link), and edge cases (duplicates, orphan channels, deletions, idempotent sync)

📌 Team update (2026-02-11): Sprint 2 interface contracts established — Redis event schema, job queue format, API endpoints, WorldGen interfaces, shared constants — decided by Gordon
📌 Team update (2026-02-11): Discord event DTO — unified DiscordChannelEvent record in Bridge.Data/Events/ — decided by Oracle
📌 Team update (2026-02-11): Bridge API endpoints + nullable coordinate columns + AddCoordinateColumns migration — decided by Lucius
📌 Team update (2026-02-11): Event consumer uses IsArchived on ChannelGroup, auto-creates groups on out-of-order events — decided by Lucius
📌 Team update (2026-02-12): Sprint work items are now GitHub Issues with milestones and squad-colored labels — decided by Jeff and Gordon

 Team update (2026-02-12): README.md created with project overview, architecture, getting started, and squad roster with shields.io badges  decided by Gordon
- Sprint 3 test specs live at `tests/Bridge.Api.Tests/Sprint3/Sprint3TestSpecs.md` — covers all 6 Sprint 3 features with test cases, edge cases, and coverage targets
- Sprint 3 channel deletion tests at `tests/Bridge.Api.Tests/Sprint3/ChannelDeletionTests.cs` — 14 concrete xUnit integration tests covering archival, idempotency, API behavior, building index continuity, and edge cases
- Sprint 3 E2E smoke tests at `tests/Bridge.Api.Tests/Sprint3/EndToEndSmokeTests.cs` — 6 active + 2 skipped (pending endpoint implementation) integration tests covering full sync, event pipeline, mixed operations
- Test files under `Sprint3/` subdirectory within the existing `Bridge.Api.Tests` project — no new csproj needed, existing `BridgeApiFactory` reused
- Channel deletion behavior: `IsArchived` flag set on Channel/ChannelGroup, record NOT removed from DB; sync endpoint does NOT clear `IsArchived` on upsert (archived channels stay archived)
- `BuildingX`/`BuildingZ` are null until WorldGen Worker processes the job — deletion of pre-generation channels is safe (archive flag set, null coords preserved)
- Current event consumer does NOT enqueue archive/UpdateBuilding jobs on channel deletion — it only sets `IsArchived`. Sprint 3 S3-05 implementation should add job enqueueing for sign updates and barrier placement

📌 Team update (2026-02-12): Minecart track layout — L-shaped paths at y=65, stations 30 blocks south of village center, angle-based platform slots — decided by Batgirl
📌 Team update (2026-02-12): Channel deletion now enqueues ArchiveBuilding/ArchiveVillage jobs to Redis worldgen queue — BuildingArchiver updates signs + blocks entrances — decided by Lucius
📌 Team update (2026-02-12): BlueMap integration added as S3-08 — drop-in Paper plugin, port 8100, Java API markers — decided by Gordon
📌 Team update (2026-02-12): Paper Bridge Plugin uses JDK HttpServer + Jedis + Bukkit scheduler, player events on events:minecraft:player — decided by Oracle
📌 Team update (2026-02-12): Port reassignment — decided by Lucius, requested by Jeff
📌 Team update (2026-02-12): Track routing triggered by village creation — WorldGenJobProcessor enqueues CreateTrack jobs after CreateVillage completes — decided by Batgirl
📌 Team update (2026-02-12): RCON config fixes — port mapping (targetPort: 25575, port: 25675) and URI parsing in RconService — decided by Lucius
📌 Team update (2026-02-12): MinecraftHealthCheck added — Aspire dashboard shows MC as unhealthy until RCON responds — decided by Lucius
📌 Team update (2026-02-12): Startup guild sync added to DiscordBotWorker — populates DB on bot ready — decided by Oracle
📌 Team update (2026-02-12): Sync endpoint now creates GenerationJob records and pushes to Redis queue — decided by Oracle
