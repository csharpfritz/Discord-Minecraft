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

📌 Team update (2026-02-11): Test projects under tests/{ProjectName}.Tests/, CI at .github/workflows/ci.yml with .NET 10 — decided by Nightwing
📌 Team update (2026-02-11): Snake_case PostgreSQL table names with PascalCase C# entities — decided by Lucius
📌 Team update (2026-02-11): EF Core enum-to-string conversion for GenerationJobStatus — decided by Lucius

📌 S2-04 complete (2026-02-11): Village generation algorithm implemented — VillageGenerator builds a 31×31 stone brick plaza with perimeter walls (3-wide cardinal openings), central fountain, glowstone lighting every 4 blocks along paths, oak signs with village name at center facing all 4 directions, and welcome paths extending 10 blocks outward from each opening. RconService wraps CoreRCON with rate limiting (configurable delay), connection retry on failure, and semaphore-based serialization. RCON config wired via Aspire secret parameters (Rcon__Host, Rcon__Port, Rcon__Password). All registered as singletons in DI. Uses /fill for large areas and /setblock for individual blocks.
📌 Learning (2026-02-11): CoreRCON RCON class needs Dns.GetHostAddressesAsync to resolve hostnames before connecting — constructor takes IPAddress, not string hostname
📌 Learning (2026-02-11): Minecraft sign NBT format uses front_text.messages array with JSON text components — rotation values: 0=north, 4=east, 8=south, 12=west
📌 Learning (2026-02-11): RCON commands need rate limiting (50ms default delay) to avoid overwhelming Paper MC — SemaphoreSlim(1,1) ensures serialized command execution

📌 S2-05 complete (2026-02-11): Building generation algorithm implemented — BuildingGenerator creates 21×21 footprint, 4-floor stone brick buildings via RCON. Each floor is 5 blocks tall (y=65-84). Features: oak plank intermediate floors (y=69,74,79), stone brick slab roof (y=85), 3-wide entrance on south face, oak switchback stairs in NE corner, 2-wide glass pane windows centered on each wall per floor, glowstone ceiling grid every 5 blocks, colored carpet border per floor (red/blue/green/yellow), oak wall signs at entrance and inside each floor with channel name. Buildings placed in ring layout (radius=60) around village center using buildingIndex angle formula.
📌 Learning (2026-02-11): Minecraft oak_wall_sign uses [facing=direction] blockstate (not rotation like standing signs) — facing=south means the sign face points south, must be placed on a solid block
📌 Learning (2026-02-11): Building generation order matters — walls first, then clear interior (air fill), then floors, then details. This prevents /fill commands from overwriting previously placed blocks
📌 Learning (2026-02-11): For multi-floor buildings, clear the entire interior as one big air fill, then place individual floor slabs — more efficient than clearing floor-by-floor

📌 Team update (2026-02-11): Sprint 2 interface contracts established — Redis event schema, job queue format, API endpoints, WorldGen interfaces, shared constants — decided by Gordon
📌 Team update (2026-02-11): Bridge API nullable coordinate columns (VillageX/Z on ChannelGroup, BuildingX/Z on Channel) — set by WorldGen Worker after generation — decided by Lucius
📌 Team update (2026-02-11): Event consumer archives groups+channels on deletion, auto-creates groups on out-of-order events — decided by Lucius
📌 Team update (2026-02-11): Job processor maps VillageJobPayload.VillageName→Name, BuildingJobPayload.ChannelName→Name, CenterX/Z→VillageCenterX/Z — decided by Lucius
📌 Team update (2026-02-12): Sprint work items are now GitHub Issues with milestones and squad-colored labels — decided by Jeff and Gordon
