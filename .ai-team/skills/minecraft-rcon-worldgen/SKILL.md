# SKILL: Minecraft RCON World Generation

**Confidence:** medium
**Source:** earned
**Tags:** minecraft, rcon, world-generation, coreCRON

## What

Build Minecraft structures programmatically via RCON commands using CoreRCON in .NET, with rate limiting and connection management.

## Pattern

```csharp
// RconService — singleton, wraps CoreRCON with serialization + rate limiting
public sealed class RconService : IAsyncDisposable
{
    private RCON? _rcon;
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    // Resolve hostname → IPAddress before connecting (CoreRCON requires IPAddress)
    var addresses = await Dns.GetHostAddressesAsync(host);
    _rcon = new RCON(addresses[0], port, password);
    await _rcon.ConnectAsync();

    // Serialize commands with configurable delay
    await _semaphore.WaitAsync(ct);
    try {
        var response = await _rcon.SendCommandAsync(command);
        await Task.Delay(delayMs, ct); // Rate limiting
        return response;
    } finally { _semaphore.Release(); }
}
```

### RCON Commands for Structure Building

```
/fill x1 y1 z1 x2 y2 z2 block           — fill a cuboid with blocks
/setblock x y z block                     — place a single block
/setblock x y z minecraft:oak_sign[rotation=0]{front_text:{messages:['"line1"','"line2"','"line3"','"line4"']}}
```

### Minecraft Sign Rotations
- `rotation=0` → North-facing
- `rotation=4` → East-facing
- `rotation=8` → South-facing
- `rotation=12` → West-facing

### Sign Text Format (1.20+)
```
{front_text:{messages:['{"text":"Line 1"}','{"text":"Line 2"}','""','""']}}
```

## Key Details

- Superflat world: y=64 is ground level
- CoreRCON constructor takes `IPAddress`, not hostname string — must resolve first
- Rate limit RCON commands (50ms default) to avoid overwhelming Paper MC
- Use `/fill` for large areas (floors, walls) — much faster than individual `/setblock`
- Use `/setblock` for individual decorative blocks (signs, water, glowstone)
- Single RCON connection with semaphore prevents connection pool exhaustion
- Reset connection on failure and retry — RCON connections can drop

### Multi-Floor Building Construction Order

```
1. Foundation (/fill stone_bricks at BaseY)
2. Walls (/fill stone_bricks, all 4 faces from BaseY+1 to WallTop)
3. Clear interior (/fill air, single command for entire interior volume)
4. Intermediate floors (/fill oak_planks at each floor Y)
5. Entrance (/fill air to cut doorway in wall)
6. Stairs (individual /setblock for stair blocks with facing direction)
7. Windows (/fill glass_pane to cut into walls)
8. Roof (/fill stone_brick_slab)
9. Lighting (/setblock glowstone in grid pattern per floor ceiling)
10. Decoration (/fill carpet along perimeter per floor)
11. Signs (/setblock wall_sign with NBT)
```

### Wall Sign vs Standing Sign
```
# Standing sign (freestanding, rotation-based):
/setblock x y z minecraft:oak_sign[rotation=8]{front_text:{messages:[...]}}

# Wall sign (attached to wall, facing-based):
/setblock x y z minecraft:oak_wall_sign[facing=south]{front_text:{messages:[...]}}
# facing values: north, south, east, west (not numeric rotation)
```

### Stair Block Facing
```
minecraft:oak_stairs[facing=north]  — ascending toward north
minecraft:oak_stairs[facing=south]  — ascending toward south
minecraft:oak_stairs[facing=east]   — ascending toward east
minecraft:oak_stairs[facing=west]   — ascending toward west
```

### Ring Layout for Building Placement
```csharp
double angleRad = buildingIndex * (360.0 / maxSlots) * Math.PI / 180.0;
int buildingX = centerX + (int)(radius * Math.Cos(angleRad));
int buildingZ = centerZ + (int)(radius * Math.Sin(angleRad));
```

## When to Use

- Any Minecraft structure generation via RCON (villages, buildings, paths, tracks)
- Building generation patterns: walls-first, clear-interior, then details
- Ring layout for placing buildings around a central point

## When NOT to Use

- For complex redstone or entity placement — consider a Paper plugin instead
- For real-time game interactions — RCON has latency
