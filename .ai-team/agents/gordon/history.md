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
