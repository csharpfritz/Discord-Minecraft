using Microsoft.Extensions.Logging;
using WorldGen.Worker.Models;
using WorldGen.Worker.Services;

namespace WorldGen.Worker.Generators;

/// <summary>
/// Generates medieval castle-style buildings via RCON commands.
/// 21×21 footprint, 2 floors, cobblestone walls with stone brick trim,
/// corner turrets, crenellated parapet, arrow slit windows, 3-wide staircase.
/// </summary>
public sealed class BuildingGenerator(RconService rcon, ILogger<BuildingGenerator> logger) : IBuildingGenerator
{
    private const int BaseY = -60; // Superflat world surface level
    private const int Footprint = 21;
    private const int HalfFootprint = Footprint / 2; // 10
    private const int Floors = 2;
    private const int FloorHeight = 5; // floor-to-ceiling per story
    private const int WallTop = BaseY + Floors * FloorHeight; // y=-50
    private const int RoofY = WallTop + 1; // y=-49

    // Corner turret positions relative to building center
    private static readonly (int dx, int dz)[] TurretOffsets =
    [
        (-HalfFootprint, -HalfFootprint), // NW
        (HalfFootprint, -HalfFootprint),  // NE
        (-HalfFootprint, HalfFootprint),  // SW
        (HalfFootprint, HalfFootprint)    // SE
    ];

    // Linear "main street" layout: buildings in two rows facing each other across a central street
    // Building footprint (21) + gap (3) = 24 block spacing between building centers along the street
    private const int BuildingSpacing = 24; // footprint + 3-block gap between buildings
    private const int StreetWidth = 15; // Main street width between north/south rows (players walk here)
    private const int RowOffset = 20; // Distance from village center to row center (buildings at ±20)

    public async Task GenerateAsync(BuildingGenerationRequest request, CancellationToken ct)
    {
        var cx = request.VillageCenterX;
        var cz = request.VillageCenterZ;

        // Main street layout: Two rows of buildings facing each other across a central street
        // Row 0 (north side): buildings face south (entrance on south = +Z)
        // Row 1 (south side): buildings face north (entrance on north = -Z)
        // Buildings are placed in a linear row along the X axis, tightly grouped
        int row = request.BuildingIndex % 2; // 0 = north row, 1 = south row
        int positionInRow = request.BuildingIndex / 2; // which building along the row

        // X position: linear placement along the street, centered on village
        int bx = cx + (positionInRow - 3) * BuildingSpacing; // Center 8 buildings (indices 0-7 per row)

        // Z position: north or south side of the main street (tight spacing: 40 blocks between rows)
        int bz = row == 0
            ? cz - RowOffset // North row (20 blocks north of center)
            : cz + RowOffset; // South row (20 blocks south of center)

        logger.LogInformation(
            "Generating medieval castle '{Name}' at ({BX}, {BZ}), index {Index} in village at ({CX}, {CZ})",
            request.Name, bx, bz, request.BuildingIndex, cx, cz);

        // Forceload chunks covering the building footprint AND walkway path
        int pathMinX = Math.Min(bx - HalfFootprint, cx);
        int pathMaxX = Math.Max(bx + HalfFootprint, cx);
        int pathMinZ = Math.Min(bz - HalfFootprint, cz);
        int pathMaxZ = Math.Max(bz + HalfFootprint + 2, cz);
        int minChunkX = (pathMinX - 5) >> 4;
        int maxChunkX = (pathMaxX + 5) >> 4;
        int minChunkZ = (pathMinZ - 5) >> 4;
        int maxChunkZ = (pathMaxZ + 5) >> 4;
        await rcon.SendCommandAsync($"forceload add {minChunkX << 4} {minChunkZ << 4} {maxChunkX << 4} {maxChunkZ << 4}", ct);

        // Block placement order is critical to avoid floating/erased blocks:
        // 1. Walkway  2. Foundation  3. Walls  4. Turrets  5. Clear interior
        // 6. Floors  7. Stairs  8. Roof/parapet  9. Windows
        // 10. Entrance  11. Lighting  12. Signs
        await GenerateWalkwayAsync(cx, cz, bx, bz, ct);
        await GenerateFoundationAsync(bx, bz, ct);
        await GenerateWallsAsync(bx, bz, ct);
        await GenerateCornerTurretsAsync(bx, bz, ct);
        await ClearInteriorAsync(bx, bz, ct);
        await GenerateFloorsAsync(bx, bz, ct);
        await GenerateStairsAsync(bx, bz, ct);
        await GenerateRoofAndParapetAsync(bx, bz, ct);
        await GenerateArrowSlitsAsync(bx, bz, ct);
        await GenerateEntranceAsync(bx, bz, ct);
        await GenerateLightingAsync(bx, bz, ct);
        await GenerateSignsAsync(bx, bz, request.Name, ct);

        // Release forceloaded chunks
        await rcon.SendCommandAsync($"forceload remove {minChunkX << 4} {minChunkZ << 4} {maxChunkX << 4} {maxChunkZ << 4}", ct);

        logger.LogInformation("Medieval castle '{Name}' generation complete at ({BX}, {BZ})", request.Name, bx, bz);
    }

    /// <summary>
    /// Cobblestone walkway from village center to building entrance.
    /// Creates an L-shaped path: X direction first, then Z direction to the south entrance.
    /// </summary>
    private async Task GenerateWalkwayAsync(int cx, int cz, int bx, int bz, CancellationToken ct)
    {
        const int PathHalfWidth = 1; // 3-block wide path
        int entranceZ = bz + HalfFootprint + 1; // Building entrance is on south face

        logger.LogInformation("Generating cobblestone walkway from village center ({CX},{CZ}) to building ({BX},{BZ})", cx, cz, bx, bz);

        // Horizontal segment (from center X toward building X)
        if (bx != cx)
        {
            int minPathX = Math.Min(cx, bx);
            int maxPathX = Math.Max(cx, bx);
            await rcon.SendFillAsync(minPathX, BaseY, cz - PathHalfWidth, maxPathX, BaseY, cz + PathHalfWidth, "minecraft:cobblestone", ct);
        }

        // Vertical segment (from path at building X toward building entrance)
        int minPathZ = Math.Min(cz, entranceZ);
        int maxPathZ = Math.Max(cz, entranceZ);
        await rcon.SendFillAsync(bx - PathHalfWidth, BaseY, minPathZ, bx + PathHalfWidth, BaseY, maxPathZ, "minecraft:cobblestone", ct);
    }

    /// <summary>21×21 cobblestone foundation slab at surface level</summary>
    private async Task GenerateFoundationAsync(int bx, int bz, CancellationToken ct)
    {
        await rcon.SendFillAsync(
            bx - HalfFootprint, BaseY, bz - HalfFootprint,
            bx + HalfFootprint, BaseY, bz + HalfFootprint,
            "minecraft:cobblestone", ct);
    }

    /// <summary>
    /// Cobblestone walls with stone brick trim at top and bottom courses.
    /// Bottom row and top row are stone bricks, middle is cobblestone.
    /// </summary>
    private async Task GenerateWallsAsync(int bx, int bz, CancellationToken ct)
    {
        int minX = bx - HalfFootprint;
        int maxX = bx + HalfFootprint;
        int minZ = bz - HalfFootprint;
        int maxZ = bz + HalfFootprint;

        // Main cobblestone walls (all 4 faces)
        await rcon.SendFillAsync(minX, BaseY + 1, minZ, maxX, WallTop, minZ, "minecraft:cobblestone", ct);
        await rcon.SendFillAsync(minX, BaseY + 1, maxZ, maxX, WallTop, maxZ, "minecraft:cobblestone", ct);
        await rcon.SendFillAsync(minX, BaseY + 1, minZ, minX, WallTop, maxZ, "minecraft:cobblestone", ct);
        await rcon.SendFillAsync(maxX, BaseY + 1, minZ, maxX, WallTop, maxZ, "minecraft:cobblestone", ct);

        // Stone brick trim — bottom course
        await rcon.SendFillAsync(minX, BaseY + 1, minZ, maxX, BaseY + 1, minZ, "minecraft:stone_bricks", ct);
        await rcon.SendFillAsync(minX, BaseY + 1, maxZ, maxX, BaseY + 1, maxZ, "minecraft:stone_bricks", ct);
        await rcon.SendFillAsync(minX, BaseY + 1, minZ, minX, BaseY + 1, maxZ, "minecraft:stone_bricks", ct);
        await rcon.SendFillAsync(maxX, BaseY + 1, minZ, maxX, BaseY + 1, maxZ, "minecraft:stone_bricks", ct);

        // Stone brick trim — top course
        await rcon.SendFillAsync(minX, WallTop, minZ, maxX, WallTop, minZ, "minecraft:stone_bricks", ct);
        await rcon.SendFillAsync(minX, WallTop, maxZ, maxX, WallTop, maxZ, "minecraft:stone_bricks", ct);
        await rcon.SendFillAsync(minX, WallTop, minZ, minX, WallTop, maxZ, "minecraft:stone_bricks", ct);
        await rcon.SendFillAsync(maxX, WallTop, minZ, maxX, WallTop, maxZ, "minecraft:stone_bricks", ct);
    }

    /// <summary>
    /// Oak log pillars at the 4 corners, extending 1 block above the main roofline.
    /// Creates the castle turret silhouette.
    /// </summary>
    private async Task GenerateCornerTurretsAsync(int bx, int bz, CancellationToken ct)
    {
        foreach (var (dx, dz) in TurretOffsets)
        {
            int tx = bx + dx;
            int tz = bz + dz;

            // Oak log pillar from ground to 1 above wall top
            for (int y = BaseY + 1; y <= WallTop + 1; y++)
            {
                await rcon.SendSetBlockAsync(tx, y, tz, "minecraft:oak_log", ct);
            }

            // Stone brick cap on top of turret
            await rcon.SendSetBlockAsync(tx, WallTop + 2, tz, "minecraft:stone_brick_slab", ct);
        }
    }

    /// <summary>Clear air inside the walls (19×19 interior, full height)</summary>
    private async Task ClearInteriorAsync(int bx, int bz, CancellationToken ct)
    {
        await rcon.SendFillAsync(
            bx - HalfFootprint + 1, BaseY + 1, bz - HalfFootprint + 1,
            bx + HalfFootprint - 1, WallTop, bz + HalfFootprint - 1,
            "minecraft:air", ct);
    }

    /// <summary>Oak plank floor between stories (only 1 intermediate floor for 2-story building)</summary>
    private async Task GenerateFloorsAsync(int bx, int bz, CancellationToken ct)
    {
        // Second floor slab at BaseY + FloorHeight = -55
        int floorY = BaseY + FloorHeight;
        await rcon.SendFillAsync(
            bx - HalfFootprint + 1, floorY, bz - HalfFootprint + 1,
            bx + HalfFootprint - 1, floorY, bz + HalfFootprint - 1,
            "minecraft:oak_planks", ct);
    }

    /// <summary>
    /// 3-wide oak staircase in the NE corner with landing between floors.
    /// Runs south-to-north, then switches back west on a landing.
    /// </summary>
    private async Task GenerateStairsAsync(int bx, int bz, CancellationToken ct)
    {
        int stairX = bx + HalfFootprint - 5; // NE corner, 3 blocks wide
        int stairZ = bz - HalfFootprint + 1; // near north wall

        int baseFloorY = BaseY + 1; // ground floor walking surface

        // Cut stairwell opening in the second floor
        int upperFloorY = BaseY + FloorHeight;
        await rcon.SendFillAsync(stairX, upperFloorY, stairZ,
            stairX + 2, upperFloorY, stairZ + 4, "minecraft:air", ct);

        // First run: 3-wide stairs going north (5 steps to reach landing height)
        for (int step = 0; step < 4; step++)
        {
            int sy = baseFloorY + step;
            int sz = stairZ + 4 - step;
            for (int dx = 0; dx < 3; dx++)
            {
                await rcon.SendSetBlockAsync(stairX + dx, sy, sz,
                    "minecraft:oak_stairs[facing=north]", ct);
            }
        }

        // Landing platform
        await rcon.SendFillAsync(stairX, baseFloorY + 4, stairZ,
            stairX + 2, baseFloorY + 4, stairZ + 1,
            "minecraft:oak_planks", ct);
    }

    /// <summary>
    /// Stone brick roof slab with crenellated parapet (alternating stone/air merlons).
    /// </summary>
    private async Task GenerateRoofAndParapetAsync(int bx, int bz, CancellationToken ct)
    {
        int minX = bx - HalfFootprint;
        int maxX = bx + HalfFootprint;
        int minZ = bz - HalfFootprint;
        int maxZ = bz + HalfFootprint;

        // Solid roof slab
        await rcon.SendFillAsync(minX, RoofY, minZ, maxX, RoofY, maxZ,
            "minecraft:stone_brick_slab", ct);

        // Crenellated parapet — alternating stone bricks along the roof edge
        // North and south edges
        for (int x = minX; x <= maxX; x += 2)
        {
            await rcon.SendSetBlockAsync(x, RoofY + 1, minZ, "minecraft:stone_bricks", ct);
            await rcon.SendSetBlockAsync(x, RoofY + 1, maxZ, "minecraft:stone_bricks", ct);
        }
        // West and east edges (skip corners to avoid doubling with turrets)
        for (int z = minZ + 2; z <= maxZ - 2; z += 2)
        {
            await rcon.SendSetBlockAsync(minX, RoofY + 1, z, "minecraft:stone_bricks", ct);
            await rcon.SendSetBlockAsync(maxX, RoofY + 1, z, "minecraft:stone_bricks", ct);
        }
    }

    /// <summary>
    /// Arrow slit windows: 1-wide, 2-tall air gaps in walls on each face per floor.
    /// Placed symmetrically, 4 slits per wall per floor.
    /// </summary>
    private async Task GenerateArrowSlitsAsync(int bx, int bz, CancellationToken ct)
    {
        int minX = bx - HalfFootprint;
        int maxX = bx + HalfFootprint;
        int minZ = bz - HalfFootprint;
        int maxZ = bz + HalfFootprint;

        // Window positions along each wall (offsets from center)
        int[] windowOffsets = [-6, -3, 3, 6];

        for (int floor = 0; floor < Floors; floor++)
        {
            int windowBaseY = BaseY + 2 + floor * FloorHeight; // 2 blocks above floor

            foreach (int offset in windowOffsets)
            {
                // North wall arrow slits
                await rcon.SendFillAsync(bx + offset, windowBaseY, minZ,
                    bx + offset, windowBaseY + 1, minZ, "minecraft:air", ct);

                // South wall arrow slits (skip center on ground floor for entrance)
                if (floor > 0 || (offset != -3 && offset != 3))
                {
                    await rcon.SendFillAsync(bx + offset, windowBaseY, maxZ,
                        bx + offset, windowBaseY + 1, maxZ, "minecraft:air", ct);
                }

                // West wall arrow slits
                await rcon.SendFillAsync(minX, windowBaseY, bz + offset,
                    minX, windowBaseY + 1, bz + offset, "minecraft:air", ct);

                // East wall arrow slits
                await rcon.SendFillAsync(maxX, windowBaseY, bz + offset,
                    maxX, windowBaseY + 1, bz + offset, "minecraft:air", ct);
            }
        }
    }

    /// <summary>
    /// 3-wide, 4-tall arched doorway on the south face with stone brick arch.
    /// </summary>
    private async Task GenerateEntranceAsync(int bx, int bz, CancellationToken ct)
    {
        int maxZ = bz + HalfFootprint;

        // Clear 3-wide, 4-tall opening
        await rcon.SendFillAsync(bx - 1, BaseY + 1, maxZ,
            bx + 1, BaseY + 4, maxZ, "minecraft:air", ct);

        // Stone brick arch at top of doorway
        await rcon.SendSetBlockAsync(bx - 1, BaseY + 4, maxZ, "minecraft:stone_bricks", ct);
        await rcon.SendSetBlockAsync(bx + 1, BaseY + 4, maxZ, "minecraft:stone_bricks", ct);
    }

    /// <summary>
    /// Wall-mounted torches for lighting, placed on interior walls after clearing.
    /// Torches on all 4 interior walls, spaced every 4 blocks, on each floor.
    /// </summary>
    private async Task GenerateLightingAsync(int bx, int bz, CancellationToken ct)
    {
        int minX = bx - HalfFootprint + 1; // interior wall face
        int maxX = bx + HalfFootprint - 1;
        int minZ = bz - HalfFootprint + 1;
        int maxZ = bz + HalfFootprint - 1;

        for (int floor = 0; floor < Floors; floor++)
        {
            int torchY = BaseY + 3 + floor * FloorHeight; // eye-level height

            // Torches on north interior wall (facing south)
            for (int x = bx - 8; x <= bx + 8; x += 4)
            {
                await rcon.SendSetBlockAsync(x, torchY, minZ,
                    "minecraft:wall_torch[facing=south]", ct);
            }

            // Torches on south interior wall (facing north, skip entrance area on ground floor)
            for (int x = bx - 8; x <= bx + 8; x += 4)
            {
                if (floor == 0 && x >= bx - 1 && x <= bx + 1)
                    continue; // skip entrance area
                await rcon.SendSetBlockAsync(x, torchY, maxZ,
                    "minecraft:wall_torch[facing=north]", ct);
            }

            // Torches on west interior wall (facing east)
            for (int z = bz - 8; z <= bz + 8; z += 4)
            {
                await rcon.SendSetBlockAsync(minX, torchY, z,
                    "minecraft:wall_torch[facing=east]", ct);
            }

            // Torches on east interior wall (facing west)
            for (int z = bz - 8; z <= bz + 8; z += 4)
            {
                await rcon.SendSetBlockAsync(maxX, torchY, z,
                    "minecraft:wall_torch[facing=west]", ct);
            }
        }

        // Lanterns at each corner of the entrance (exterior, on wall blocks)
        int entranceZ = bz + HalfFootprint;
        await rcon.SendSetBlockAsync(bx - 2, BaseY + 3, entranceZ,
            "minecraft:wall_torch[facing=south]", ct);
        await rcon.SendSetBlockAsync(bx + 2, BaseY + 3, entranceZ,
            "minecraft:wall_torch[facing=south]", ct);
    }

    /// <summary>
    /// Signs placed LAST so they attach to solid wall blocks.
    /// Entrance sign OUTSIDE above doorway (facing south toward approaching players),
    /// channel name sign inside entrance, and floor label signs on each floor.
    /// </summary>
    private async Task GenerateSignsAsync(int bx, int bz, string channelName, CancellationToken ct)
    {
        var truncatedName = channelName.Length > 15 ? channelName[..15] : channelName;
        
        // Minecraft 1.20+ signs need plain text in double quotes, not JSON objects
        // Format: '\"text here\"' produces a simple text display
        var nameText = $"\"#{truncatedName}\"";
        var castleText = "\"Castle Keep\"";
        var emptyText = "\"\"";

        int maxZ = bz + HalfFootprint;

        // Exterior entrance sign — OUTSIDE the building, above the doorway, facing SOUTH
        // Place at maxZ + 1 (one block outside the wall) so it faces outward toward approaching players
        await rcon.SendSetBlockAsync(bx, BaseY + 5, maxZ + 1,
            $"minecraft:oak_wall_sign[facing=south]{{front_text:{{messages:['{castleText}','{nameText}',{emptyText},{emptyText}]}}}}", ct);

        // NOTE: Interior entrance sign REMOVED — it was floating in the doorway
        // Keep only the exterior sign above the door and floor label signs on each level

        // Floor signs on each level — on south interior wall
        int interiorSignZ = bz + HalfFootprint - 1;
        for (int floor = 0; floor < Floors; floor++)
        {
            int signY = BaseY + 2 + floor * FloorHeight;
            // Place sign offset from entrance sign to avoid overlap on ground floor
            int signX = bx + 3;

            var floorLabel = $"\"Floor {floor + 1}\"";
            var channelLabel = $"\"#{truncatedName}\"";

            await rcon.SendSetBlockAsync(signX, signY, interiorSignZ,
                $"minecraft:oak_wall_sign[facing=north]{{front_text:{{messages:[{emptyText},'{floorLabel}','{channelLabel}',{emptyText}]}}}}", ct);
        }
    }
}
