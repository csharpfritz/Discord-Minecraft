# Decision: Station Platform Redesign & Track Collision Mitigation

**Author:** Batgirl  
**Date:** 2026-02-12  
**Status:** Implemented

## Context

The original station platforms were functional but bare — just slabs and signs. Players arriving at a village station had no sense of arrival or place. Additionally, L-shaped track paths between villages could collide when multiple tracks crossed the same world region.

## Decision

### Station Aesthetics

Redesigned station platforms as welcoming transit hubs:

1. **Shelter structure** — Oak fence corner posts with oak slab roof (4 blocks high)
2. **Ambient lighting** — Hanging lanterns under the shelter roof (replacing floating glowstone)
3. **Waiting amenities** — Oak stair benches facing inward on both sides of track
4. **Decorative touches** — Potted red tulips and blue orchids at shelter posts
5. **Improved signage**:
   - Departure sign uses bold "Station" header with arrow and destination name
   - Arrival sign shows green "Welcome!" with local village name and gray "From:" origin
   - Dispenser has instruction sign: "Get Minecart / Press Button"
6. **Expanded dimensions** — 9×5 blocks (up from 7×3) to accommodate shelter and amenities

### Track Collision Mitigation

Added coordinate-based hash offset at L-path corners:
- Tracks between different village pairs use different Z offsets at their corner junction
- Offset range: -2 to +1 blocks (4 possible lanes)
- Reduces but doesn't eliminate collision probability when many tracks cross

### WorldConstants.cs Corrections

Fixed constants that drifted from generator implementations:
- `BaseY`: 64 → -60 (superflat surface level)
- `BuildingFloors`: 4 → 2 (castle redesign)
- `FloorHeight`: 4 → 5 (actual spacing)

## Impact

- **WorldGen.Worker** — `TrackGenerator.cs` completely rewritten
- **Bridge.Data** — `WorldConstants.cs` corrected
- **Skills** — `minecraft-rcon-worldgen/SKILL.md` updated

## Alternatives Considered

- **Full track collision grid**: Would require maintaining a spatial index of all laid tracks — complexity not justified for current scale
- **Diagonal tracks**: Minecraft rails don't support true diagonals; would require sloped segments which add complexity

## Team Notes

Stations are now the "front door" to each village. Every detail matters — the potted flowers, the warm lantern glow, the benches. Players should feel welcomed, not just deposited.
