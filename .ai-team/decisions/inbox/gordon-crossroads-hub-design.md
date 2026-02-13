# Design Decision: Crossroads of the World Hub

**By:** Gordon (Lead/Architect)  
**Requested by:** Jeff  
**Date:** 2026-02-12

## Summary

Design a central "Crossroads of the World" hub that serves as the spawn point and central train station connecting all villages via hub-and-spoke topology.

---

## 1. Location Decision

**Location: World Origin (0, 0)**

The Crossroads will be placed at world coordinates `(0, 0)` — the natural center point.

**Rationale:**
- Villages are currently generated on a 175-block grid. Placing Crossroads at origin means it sits at the grid's conceptual center.
- Minecraft's default world spawn is near (0, 0), making this the expected landing point for new players.
- All distance calculations and track routing simplify when the hub is at the origin.
- The first generated "guild/server" villages will spawn at grid positions radiating outward from origin.

**Impact on existing village grid:**
- Village index 0 currently maps to `(0, 0)` — this must change.
- New village placement will skip the origin cell or use an offset grid starting at cell (1, 0) or similar.
- Option A: Reserve grid cell (0, 0) for Crossroads; villages start at index 0 → grid position (1, 0).
- **Selected: Option A** — cleanest separation between hub and village grid.

---

## 2. Track Topology Change: Point-to-Point → Hub-and-Spoke

### Current Model (Point-to-Point)
- Each new village connects to ALL existing villages via L-shaped tracks.
- N villages = N×(N-1)/2 total tracks.
- Complexity explodes: 10 villages = 45 tracks; 50 villages = 1,225 tracks.

### New Model (Hub-and-Spoke)
- Each village connects ONLY to the Crossroads hub.
- N villages = N tracks (one per village).
- Players take two rides max: Village A → Crossroads → Village B.
- Scalable, simple, consistent travel time.

### TrackGenerator Changes

**Before:** `CreateTrack` job connects Village A ↔ Village B directly.

**After:** `CreateTrack` job connects Village ↔ Crossroads only.

| Component | Change |
|-----------|--------|
| `TrackGenerationRequest` | Replace `DestCenterX/Z` + `DestinationVillageName` with fixed hub coordinates. Add `VillageDirection` (angle from hub) for platform slot assignment. |
| `WorldGenJobProcessor` | After `CreateVillage` completes, enqueue ONE `CreateTrack` job: new village → Crossroads. Remove the loop connecting to all other villages. |
| `TrackGenerator.GenerateAsync` | Station at village end unchanged. Station at Crossroads end uses new `CrossroadsPlatformGenerator` (or inline logic) to place platform in correct radial slot. |

### Platform Slot Assignment at Crossroads

The Crossroads hub arranges platforms radially around the central plaza:
- Calculate angle from hub center to village: `atan2(villageZ, villageX)`
- Map angle to one of 16 cardinal/intercardinal slots (N, NNE, NE, ENE, E, ESE, SE, SSE, S, SSW, SW, WSW, W, WNW, NW, NNW)
- Each slot is a platform corridor extending outward from the hub perimeter

This ensures:
- Platforms don't overlap regardless of village count
- Directional intuition: "Take the Northeast platform to reach the guild in that direction"
- Future villages in similar directions use adjacent slots

---

## 3. CrossroadsGenerator — New Component

### File: `src/WorldGen.Worker/Generators/CrossroadsGenerator.cs`

### Responsibilities
1. Generate the ornate central plaza (larger than village plazas)
2. Set world spawn point to Crossroads center
3. Build the initial hub structure (platforms added incrementally by TrackGenerator)

### Crossroads Layout Concept

```
                        N
                        |
           +-----------[N Platform]------------+
           |                                   |
           |     +-------------------+         |
    [W     |     |   GRAND PLAZA    |         |    [E
  Platform]|     |    (61 × 61)     |         |  Platform]
           |     |   w/ Fountain    |         |
           |     +-------------------+         |
           |                                   |
           +-----------[S Platform]------------+
                        |
                        S

    (Conceptual — actual layout is circular/radial)
```

### Dimensions

| Element | Size | Notes |
|---------|------|-------|
| Grand Plaza | 61×61 blocks (radius 30) | ~4× village plaza area |
| Central Fountain | 15×15 blocks | Large multi-tier decorative fountain |
| Tree-Lined Avenues | 4 cardinal paths, 7 blocks wide | Oak trees every 10 blocks |
| Perimeter Walkway | 5 blocks wide | Polished stone with benches |
| Platform Corridors | 16 slots, 7 blocks wide each | Radiate outward from perimeter |
| Total Footprint | ~200×200 blocks | Including platform extensions |

### Decorative Elements

| Element | Implementation |
|---------|----------------|
| **Grand Fountain** | 3-tier stone brick basin with water cascading down, glowstone underwater lighting, sea lanterns at corners, central quartz pillar with beacon light |
| **Tree-Lined Streets** | Oak leaves canopy on oak log trunks, placed along the 4 main avenues at 10-block intervals |
| **Lampposts** | Stone brick post (3 high) + lantern at top, every 8 blocks along pathways |
| **Flower Beds** | 3×3 planters with mixed flowers (poppies, dandelions, blue orchids) between lampposts |
| **Benches** | Oak stairs facing inward along perimeter walkway |
| **Decorative Banners** | Colored banners on fence posts at cardinal entry points |
| **Welcome Signs** | Large oak signs reading "CROSSROADS OF THE WORLD" facing each cardinal direction |
| **Directional Signs** | Sign posts at each platform corridor indicating village names/directions |

### Y-Level Coordination

| Layer | Y-Level | Content |
|-------|---------|---------|
| Foundation | -60 | Polished andesite base |
| Surface | -59 | Stone bricks (plaza), grass paths (avenues) |
| Track Level | -59 | Powered rails in platform corridors |
| Structures | -58 to -55 | Lampposts, signs, shelter roofs |
| Trees | -59 to -50 | Oak trees (9 blocks tall) |

---

## 4. Spawn Point Management

### Implementation
After CrossroadsGenerator completes the plaza, execute RCON command:
```
/setworldspawn 0 -59 0
```

### Spawn Behavior
- New players spawn at (0, -59, 0) — center of Crossroads plaza
- Respawning players return to Crossroads (unless they have a bed)
- Provides consistent, beautiful first impression

### Player Welcome

On first join or respawn, the player stands in the grand plaza with:
- 4 visible tree-lined avenues extending to cardinal directions
- Signs pointing toward platform corridors for each village
- Central fountain as visual landmark

---

## 5. Generation Order

### Sequence

```
1. [First-time world setup only]
   └─→ CrossroadsGenerator.GenerateAsync()
       ├─ Build grand plaza
       ├─ Build central fountain
       ├─ Build tree-lined avenues
       ├─ Place lampposts, benches, flower beds
       ├─ Place welcome signs
       └─ /setworldspawn 0 -59 0

2. [For each Discord guild sync / channel event]
   └─→ VillageGenerator.GenerateAsync() at grid position (skipping origin)

3. [After each village completes]
   └─→ TrackGenerator.GenerateAsync()
       ├─ Calculate radial slot at Crossroads for this village
       ├─ Generate platform corridor at Crossroads (extends from perimeter)
       ├─ Generate station at village
       └─ Lay track connecting the two
```

### Trigger for Crossroads Generation

**Option A:** Generate on first guild sync (lazy initialization)  
**Option B:** Generate at application startup before any other jobs  
**Selected: Option B** — ensures spawn point exists before first player could join

Add `CrossroadsInitializationService : BackgroundService` that:
1. Checks if Crossroads already built (flag in database or check for beacon block at origin)
2. If not built, enqueues `CreateCrossroads` job to Redis queue
3. `WorldGenJobProcessor` handles `CreateCrossroads` same as other job types

---

## 6. Component Changes Summary

### New Files

| File | Purpose |
|------|---------|
| `src/WorldGen.Worker/Generators/CrossroadsGenerator.cs` | Builds the grand plaza, fountain, avenues, decorations |
| `src/WorldGen.Worker/Generators/ICrossroadsGenerator.cs` | Interface for DI |
| `src/WorldGen.Worker/Models/CrossroadsGenerationRequest.cs` | Request model (minimal — just includes job metadata) |
| `src/WorldGen.Worker/CrossroadsInitializationService.cs` | BackgroundService to trigger Crossroads generation on startup |

### Modified Files

| File | Change |
|------|--------|
| `Bridge.Data/WorldConstants.cs` | Add `CrossroadsCenterX = 0`, `CrossroadsCenterZ = 0`, `CrossroadsPlazaRadius = 30`, `HubPlatformSlots = 16` |
| `Bridge.Data/GenerationJobType.cs` | Add `CreateCrossroads` enum value |
| `WorldGen.Worker/WorldGenJobProcessor.cs` | Handle `CreateCrossroads` job type; modify `CreateTrack` enqueueing to use hub-and-spoke model |
| `WorldGen.Worker/Generators/TrackGenerator.cs` | Update to route all tracks to/from Crossroads; add radial platform slot logic |
| `WorldGen.Worker/Models/TrackGenerationRequest.cs` | Simplify: village coords only, hub coords are constant |
| `Bridge.Data/Mapping/VillageMappingExtensions.cs` | Update `GetVillageGridPosition` to skip (0,0) — offset village indices |
| `WorldGen.Worker/Program.cs` | Register `ICrossroadsGenerator`, `CrossroadsInitializationService` |

---

## 7. Work Items for Implementation

### Phase 1: Crossroads Core (Batgirl)

| ID | Task | Estimate |
|----|------|----------|
| CROSS-01 | Create `CrossroadsGenerator` with grand plaza (61×61 polished stone brick platform) | 2h |
| CROSS-02 | Implement multi-tier fountain (15×15, 3 tiers, water, glowstone, beacon) | 2h |
| CROSS-03 | Add tree-lined avenues (4 cardinal paths, oak trees every 10 blocks) | 1h |
| CROSS-04 | Add decorative elements (lampposts, benches, flower beds, banners) | 2h |
| CROSS-05 | Add welcome signs and spawn point command | 30m |

### Phase 2: Track Topology (Batgirl)

| ID | Task | Estimate |
|----|------|----------|
| CROSS-06 | Modify `TrackGenerator` to route to Crossroads hub instead of peer villages | 2h |
| CROSS-07 | Implement radial platform slot assignment based on village angle | 1h |
| CROSS-08 | Build platform corridor structures at Crossroads (extending outward) | 2h |
| CROSS-09 | Add directional signage at each platform corridor | 1h |

### Phase 3: Infrastructure (Lucius)

| ID | Task | Estimate |
|----|------|----------|
| CROSS-10 | Add `CreateCrossroads` job type to enum and processor | 1h |
| CROSS-11 | Create `CrossroadsInitializationService` to trigger hub generation on startup | 1h |
| CROSS-12 | Update village grid positioning to skip origin cell | 30m |
| CROSS-13 | Add constants to `WorldConstants.cs` | 15m |

### Phase 4: Testing (Nightwing)

| ID | Task | Estimate |
|----|------|----------|
| CROSS-14 | Unit tests for radial slot calculation | 1h |
| CROSS-15 | Acceptance tests: Crossroads exists at spawn, tracks route through hub | 2h |

---

## 8. Migration Considerations

### Existing Worlds

If this feature is deployed to a world with existing point-to-point tracks:
1. Crossroads generation will overlay at (0, 0) — any existing village at origin would be damaged
2. Old tracks remain but become "legacy" — no automated cleanup

**Recommendation:** This feature targets new worlds. Document that existing deployments should start fresh or manually clean up origin area.

### Backward Compatibility

The `CreateTrack` job schema changes:
- Old: `{ sourceVillageName, destVillageName, sourceX, sourceZ, destX, destZ }`
- New: `{ villageName, villageX, villageZ }` (hub coords are constants)

Jobs already in queue at deployment time will fail. **Drain queue before deploying this change.**

---

## 9. Future Enhancements (Out of Scope)

- **Hub station UI:** Map display showing all connected villages
- **Express routes:** Direct village-to-village tracks for frequently traveled routes
- **Hub events:** NPCs, scheduled events, dynamic decorations
- **Multiple hubs:** Regional sub-hubs for very large guild counts

---

## Decision

✅ **Approved for implementation**

The Crossroads Hub design provides:
1. Intuitive navigation (everyone knows where the center is)
2. Scalable track topology (linear growth vs. quadratic)
3. Beautiful spawn experience for new players
4. Clear work breakdown for squad implementation

**Assignees:**
- Batgirl: CROSS-01 through CROSS-09 (generation)
- Lucius: CROSS-10 through CROSS-13 (infrastructure)
- Nightwing: CROSS-14, CROSS-15 (testing)

---

*Gordon, Lead/Architect*
