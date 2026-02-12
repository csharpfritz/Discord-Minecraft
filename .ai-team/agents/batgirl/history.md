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

 Team update (2026-02-12): README.md created with project overview, architecture, getting started, and squad roster with shields.io badges  decided by Gordon
📌 Team update (2026-02-12): README.md created with project overview, architecture, getting started, and squad roster with shields.io badges — decided by Gordon

📌 Team update (2026-02-12): Only publicly accessible Discord channels are mapped to Minecraft village buildings — private/restricted channels excluded — decided by Jeffrey T. Fritz

📌 S3-03 complete (2026-02-12): Minecart track generation implemented — TrackGenerator creates L-shaped rail paths between villages at y=65 (elevated trackbed on stone bricks at y=64). Powered rails every 8 blocks with redstone blocks underneath for permanent activation. Station platforms (7×3) placed 30 blocks south of village center with angle-based slot assignment for multiple destinations. Each platform has departure/arrival oak_wall_signs, button-activated dispenser with 64 minecarts, glowstone corner lighting, stone brick slab walkways. CreateTrack job type added to WorldGenJobType enum with TrackJobPayload DTO. Registered as singleton ITrackGenerator in DI.
📌 Learning (2026-02-12): Minecraft powered rails need a redstone signal to stay powered — placing a redstone_block under each powered rail provides permanent activation without external circuits
📌 Learning (2026-02-12): Rails cannot be placed diagonally in Minecraft — use L-shaped paths (axis-aligned segments) for track routing between villages
📌 Learning (2026-02-12): Station platform slot assignment uses Atan2 angle-based hashing to deterministically assign platforms per destination direction — prevents overlapping when multiple tracks terminate at the same village
📌 Learning (2026-02-12): Dispensers facing=up spawn minecarts on top when activated by button — use /data merge to pre-load minecart items into dispenser inventory
📌 Learning (2026-02-12): Shared git working directory with concurrent agents causes stash/checkout conflicts — use GitHub push_files API for atomic commits when environment is shared

📌 Team update (2026-02-12): Channel deletion now enqueues ArchiveBuilding/ArchiveVillage jobs to Redis worldgen queue (not just DB flag) — BuildingArchiver updates signs + blocks entrances — decided by Lucius
📌 Team update (2026-02-12): Sprint 3 test specs written for all features including 14 channel deletion + 8 E2E smoke tests — decided by Nightwing
📌 Team update (2026-02-12): BlueMap integration added as S3-08 — drop-in Paper plugin, port 8100, Java API markers, /map Discord command — decided by Gordon
📌 Team update (2026-02-12): Paper Bridge Plugin uses JDK HttpServer + Jedis + Bukkit scheduler, player events on events:minecraft:player — decided by Oracle
📌 Team update (2026-02-12): Port reassignment — decided by Lucius, requested by Jeff
📌 S3-04 complete (2026-02-12): Track routing on village creation — WorldGenJobProcessor now enqueues CreateTrack jobs for every existing non-archived village after a CreateVillage job completes successfully. First village is handled gracefully (no tracks needed, logged informatively). Track jobs are queued AFTER village generation completes, ensuring the village is fully built before track generation begins. Station signs at existing villages are updated automatically because each CreateTrack job generates new station platforms with destination signs at both ends via the existing TrackGenerator.
📌 Learning (2026-02-12): Track routing is a follow-up concern of the job processor, not the event consumer — enqueue CreateTrack jobs after CreateVillage succeeds to guarantee correct ordering
📌 Learning (2026-02-12): Existing station sign updates happen naturally through new platform creation — each TrackGenerator.GenerateAsync call builds platforms with signs at both the source and destination village, so connecting a new village automatically adds signs at existing ones
📌 Team update (2026-02-12): RCON config fixes — port mapping (targetPort: 25575, port: 25675) and URI parsing in RconService — decided by Lucius
📌 Team update (2026-02-12): MinecraftHealthCheck added — Aspire dashboard shows MC as unhealthy until RCON responds — decided by Lucius
📌 Team update (2026-02-12): /status and /navigate slash commands added with Bridge API endpoints — decided by Oracle
📌 Team update (2026-02-12): Startup guild sync added to DiscordBotWorker — populates DB on bot ready — decided by Oracle
📌 Team update (2026-02-12): Sync endpoint now creates GenerationJob records and pushes to Redis queue — decided by Oracle

📌 Medieval castle redesign (2026-02-12): BuildingGenerator redesigned from plain stone brick box to medieval castle keep — cobblestone walls with stone brick trim, oak log corner turrets, crenellated parapet, arrow slit windows, arched entrance, wall-mounted torches. Reduced from 4 floors to 2 for better proportions.
📌 Learning (2026-02-12): Block placement order is CRITICAL — must be: foundation → walls → turrets → clear interior → floors → stairs → roof → windows → entrance → lighting → signs. Interior clear wipes anything placed before it (floors, lighting). Signs must be placed LAST on solid wall blocks.
📌 Learning (2026-02-12): Wall-mounted torches (`wall_torch[facing=direction]`) solve the floating glowstone problem — they attach to solid interior walls and are placed after interior clear, so they never get erased.
📌 Learning (2026-02-12): Superflat world surface is Y=-60 (bedrock -64, dirt -63 to -61, grass -60). All generators use BaseY = -60. BuildingArchiver must match BuildingGenerator's floor count and entrance dimensions.
📌 RCON building skill created (2026-02-12): Comprehensive SKILL.md at .ai-team/skills/minecraft-rcon-building/ covering superflat coordinates, CoreRCON IPv4 patterns, block placement order, Aspire/Docker networking, and medieval castle patterns.
