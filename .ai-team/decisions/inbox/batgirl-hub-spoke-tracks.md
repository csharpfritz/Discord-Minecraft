# Decision: Hub-and-Spoke Track Topology

**Date:** 2026-02-13
**By:** Batgirl
**Issue:** #20 (S4-02)

## What

Track routing changed from point-to-point (N×(N-1)/2 tracks) to hub-and-spoke (1 track per village to Crossroads at origin).

### Changes Made

1. **WorldGenJobProcessor.EnqueueTrackJobsForNewVillageAsync** — No longer queries existing villages or loops. Creates exactly one CreateTrack job with `DestCenterX/Z = 0`, `DestinationVillageName = "Crossroads"`, `DestinationChannelGroupId = 0`.

2. **TrackGenerator.GenerateAsync** — Detects Crossroads destination (`DestCenterX == 0 && DestCenterZ == 0`). Village-end station uses existing south-offset placement. Crossroads-end station uses radial slot from `GetCrossroadsSlotPosition()` — Atan2-based angle mapping to 16 evenly spaced slots at radius 35.

3. **WorldConstants** — Added `CrossroadsStationRadius = 35`.

## Why

Point-to-point created O(n²) tracks, causing overlapping rails, excessive forceloaded chunks, and confusing station sprawl. Hub-and-spoke gives O(n) tracks with clean radial arrivals at the central hub.

## Impact

- **Existing Sprint 3 worlds:** Old point-to-point tracks remain in-world (not deleted). New villages only get hub-and-spoke tracks.
- **TrackJobPayload/TrackGenerationRequest:** No schema changes. Same DTOs, different values.
- **Crossroads stations:** Each village gets a deterministic radial slot. Slots are reused if two villages happen to map to the same angle (rare with 16 slots and grid-spaced villages).
