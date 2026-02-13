# Project Context

- **Owner:** Jeffrey T. Fritz (csharpfritz@users.noreply.github.com)
- **Project:** Discord-to-Minecraft bridge — maps Discord channels to Minecraft villages/buildings with minecart navigation between channel groups. Creative/peaceful mode, .NET 10/Aspire 13.1/C#.
- **Stack:** .NET 10, Aspire 13.1, C#, Discord.NET, Minecraft protocol
- **Created:** 2026-02-11

## Learnings

<!-- Append new learnings below. Each entry is something lasting about the project. -->

### Sprint 1–3 Summary (2026-02-11 → 2026-02-12)

**S2-04 VillageGenerator:** 31×31 stone brick plaza, perimeter walls, fountain, glowstone lighting, oak signs. RconService wraps CoreRCON with semaphore serialization + rate limiting. Singletons in DI.

**S2-05 BuildingGenerator → Medieval Castle (redesigned 2026-02-12):** 21×21 footprint, 2-floor medieval castle keep — cobblestone walls, stone brick trim, oak log turrets, crenellated parapet, arrow slit windows, wall-mounted torches. 4x4 grid layout (27-block spacing, GridStartOffset=50). Exterior entrance sign at (bx, BaseY+5, maxZ+1).

**S3-03 TrackGenerator:** L-shaped rail paths at y=65, powered rails every 8 blocks + redstone blocks. 9×5 station platforms with shelter, lanterns, benches, flowers, improved signage. Atan2 angle-based slot assignment. Button-activated dispensers with 64 minecarts.

**S3-04 Track Routing:** WorldGenJobProcessor enqueues CreateTrack jobs after CreateVillage completes. Each new village connects to all existing non-archived villages.

**Village Amenities (2026-02-12):** Oak fence perimeter at radius 150. Cobblestone walkways (L-shaped, generated BEFORE foundation). Scalable fountain (3×3 default, 7×7 for 4+ buildings). WorldConstants corrected: BaseY=-60, BuildingFloors=2, FloorHeight=5.

**Key Minecraft Learnings:**
- Block placement order: foundation → walls → turrets → clear interior → floors → stairs → roof → windows → entrance → lighting → signs
- Superflat surface Y=-60; CoreRCON needs Dns.GetHostAddressesAsync for hostnames
- Sign NBT: front_text.messages array, rotation 0=N/4=E/8=S/12=W; wall_sign uses [facing=direction]
- Rails can't be diagonal; powered rails need redstone_block underneath
- Walkways before foundation; forceload must cover walkway extent
- SKILL.md at .ai-team/skills/minecraft-rcon-building/

📌 Team update (2026-02-13): Crossroads hub + spawn + teleport consolidated — central hub at origin (0,0), hub-and-spoke track topology, /goto command, world spawn at (0,-59,0) — decided by Jeff, Gordon
📌 Team update (2026-02-13): Train stations should be near village plaza, not far away — decided by Jeff
📌 Team update (2026-02-13): Sprint 4 plan — 8 work items: Crossroads hub, hub-and-spoke tracks, player teleport, building variety, station relocation, BlueMap markers, E2E tests, Crossroads integration. Account linking deferred again — decided by Gordon
📌 Team update (2026-02-13): Building design consolidated — medieval castle on 4x4 grid, 2-floor, entrance sign outside facing south — decided by Batgirl
📌 Team update (2026-02-13): Station platform design consolidated — near plaza per Jeff's directive, 9x5 shelter platforms, L-shaped tracks — decided by Batgirl, Jeff

📌 S4-04 complete (2026-02-13): Village station relocation to plaza edge — VillageGenerator now creates a 9×5 stone brick station area at the south edge of the plaza (PlazaRadius + 2 = 17 blocks south of village center), with a cobblestone walkway connecting the plaza opening to the station platform and a north-facing directional sign ("Station → Crossroads"). TrackGenerator's StationOffset changed from hardcoded 20 to WorldConstants.VillageStationOffset (17). WorldConstants.cs now exports VillageStationOffset and VillagePlazaInnerRadius for shared use.
📌 Learning (2026-02-13): Station placement should use shared constants from WorldConstants.cs — both VillageGenerator (station area) and TrackGenerator (station shelter/rails) must agree on the offset. Using a constant prevents misalignment.
📌 Learning (2026-02-13): Station area is built by VillageGenerator (foundation + walkway + sign) while shelter/rails are built by TrackGenerator — separation of concerns keeps village structure and track structure independent but spatially aligned.
📌 Team update (2026-02-13): Plugin HTTP port 8180 exposed via Aspire for marker wiring — if you add new generation job types, add marker calls in SetMarkersForJobAsync — decided by Oracle
📌 Team update (2026-02-13): /goto command uses Bridge API (/api/buildings/search + /api/buildings/{id}/spawn) for building lookup and teleport — decided by Oracle

📌 S4-02 complete (2026-02-13): Hub-and-spoke track topology implemented — replaced N×(N-1)/2 point-to-point routing with single track per village to Crossroads hub at (0,0). WorldGenJobProcessor.EnqueueTrackJobsForNewVillageAsync now creates exactly one CreateTrack job with DestCenterX/Z=0 and DestinationVillageName="Crossroads" (no DB query for existing villages). TrackGenerator detects Crossroads destination (DestCenterX==0 && DestCenterZ==0) and uses radial slot positioning via Atan2 angle-based slot selection from 16 evenly spaced slots at CrossroadsStationRadius=35. Village-side stations keep south-offset placement unchanged.
📌 Learning (2026-02-13): Hub-and-spoke topology reduces track count from O(n²) to O(n) — each village gets exactly one track to Crossroads, eliminating the foreach loop over existing villages and the DB query entirely.
📌 Learning (2026-02-13): Crossroads radial slot positioning uses Atan2(villageZ, villageX) to deterministically assign one of 16 slots — ensures each village's track arrives at a unique station slot around the Crossroads plaza perimeter.
📌 Team update (2026-02-13): Crossroads API and BlueMap URL configuration — Bridge.Api has BlueMap:WebUrl config key, /api/crossroads endpoint, /crossroads slash command — decided by Oracle

📌 RCON batch optimization complete (2026-02-14): Phase 2+3 fill consolidation across all generators — CrossroadsGenerator checkerboard 1,860→31 row fills, avenues 120→4 fills, tree trunks/flowers/benches/lanterns/banners/station slots all batched. TrackGenerator rail segments use fill for regular rail runs between powered intervals, station platform rails/decorations batched. BuildingGenerator castle turrets/cottage posts use vertical fills, crenellation uses fill+batch-clear, all stairs use fill per step row, all lighting batched. VillageGenerator lighting/fence corners batched. Total commands reduced from ~7,100 to ~2,600.
📌 Learning (2026-02-14): Checkerboard pattern via individual setblock is prohibitively expensive — use alternating-row fills (striped pattern) for decorative plaza floors. 31 fills vs 1,860 setblocks for a 61×61 plaza.
📌 Learning (2026-02-14): SendBatchAsync eliminates N-1 inter-command delays — collect setblock positions into a List, then call SendSetBlockBatchAsync once. Same pattern for fills via SendFillBatchAsync. Critical for decoration loops (lighting, flowers, banners).
📌 Learning (2026-02-14): Vertical fill replaces per-Y setblock loops for pillars/posts/columns — `fill bx y1 bz bx y2 bz block` is 1 command instead of (y2-y1+1) commands. Used for castle turrets (4 fills vs 44 setblocks), cottage posts (8 fills vs 80 setblocks), tree trunks.
📌 Learning (2026-02-14): Crenellation is more efficient as fill-then-clear than place-every-other — fill 4 full parapet edges with stone bricks (4 fills), then batch-clear alternating positions with air (1 batch). Saves ~32 commands per building vs individual merlon placement.
📌 Learning (2026-02-14): Rail segment fill optimization — fill contiguous regular rail runs between powered rail intervals, then batch the powered rail + redstone block pairs. For a 500-block track: ~70 fills + 1 batch vs ~562 individual setblocks.

 Team update (2026-02-13): RconService batch API (SendBatchAsync, adaptive delay 50ms->10ms) now available for generator adoption  decided by Lucius

📌 S5-01 complete (2026-02-14): Building interior furnishing — each architectural style now has fully furnished interiors placed AFTER GenerateFloorsAsync:
- **Medieval Castle:** Ground floor = throne room (throne chair on polished andesite platform, red carpet runner, flanking banners, banquet table). 2nd floor = armory (anvil, grindstone, smithing table, weapon display chains, storage chests).
- **Timber Cottage:** Ground floor = hearth + kitchen (campfire with chain, smoker, crafting table, furnace, barrel, dining table with chairs, cauldron). 2nd floor = study + bookshelves (bookshelf walls, writing desk, lectern, flower pot, beds).
- **Stone Watchtower:** Ground floor = map/planning room (5×5 central planning table, cartography table, lectern, chairs, barrels, chiseled bookshelves). 2nd floor = brewing + supplies (brewing stands, water cauldron, supply chests, bookshelves, crafting table, soul campfire).
- Channel topic from Discord displayed as wall sign on ground floor interior (when set). Topic data flows: Channel.Topic → DiscordChannelEvent.Topic → BuildingJobPayload.ChannelTopic → BuildingGenerationRequest.ChannelTopic → BuildingGenerator.
📌 Learning (2026-02-14): Interior furnishing must happen AFTER all structural generation — placed last in the style method call chain (after signs). This prevents ClearInteriorAsync from wiping furniture and ensures all wall blocks exist for wall-mounted items.
📌 Learning (2026-02-14): Channel topic pipeline requires changes across 5 layers (entity → event → payload → request → generator). Using optional parameters with defaults avoids breaking existing deserialization of jobs already in Redis queue.
📌 S5-04 complete (2026-02-14): Village ambient life — AddAmbientDetailsAsync phase after fence generation:
- 3 villager NPCs (librarian, farmer, armorer) with PersistenceRequired:1b to prevent despawning
- 4 pets (2 cats, 2 dogs) near building rows
- 4 crop farms (8×8 each) at village outskirts: wheat, carrots, potatoes, beetroot with water channels and fence borders
- 4 flower gardens (3×3 mixed flowers) between plaza and building rows
- Lanterns on fence posts every 6 blocks along perimeter walkway
📌 Learning (2026-02-14): Minecraft mob summon commands need PersistenceRequired:1b NBT tag or villagers/animals will despawn when no players are nearby. Without it, villages become empty again after players leave.

📌 Change (2026-02-13): Removed generic villager NPCs (librarian, farmer, armorer) from VillageGenerator.SummonVillagersAsync — method deleted, call removed from AddAmbientDetailsAsync. Per Jeff's directive: Discord bots connected to the server will be represented as entities in a future iteration. Cats, dogs, farms, gardens, and lanterns remain as ambient life.

 Team update (2026-02-13): Player welcome pressure plate at (0, -59, 8) and lectern at (8, -59, 0)  avoid building anything at these Crossroads coords  decided by Oracle
