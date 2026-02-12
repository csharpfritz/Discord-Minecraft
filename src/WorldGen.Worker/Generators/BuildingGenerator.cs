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

    public async Task GenerateAsync(BuildingGenerationRequest request, CancellationToken ct)
    {
        var cx = request.VillageCenterX;
        var cz = request.VillageCenterZ;

        // Building center is computed from ring layout
        double angleRad = request.BuildingIndex * (360.0 / 16) * Math.PI / 180.0;
        int bx = cx + (int)(60 * Math.Cos(angleRad));
        int bz = cz + (int)(60 * Math.Sin(angleRad));

        logger.LogInformation(
            "Generating medieval castle '{Name}' at ({BX}, {BZ}), index {Index} in village at ({CX}, {CZ})",
            request.Name, bx, bz, request.BuildingIndex, cx, cz);

        // Block placement order is critical to avoid floating/erased blocks:
        // 1. Foundation  2. Walls  3. Turrets  4. Clear interior
        // 5. Floors  6. Stairs  7. Roof/parapet  8. Windows
        // 9. Entrance  10. Lighting  11. Signs
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

        logger.LogInformation("Medieval castle '{Name}' generation complete at ({BX}, {BZ})", request.Name, bx, bz);
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
    /// Entrance sign outside above doorway, channel name sign inside entrance,
    /// and floor label signs on each floor.
    /// </summary>
    private async Task GenerateSignsAsync(int bx, int bz, string channelName, CancellationToken ct)
    {
        var truncatedName = channelName.Length > 15 ? channelName[..15] : channelName;
        var nameText = $"{{\"text\":\"#{truncatedName}\"}}";
        var castleText = "{\"text\":\"Castle Keep\"}";

        int maxZ = bz + HalfFootprint;

        // Exterior entrance sign — on the solid wall block above the arch
        // The arch leaves blocks at bx-1 and bx+1 at BaseY+4, and the wall above at BaseY+5 is solid
        await rcon.SendSetBlockAsync(bx, BaseY + 5, maxZ,
            $"minecraft:oak_wall_sign[facing=south]{{front_text:{{messages:['{castleText}','{nameText}','\"\"','\"\"']}}}}", ct);

        // Interior entrance sign — on south interior wall, attached to solid wall
        int interiorSignZ = bz + HalfFootprint - 1;
        await rcon.SendSetBlockAsync(bx, BaseY + 2, interiorSignZ,
            $"minecraft:oak_wall_sign[facing=north]{{front_text:{{messages:['\"\"','{nameText}','\"\"','\"\"']}}}}", ct);

        // Floor signs on each level — on south interior wall
        for (int floor = 0; floor < Floors; floor++)
        {
            int signY = BaseY + 2 + floor * FloorHeight;
            // Place sign offset from entrance sign to avoid overlap on ground floor
            int signX = bx + 3;

            var floorLabel = $"{{\"text\":\"Floor {floor + 1}\"}}";
            var channelLabel = $"{{\"text\":\"#{truncatedName}\"}}";

            await rcon.SendSetBlockAsync(signX, signY, interiorSignZ,
                $"minecraft:oak_wall_sign[facing=north]{{front_text:{{messages:['\"\"','{floorLabel}','{channelLabel}','\"\"']}}}}", ct);
        }
    }
}
