### 2026-02-14: Track routing fix — remove platform offset, X-first L-path
**By:** Batgirl
**What:** Fixed crossed/wrong-way rails near village plazas by:
1. Removed `GetPlatformOffset()` from TrackGenerator — village stations now always generate at exactly `(cx, cz + VillageStationOffset)`, matching VillageGenerator's station area placement. No offset shifts.
2. Changed L-shaped track path from Z-first (corner at `(srcX, dstZ)`) to X-first (corner at `(dstX, srcZ)`). The track now exits the village station heading east/west along X, then turns toward Crossroads along Z. The corner is far from both stations.
3. Crossroads radial slot system (`GetCrossroadsSlotPosition`) is unchanged — it correctly handles multiple arriving tracks.

**Why:** `GetPlatformOffset` was designed for N×N point-to-point routing where villages had multiple tracks. Hub-and-spoke topology (each village → one track to Crossroads) makes collision avoidance unnecessary for village-side stations. The offset was shifting the track's Z position by up to `slot * 8` blocks, causing it to miss the VillageGenerator's station area and cross through the plaza. The Z-first path direction placed the L-corner inside/near the plaza, creating visible crossed rails. X-first pushes the corner away from both endpoints.
