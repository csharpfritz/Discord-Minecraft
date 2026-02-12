using Microsoft.Extensions.Logging;
using WorldGen.Worker.Models;
using WorldGen.Worker.Services;

namespace WorldGen.Worker.Generators;

public sealed class BuildingGenerator(RconService rcon, ILogger<BuildingGenerator> logger) : IBuildingGenerator
{
    private const int BaseY = 64;
    private const int Footprint = 21;
    private const int HalfFootprint = Footprint / 2; // 10
    private const int Floors = 4;
    private const int FloorHeight = 5; // floor-to-ceiling per story
    private const int WallTop = BaseY + Floors * FloorHeight; // y=84
    private const int RoofY = WallTop + 1; // y=85

    // Carpet colors per floor (different color each floor)
    private static readonly string[] CarpetColors =
    [
        "minecraft:red_carpet",
        "minecraft:blue_carpet",
        "minecraft:green_carpet",
        "minecraft:yellow_carpet"
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
            "Generating building '{Name}' at ({BX}, {BZ}), index {Index} in village at ({CX}, {CZ})",
            request.Name, bx, bz, request.BuildingIndex, cx, cz);

        await GenerateFoundationAsync(bx, bz, ct);
        await GenerateWallsAsync(bx, bz, ct);
        await ClearInteriorAsync(bx, bz, ct);
        await GenerateFloorsAsync(bx, bz, ct);
        await GenerateEntranceAsync(bx, bz, ct);
        await GenerateStairsAsync(bx, bz, ct);
        await GenerateWindowsAsync(bx, bz, ct);
        await GenerateRoofAsync(bx, bz, ct);
        await GenerateLightingAsync(bx, bz, ct);
        await GenerateDecorationAsync(bx, bz, ct);
        await GenerateSignsAsync(bx, bz, request.Name, ct);

        logger.LogInformation("Building '{Name}' generation complete at ({BX}, {BZ})", request.Name, bx, bz);
    }

    /// <summary>21×21 stone brick platform at y=64</summary>
    private async Task GenerateFoundationAsync(int bx, int bz, CancellationToken ct)
    {
        await rcon.SendFillAsync(
            bx - HalfFootprint, BaseY, bz - HalfFootprint,
            bx + HalfFootprint, BaseY, bz + HalfFootprint,
            "minecraft:stone_bricks", ct);
    }

    /// <summary>Stone brick walls from y=65 to y=84, 4 floors × 5 blocks each</summary>
    private async Task GenerateWallsAsync(int bx, int bz, CancellationToken ct)
    {
        int minX = bx - HalfFootprint;
        int maxX = bx + HalfFootprint;
        int minZ = bz - HalfFootprint;
        int maxZ = bz + HalfFootprint;

        // North wall (minZ face)
        await rcon.SendFillAsync(minX, BaseY + 1, minZ, maxX, WallTop, minZ, "minecraft:stone_bricks", ct);
        // South wall (maxZ face)
        await rcon.SendFillAsync(minX, BaseY + 1, maxZ, maxX, WallTop, maxZ, "minecraft:stone_bricks", ct);
        // West wall (minX face)
        await rcon.SendFillAsync(minX, BaseY + 1, minZ, minX, WallTop, maxZ, "minecraft:stone_bricks", ct);
        // East wall (maxX face)
        await rcon.SendFillAsync(maxX, BaseY + 1, minZ, maxX, WallTop, maxZ, "minecraft:stone_bricks", ct);
    }

    /// <summary>Clear air inside each floor (19×19 interior)</summary>
    private async Task ClearInteriorAsync(int bx, int bz, CancellationToken ct)
    {
        await rcon.SendFillAsync(
            bx - HalfFootprint + 1, BaseY + 1, bz - HalfFootprint + 1,
            bx + HalfFootprint - 1, WallTop, bz + HalfFootprint - 1,
            "minecraft:air", ct);
    }

    /// <summary>Oak plank floors at y=69, y=74, y=79 (between stories)</summary>
    private async Task GenerateFloorsAsync(int bx, int bz, CancellationToken ct)
    {
        for (int floor = 1; floor < Floors; floor++)
        {
            int floorY = BaseY + floor * FloorHeight; // y=69, 74, 79
            await rcon.SendFillAsync(
                bx - HalfFootprint + 1, floorY, bz - HalfFootprint + 1,
                bx + HalfFootprint - 1, floorY, bz + HalfFootprint - 1,
                "minecraft:oak_planks", ct);
        }
    }

    /// <summary>3-wide, 3-tall doorway on the south face at ground level (y=65-67)</summary>
    private async Task GenerateEntranceAsync(int bx, int bz, CancellationToken ct)
    {
        int maxZ = bz + HalfFootprint;
        await rcon.SendFillAsync(
            bx - 1, BaseY + 1, maxZ,
            bx + 1, BaseY + 3, maxZ,
            "minecraft:air", ct);
    }

    /// <summary>
    /// Oak stairs in the NE corner, 3-wide switchback staircase connecting all floors.
    /// Stairs run along the east wall (positive X), then switch back along the north wall.
    /// </summary>
    private async Task GenerateStairsAsync(int bx, int bz, CancellationToken ct)
    {
        int stairX = bx + HalfFootprint - 4; // NE corner area, 3 blocks wide
        int stairZ = bz - HalfFootprint + 1; // near north wall

        for (int floor = 0; floor < Floors - 1; floor++)
        {
            int baseFloorY = BaseY + 1 + floor * FloorHeight; // bottom of this story

            // First run: stairs going north (negative Z), 3 wide along X
            for (int step = 0; step < 3; step++)
            {
                int sy = baseFloorY + step;
                int sz = stairZ + 2 - step; // going from south to north
                // 3-wide stair blocks facing north
                for (int dx = 0; dx < 3; dx++)
                {
                    await rcon.SendSetBlockAsync(stairX + dx, sy, sz,
                        "minecraft:oak_stairs[facing=north]", ct);
                }
                // Clear air above each stair step
                await rcon.SendFillAsync(stairX, sy + 1, sz, stairX + 2, sy + 2, sz, "minecraft:air", ct);
            }

            // Landing block (flat platform to switch direction)
            await rcon.SendFillAsync(stairX, baseFloorY + 3, stairZ, stairX + 2, baseFloorY + 3, stairZ, "minecraft:oak_planks", ct);

            // Second run: stairs going west (negative X)
            int landingZ = stairZ;
            for (int step = 0; step < 2; step++)
            {
                int sy = baseFloorY + 4 + step;
                int sx = stairX + 2 - step;
                for (int dz = 0; dz < 1; dz++)
                {
                    await rcon.SendSetBlockAsync(sx, sy, landingZ + dz,
                        "minecraft:oak_stairs[facing=west]", ct);
                }
            }

            // Clear opening in the floor above for stair access
            if (floor < Floors - 2)
            {
                int upperFloorY = BaseY + (floor + 1) * FloorHeight;
                await rcon.SendFillAsync(stairX, upperFloorY, stairZ, stairX + 2, upperFloorY, stairZ + 2, "minecraft:air", ct);
            }
        }
    }

    /// <summary>Glass pane windows: 2-wide, centered on each wall face, each floor</summary>
    private async Task GenerateWindowsAsync(int bx, int bz, CancellationToken ct)
    {
        int minX = bx - HalfFootprint;
        int maxX = bx + HalfFootprint;
        int minZ = bz - HalfFootprint;
        int maxZ = bz + HalfFootprint;

        for (int floor = 0; floor < Floors; floor++)
        {
            int windowY = BaseY + 2 + floor * FloorHeight; // 2 blocks up from floor level

            // North wall windows (centered)
            await rcon.SendFillAsync(bx - 1, windowY, minZ, bx, windowY, minZ, "minecraft:glass_pane", ct);
            // South wall windows (centered, skip entrance area on ground floor)
            if (floor > 0)
            {
                await rcon.SendFillAsync(bx - 1, windowY, maxZ, bx, windowY, maxZ, "minecraft:glass_pane", ct);
            }
            // West wall windows (centered)
            await rcon.SendFillAsync(minX, windowY, bz - 1, minX, windowY, bz, "minecraft:glass_pane", ct);
            // East wall windows (centered)
            await rcon.SendFillAsync(maxX, windowY, bz - 1, maxX, windowY, bz, "minecraft:glass_pane", ct);
        }
    }

    /// <summary>Stone brick slab roof at y=85</summary>
    private async Task GenerateRoofAsync(int bx, int bz, CancellationToken ct)
    {
        await rcon.SendFillAsync(
            bx - HalfFootprint, RoofY, bz - HalfFootprint,
            bx + HalfFootprint, RoofY, bz + HalfFootprint,
            "minecraft:stone_brick_slab", ct);
    }

    /// <summary>Glowstone in ceiling of each floor, every 5 blocks in a grid</summary>
    private async Task GenerateLightingAsync(int bx, int bz, CancellationToken ct)
    {
        for (int floor = 0; floor < Floors; floor++)
        {
            // Ceiling Y for each floor: top of the story minus 1 from the ceiling
            int ceilingY;
            if (floor < Floors - 1)
                ceilingY = BaseY + (floor + 1) * FloorHeight - 1; // block below the next floor slab
            else
                ceilingY = RoofY - 1; // block below the roof slab

            for (int dx = -HalfFootprint + 3; dx <= HalfFootprint - 3; dx += 5)
            {
                for (int dz = -HalfFootprint + 3; dz <= HalfFootprint - 3; dz += 5)
                {
                    await rcon.SendSetBlockAsync(bx + dx, ceilingY, bz + dz, "minecraft:glowstone", ct);
                }
            }
        }
    }

    /// <summary>Carpet border pattern around each floor's perimeter, different color per floor</summary>
    private async Task GenerateDecorationAsync(int bx, int bz, CancellationToken ct)
    {
        for (int floor = 0; floor < Floors; floor++)
        {
            int carpetY = BaseY + 1 + floor * FloorHeight; // on top of the floor surface
            string carpet = CarpetColors[floor % CarpetColors.Length];

            int innerMin = -HalfFootprint + 1;
            int innerMax = HalfFootprint - 1;

            // North and south edges
            await rcon.SendFillAsync(
                bx + innerMin, carpetY, bz + innerMin,
                bx + innerMax, carpetY, bz + innerMin,
                carpet, ct);
            await rcon.SendFillAsync(
                bx + innerMin, carpetY, bz + innerMax,
                bx + innerMax, carpetY, bz + innerMax,
                carpet, ct);

            // West and east edges (excluding corners already placed)
            await rcon.SendFillAsync(
                bx + innerMin, carpetY, bz + innerMin + 1,
                bx + innerMin, carpetY, bz + innerMax - 1,
                carpet, ct);
            await rcon.SendFillAsync(
                bx + innerMax, carpetY, bz + innerMin + 1,
                bx + innerMax, carpetY, bz + innerMax - 1,
                carpet, ct);
        }
    }

    /// <summary>Signs: entrance sign with channel name, floor signs inside each floor</summary>
    private async Task GenerateSignsAsync(int bx, int bz, string channelName, CancellationToken ct)
    {
        var truncatedName = channelName.Length > 15 ? channelName[..15] : channelName;
        var nameText = $"{{\"text\":\"#{truncatedName}\"}}";

        // Entrance sign on south face (outside, above doorway)
        int maxZ = bz + HalfFootprint;
        await rcon.SendSetBlockAsync(bx, BaseY + 4, maxZ,
            $"minecraft:oak_wall_sign[facing=south]{{front_text:{{messages:['\"\"','{nameText}','\"\"','\"\"']}}}}", ct);

        // Floor signs inside each floor (on the south interior wall, near entrance)
        for (int floor = 0; floor < Floors; floor++)
        {
            int signY = BaseY + 2 + floor * FloorHeight;
            int signZ = bz + HalfFootprint - 1; // interior south wall

            var floorLabel = $"{{\"text\":\"Floor {floor + 1}\"}}";
            var channelLabel = $"{{\"text\":\"#{truncatedName}\"}}";

            await rcon.SendSetBlockAsync(bx, signY, signZ,
                $"minecraft:oak_wall_sign[facing=north]{{front_text:{{messages:['\"\"','{floorLabel}','{channelLabel}','\"\"']}}}}", ct);
        }
    }
}
