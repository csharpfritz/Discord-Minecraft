# Decision: Village Amenities and Walkway System

**Date:** 2026-02-12
**Author:** Batgirl (World Builder)
**Status:** Implemented

## Context

Villages needed better infrastructure:
1. Interior entrance signs were floating in doorways (cleared air)
2. No walkways between village center and buildings
3. Fountain didn't scale with village size
4. Buildings felt disconnected from village center

## Decisions

### 1. Interior Sign Removal
- **Removed** the floating interior entrance sign at `(bx, BaseY+2, bz+HalfFootprint-1)`
- **Kept** exterior sign above doorway at `(bx, BaseY+5, maxZ+1)` facing south
- **Kept** floor label signs at `signX=bx+3` on each level (offset from entrance)

### 2. Cobblestone Walkway System
- **BuildingGenerator** creates L-shaped 3-wide cobblestone paths from village center to each building entrance
- Path routing: horizontal (X direction) first, then vertical (Z direction) to south entrance
- **VillageGenerator** creates perimeter walkway at `FenceRadius - 5` (145 blocks from center)
- Walkways generated BEFORE building foundation so foundation cleanly overwrites overlap

### 3. Scalable Fountain
- **Simple fountain (default):** 3x3 stone brick basin with single water source
- **Large fountain (4+ buildings):** 7x7 stone brick base, stone brick slab rim, 5x5 water pool, 3-tall center pillar with glowstone cap

## Coordinates Reference

| Feature | Coordinates/Formula |
|---------|---------------------|
| Perimeter walkway | `cx ± (FenceRadius-5)` = `cx ± 145` blocks |
| Large fountain base | `cx ± 3, cz ± 3` (7x7) |
| Large fountain pillar | `(cx, BaseY+1 to BaseY+3, cz)` |
| Large fountain cap | `(cx, BaseY+4, cz)` glowstone |
| Building walkway | L-path from `(cx, cz)` to `(bx, entranceZ)` |
| Entrance Z | `bz + HalfFootprint + 1` |

## Generation Order

### VillageGenerator
1. Platform (31x31 stone brick)
2. Perimeter wall (with cardinal openings)
3. Fountain (simple or large based on building count)
4. Perimeter walkway (cobblestone ring at fence-5)
5. Lighting (glowstone on walls and paths)
6. Signs (village name on fountain)
7. Welcome paths (stone brick from openings outward)
8. Fence (oak fence at radius 150 with gates)

### BuildingGenerator
1. Walkway (cobblestone from village center to entrance)
2. Foundation (overwrites walkway overlap at building edge)
3. Walls → Turrets → Clear Interior → Floors → Stairs
4. Roof/parapet → Windows → Entrance → Lighting → Signs

## Impact

- All signs are now attached to solid blocks
- Players have clear cobblestone paths to navigate
- Large villages feel more like proper town centers
- Consistent generation order prevents floating/missing blocks
