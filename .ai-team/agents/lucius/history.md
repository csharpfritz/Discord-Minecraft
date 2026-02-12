# Project Context

- **Owner:** Jeffrey T. Fritz (csharpfritz@users.noreply.github.com)
- **Project:** Discord-to-Minecraft bridge — maps Discord channels to Minecraft villages/buildings with minecart navigation between channel groups. Creative/peaceful mode, .NET 10/Aspire 13.1/C#.
- **Stack:** .NET 10, Aspire 13.1, C#, Discord.NET, Minecraft protocol
- **Created:** 2026-02-11

## Core Context (summarized from Sprint 1–2 sessions)

**Sprint 1 Foundation (S1-01–S1-04):** Scaffolded Aspire 13.1 AppHost with 6 projects under `src/`. Bridge.Data holds EF Core DbContext, entities (`Entities/`), and migrations. PostgreSQL uses snake_case tables, PascalCase C# properties, string-stored enums. Secrets via `builder.AddParameter(..., secret: true)` for RCON password and Discord bot token. Connection resources: `"bridgedb"`, `"redis"`, `"postgres"`. MC container uses `scheme: "tcp"` for non-HTTP ports. Discord IDs stored as strings. EF Core Relational pinned to 10.0.3.

**Sprint 2 — S2-02 (Bridge API):** Four Minimal API endpoints (`/api/mappings/sync`, `/api/villages`, `/api/villages/{id}/buildings`, `/api/players/link`). Nullable coordinate columns (VillageX/Z on ChannelGroup, BuildingX/Z on Channel) distinguish planned vs generated. VillageIndex for ordinal grid placement. UNIQUE constraint on (CenterX, CenterZ). WorldConstants in Bridge.Data.

**Sprint 2 — S2-03 (Event Consumer):** `DiscordEventConsumer` BackgroundService subscribes to `events:discord:channel`, dispatches by EventType. `WorldGenJob` envelope pattern on `queue:worldgen` Redis list. IsArchived on ChannelGroup. Upsert for out-of-order events. `IServiceScopeFactory` for per-event DbContext. BuildingIndex = MAX+1.

**Sprint 2 — S2-06 (Job Processor):** `WorldGenJobProcessor` BackgroundService polls Redis via `ListRightPopAsync` (500ms idle). 3 retries with exponential backoff. Payload mapping between Bridge.Data DTOs and WorldGen.Worker models. Generators/RconService are singletons.

**Discord Bot Token:** Aspire secret parameter → env var `Discord__BotToken` → config key `Discord:BotToken`.

## Learnings

### Sprint 3 — S3-05: Channel Deletion Handling

- Channel/category deletion now enqueues `ArchiveBuilding`/`ArchiveVillage` jobs to the Redis `queue:worldgen` in addition to setting `IsArchived=true` in PostgreSQL
- `BuildingArchiver` in WorldGen.Worker handles in-world archival: updates all signs (entrance + floor) with red `[Archived]` prefix, blocks 3×3 entrance with `minecraft:barrier` blocks
- `ArchiveVillage` job iterates all buildings in the group and archives each one via the same `BuildingArchiver` — no separate village-level RCON logic needed
- Building coordinate recalculation uses the same ring formula as `BuildingGenerator`: `60 * cos/sin(index * 22.5°)` from village center
- Sign text truncated to 10 chars (vs 15 in original) to leave room for `[Archived]` prefix on the first line
- Job payload DTOs: `ArchiveBuildingJobPayload` and `ArchiveVillageJobPayload` in `Bridge.Data/Jobs/`
- `IBuildingArchiver` interface + `BuildingArchiver` implementation in `WorldGen.Worker/Generators/`
- `BuildingArchiveRequest` model in `WorldGen.Worker/Models/`
- `DiscordEventConsumer.HandleChannelDeletedAsync` now includes `ChannelGroup` via `.Include()` to get village center coords for the archive job
- `DiscordEventConsumer.HandleChannelGroupDeletedAsync` now builds a list of `ArchiveBuildingJobPayload` for all channels and wraps them in an `ArchiveVillageJobPayload`

📌 Team update (2026-02-12): Minecart track layout — L-shaped paths at y=65, stations 30 blocks south of village center, angle-based platform slots — decided by Batgirl
📌 Team update (2026-02-12): BlueMap integration added as S3-08 — drop-in Paper plugin, port 8100 via Aspire, Java API markers — decided by Gordon
📌 Team update (2026-02-12): Sprint 3 test specs written — 14 channel deletion + 8 E2E smoke tests, reusing BridgeApiFactory — decided by Nightwing
📌 Team update (2026-02-12): Paper Bridge Plugin uses JDK HttpServer + Jedis + Bukkit scheduler, player events on events:minecraft:player — decided by Oracle
📌 Team update (2026-02-12): Port reassignment — decided by Lucius, requested by Jeff
