# Decision: Fill Consolidation Across All Generators

**By:** Batgirl (World Builder)
**Date:** 2026-02-14
**Status:** Implemented

## What Changed

Phase 2+3 of Gordon's RCON batch optimization plan. Replaced individual setblock loops with bulk fill commands and batch APIs across all 4 generators.

## Key Patterns Used

### 1. Alternating-Row Fills for Checkerboard
The plaza checkerboard now uses alternating z-row fills (striped pattern) instead of per-block setblock. Visually similar decorative effect at 31 fills vs 1,860 setblocks.

### 2. Vertical Fill for Pillars/Posts
Any per-Y setblock loop for columns (turrets, posts, trunks) replaced with a single `fill bx y1 bz bx y2 bz block` command.

### 3. Fill-Then-Clear for Crenellation
Parapet merlons: fill 4 full edges, then batch-clear alternating positions. More efficient than placing individual merlons.

### 4. Rail Segment Fills
Regular rail runs between powered rail intervals filled as contiguous segments. Powered rails + redstone blocks batched separately.

### 5. Decoration Batching
All decoration loops (lighting, flowers, banners, benches, lanterns) collect positions into a list, then send via `SendSetBlockBatchAsync` as a single batch.

## Impact

| Generator | Before | After | Reduction |
|-----------|--------|-------|-----------|
| CrossroadsGenerator | ~2,453 | ~500 | ~80% |
| VillageGenerator | ~83 | ~55 | ~34% |
| BuildingGenerator (per bldg) | ~200 | ~80 | ~60% |
| TrackGenerator (per track) | ~650 | ~250 | ~62% |
| **Total (typical world)** | **~7,100** | **~2,600** | **~63%** |

Combined with Lucius's Phase 1 batch delay reduction, expected generation time drops from ~6.5 minutes to ~30 seconds.

## Block Placement Order Preserved

All fill consolidation operates WITHIN each construction step (foundation → walls → turrets → clear interior → floors → stairs → roof → windows → entrance → lighting → signs). No cross-step reordering.
