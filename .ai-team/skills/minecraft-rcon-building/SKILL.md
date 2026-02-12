# SKILL: Minecraft RCON Building Construction

**Confidence:** medium
**Source:** earned
**Tags:** minecraft, rcon, building, construction, coreCRON, aspire, docker

## What

Everything the team has learned about constructing Minecraft structures via RCON commands — coordinate systems, CoreRCON connection patterns, block placement order, networking, and medieval castle design patterns.

## 1. Superflat World Coordinates

```
Y = -64  Bedrock (bottom layer)
Y = -63  Dirt
Y = -62  Dirt
Y = -61  Dirt
Y = -60  Grass Block ← SURFACE LEVEL (BaseY)
Y = -59  First block above ground
```

**Critical:** Surface is `Y = -60`, **NOT** `Y = 64`. The old default world type used Y=64 but superflat worlds (1.18+) use negative Y coordinates with bedrock at -64.

All generators in this project use `BaseY = -60`.

## 2. CoreRCON Connection Patterns

### IPv4 Only — IPv6 Causes NotSupportedException

```csharp
// CoreRCON uses raw TCP sockets with AddressFamily.InterNetwork (IPv4)
// Passing an IPv6 address throws System.NotSupportedException
var addresses = await Dns.GetHostAddressesAsync(host);
var ipv4 = addresses.FirstOrDefault(a =>
    a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
    ?? IPAddress.Loopback;

var rcon = new RCON(ipv4, port, password);
```

### SafeDispose Pattern

```csharp
// Socket may not be connected when Dispose is called — wrap in try-catch
private void SafeDisposeRcon()
{
    try { _rcon?.Dispose(); }
    catch (Exception ex) { _logger.LogDebug(ex, "Suppressed exception during RCON dispose"); }
    _rcon = null;
}
```

### Connection Verification

```csharp
// After ConnectAsync, verify with a lightweight command
await rcon.ConnectAsync();
var verify = await rcon.SendCommandAsync("seed"); // fast, read-only
```

### Singleton + Semaphore Thread Safety

```csharp
// Single RCON connection shared across all generators
// SemaphoreSlim(1,1) serializes all command sends
private readonly SemaphoreSlim _semaphore = new(1, 1);

await _semaphore.WaitAsync(ct);
try {
    var response = await _rcon.SendCommandAsync(command);
    await Task.Delay(_commandDelayMs, ct); // rate limiting
    return response;
} finally { _semaphore.Release(); }
```

## 3. RCON Command Patterns for Building

### Bulk Fill (max 32,768 blocks per command)

```
/fill x1 y1 z1 x2 y2 z2 block
/fill 100 -60 200 120 -40 220 minecraft:stone_bricks
```

### Single Block with State and NBT

```
/setblock x y z block[state]{nbt}
/setblock 100 -58 200 minecraft:oak_stairs[facing=north]
/setblock 100 -58 200 minecraft:oak_wall_sign[facing=south]{front_text:{messages:['{"text":"Hello"}','""','""','""']}}
```

### Command Delay

- Default: 50ms between commands (configurable via `Rcon:CommandDelayMs`)
- Paper MC can be overwhelmed without rate limiting
- Applied after every command send in the semaphore-protected section

### Sign NBT Format (1.20+)

```
{front_text:{messages:['{"text":"Line 1"}','{"text":"Line 2"}','""','""']}}
```

- Standing signs use `rotation=N` (0=north, 4=east, 8=south, 12=west)
- Wall signs use `facing=direction` (north, south, east, west)
- Wall signs MUST be placed on or adjacent to a solid block

## 4. Block Placement Order (CRITICAL)

This order prevents floating blocks, vanishing floors, and other rendering bugs:

```
1. Foundation/platform FIRST         — solid ground base
2. Exterior walls SECOND             — define the building shell
3. Corner turrets/pillars THIRD      — structural accents
4. Clear interior (air fill) FOURTH  — clears ONLY inside walls
5. Floor slabs FIFTH                 — after clear so they don't get erased
6. Staircase openings + stairs SIXTH — cut through floors, place stairs
7. Roof/parapet SEVENTH              — cap the building
8. Windows EIGHTH                    — air gaps in walls (arrow slits)
9. Entrance doorway NINTH            — clear air in wall for door
10. Wall-mounted lighting TENTH      — torches/lanterns on solid walls
11. Signs LAST                       — must attach to solid blocks
```

### Why This Order Matters

- **Interior clear wipes floors:** If you place floors before clearing interior, the air fill erases them.
- **Lighting floats:** Glowstone placed on ceilings gets destroyed when ClearInterior runs afterward. Use wall-mounted torches instead, placed AFTER interior is cleared.
- **Signs need solid backing:** Wall signs placed before their support block exists will float or break. Always place signs LAST.

## 5. Aspire/Docker Networking for RCON

### Port Mapping

```
Container internal port (targetPort) = server.properties rcon.port (25575)
Host external port (port) = what .NET connects to (25675)
```

In Aspire `AddContainer` or `AddDockerfile`:
```csharp
.WithEndpoint(targetPort: 25575, port: 25675, name: "rcon", scheme: "tcp")
```

### Connection Target

- Use `localhost` + host-mapped port from .NET code
- Do NOT use Docker container hostname — only works inside Docker network
- `Rcon:Host` should resolve to localhost or 127.0.0.1

### Health Check

```csharp
// Use IPAddress.Loopback directly for health checks
var rcon = new RCON(IPAddress.Loopback, port, password);
await rcon.ConnectAsync();
var result = await rcon.SendCommandAsync("seed");
```

## 6. Medieval Castle Building Patterns

### Block Palette

| Purpose | Block |
|---------|-------|
| Main walls | `minecraft:cobblestone` |
| Accent/trim | `minecraft:stone_bricks` |
| Corner pillars | `minecraft:oak_log` |
| Floors | `minecraft:oak_planks` |
| Roof/parapet | `minecraft:stone_bricks` |
| Stairs | `minecraft:oak_stairs` |

### Architectural Features

- **Corner turrets:** Oak log pillars at building corners, extending 1 block above main roofline
- **Crenellated parapet:** Alternating stone bricks and air gaps along the roofline (merlon pattern)
- **Arrow slit windows:** 1-wide, 2-tall air gaps in walls — no glass panes needed
- **Arched doorway:** 3-wide, 4-tall entrance with stone brick arch at top
- **3-wide staircases:** Proper wide stairs with landings, not 1-block-wide steps
- **Wall-mounted lighting:** Torches/lanterns attached to walls, not floating glowstone in ceilings

### Building Dimensions

- Footprint: 21×21 blocks
- Floors: 2 (reduced from 4 for better proportions)
- Floor height: 5 blocks (floor-to-ceiling)
- Style: Castle keep, not office building

## When to Use

- Building any Minecraft structure via RCON
- Setting up CoreRCON connections in .NET
- Debugging floating blocks, vanishing structures, or connection failures
- Configuring Aspire/Docker port mappings for RCON
- Designing medieval-themed Minecraft buildings

## When NOT to Use

- For complex redstone circuits — use a Paper plugin
- For real-time player interactions — RCON has too much latency
- For structures larger than 32,768 blocks per fill — split into multiple commands
