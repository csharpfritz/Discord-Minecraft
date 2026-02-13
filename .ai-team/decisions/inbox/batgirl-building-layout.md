# Decision: Building Layout and Village Fence

**Date:** 2026-02-12
**Author:** Batgirl (World Builder)
**Status:** Implemented

## Context

Buildings were placed in a tight ring at radius 60 using angle-based positioning, causing them to touch or overlap like an apartment complex. The entrance sign was also placed inside the doorway facing the wrong direction.

## Decision

### 1. Grid Layout for Buildings

Changed from ring layout to a **4×4 grid layout** with proper spacing:

- **GridSpacing:** 27 blocks (footprint 21 + buffer 3×2 = 27)
- **GridStartOffset:** 50 blocks from village center
- **GridColumns:** 4 (supports up to 16 buildings per village)

Formula for building position:
```
bx = cx + ((col < 2) ? -(50 + (1-col)*27) : (50 + (col-2)*27))
bz = cz + ((row < 2) ? -(50 + (1-row)*27) : (50 + (row-2)*27))
```

This places buildings in quadrants around the village center with at least 3 blocks of empty space on all sides.

### 2. Entrance Sign Placement

Moved exterior sign from `maxZ` (inside) to `maxZ + 1` (outside):
- Sign attaches to the outer wall and faces SOUTH
- Position: `(bx, BaseY + 5, maxZ + 1)` — above the 4-tall doorway
- Players approaching from outside can now see the channel name

### 3. Village Perimeter Fence

Added oak fence around the entire village at **radius 150 blocks**:
- Encompasses all buildings (max building edge at ~142 blocks from center)
- 3-wide oak fence gates at the 4 cardinal entrances
- Corner fence posts with lanterns for nighttime visibility

## Impact

- **BuildingGenerator.cs:** Grid layout calculation, sign placement fix
- **VillageGenerator.cs:** FenceRadius constant, GenerateVillageFenceAsync method, increased forceload radius

## Alternatives Considered

- Spiral layout: More compact but harder to navigate
- Hexagonal grid: Better packing but complex coordinate math
- Dynamic spacing: Adapts to building count but unpredictable layout

Grid layout was chosen for simplicity and predictable building positions.
