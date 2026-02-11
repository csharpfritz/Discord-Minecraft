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

### Sprint 1 Foundation (S1-01 through S1-04)

- Aspire 13.1 `aspire-apphost` template generates `AppHost.cs` (not `Program.cs`) as the entry point
- Solution structure: all projects under `src/` — `AppHost`, `ServiceDefaults`, `DiscordBot.Service`, `Bridge.Api`, `WorldGen.Worker`, `Bridge.Data`
- `Bridge.Data` is the shared EF Core class library — contains `BridgeDbContext`, entity models in `Entities/`, and migrations in `Migrations/`
- RCON password is managed via `builder.AddParameter("rcon-password", secret: true)` — stored in AppHost user secrets, not hardcoded
- Minecraft container endpoints use `scheme: "tcp"` for non-HTTP ports (25565 MC, 25575 RCON)
- `Aspire.Npgsql.EntityFrameworkCore.PostgreSQL` NuGet depends on `Npgsql.EntityFrameworkCore.PostgreSQL` which pins `EF Core Relational` to a specific patch version — Bridge.Data explicitly references `Microsoft.EntityFrameworkCore.Relational` at 10.0.3 to keep versions aligned
- Connection resource names: `"bridgedb"` (PostgreSQL database), `"redis"` (Redis), `"postgres"` (PostgreSQL server)
- PostgreSQL schema uses snake_case table names (`channel_groups`, `channels`, `players`, `world_state`, `generation_jobs`) with PascalCase C# properties
- `GenerationJobStatus` enum is stored as a string in the database (via `HasConversion<string>()`) for readability
- All Discord IDs stored as `string` (max 64 chars) — Discord snowflakes are uint64 but string avoids overflow and works across all DB providers
- Key file paths:
  - `src/AppHost/AppHost.cs` — Aspire orchestrator entry point
  - `src/ServiceDefaults/Extensions.cs` — shared Aspire service defaults (OTel, health checks, service discovery)
  - `src/Bridge.Data/BridgeDbContext.cs` — EF Core DbContext with Fluent API configuration
  - `src/Bridge.Data/Entities/` — ChannelGroup, Channel, Player, WorldState, GenerationJob
  - `src/Bridge.Api/Program.cs` — wired with AddNpgsqlDbContext, AddRedisClient, AddServiceDefaults
  - `src/WorldGen.Worker/Program.cs` — wired with AddNpgsqlDbContext, AddRedisClient, AddServiceDefaults
  - `src/DiscordBot.Service/Program.cs` — wired with AddRedisClient, AddServiceDefaults (Discord.NET already scaffolded by Oracle)

📌 Team update (2026-02-11): Discord bot uses singleton DiscordSocketClient with BackgroundService pattern — decided by Oracle
📌 Team update (2026-02-11): Test projects under tests/{ProjectName}.Tests/, CI at .github/workflows/ci.yml with .NET 10 — decided by Nightwing
