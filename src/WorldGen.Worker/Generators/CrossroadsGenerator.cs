using Bridge.Data;
using Microsoft.Extensions.Logging;
using WorldGen.Worker.Services;

namespace WorldGen.Worker.Generators;

/// <summary>
/// Generates the "Crossroads of the World" — a grand hub at world origin (0, 0)
/// that all villages connect to via hub-and-spoke tracks.
/// 61×61 plaza with multi-tier fountain, 4 tree-lined avenues, and 16 radial station slots.
/// </summary>
public sealed class CrossroadsGenerator(RconService rcon, ILogger<CrossroadsGenerator> logger) : ICrossroadsGenerator
{
    private const int BaseY = WorldConstants.BaseY;
    private const int PlazaRadius = WorldConstants.CrossroadsPlazaRadius;
    private const int StationSlots = WorldConstants.CrossroadsStationSlots;

    // Fountain dimensions
    private const int FountainBaseTierHalf = 5;   // 11×11
    private const int FountainSecondTierHalf = 3;  // 7×7
    private const int FountainThirdTierHalf = 1;   // 3×3

    // Avenue dimensions
    private const int AvenueHalfWidth = 2;  // 5 blocks wide
    private const int AvenueLength = 30;

    // Tree spacing along avenues
    private const int TreeSpacing = 8;

    public async Task GenerateAsync(CancellationToken ct)
    {
        const int cx = 0;
        const int cz = 0;

        logger.LogInformation("=== Generating Crossroads of the World at origin (0, 0) ===");

        // Forceload all chunks covering the build area (plaza + avenues)
        int radius = PlazaRadius + AvenueLength + 5;
        int minChunkX = (cx - radius) >> 4;
        int maxChunkX = (cx + radius) >> 4;
        int minChunkZ = (cz - radius) >> 4;
        int maxChunkZ = (cz + radius) >> 4;
        await rcon.SendCommandAsync(
            $"forceload add {minChunkX << 4} {minChunkZ << 4} {maxChunkX << 4} {maxChunkZ << 4}", ct);

        // Build order: Foundation → Structures → Details → Lighting → Signs
        await GeneratePlazaAsync(cx, cz, ct);
        await GenerateFountainAsync(cx, cz, ct);
        await GenerateAvenuesAsync(cx, cz, ct);
        await GenerateStationSlotsAsync(cx, cz, ct);
        await GenerateWelcomeSignsAsync(cx, cz, ct);
        await GenerateDecorativeBannersAsync(cx, cz, ct);

        // Set world spawn at the fountain top
        await rcon.SendCommandAsync("setworldspawn 0 -59 0", ct);
        logger.LogInformation("World spawn set to (0, -59, 0)");

        // Release forceloaded chunks
        await rcon.SendCommandAsync(
            $"forceload remove {minChunkX << 4} {minChunkZ << 4} {maxChunkX << 4} {maxChunkZ << 4}", ct);

        logger.LogInformation("=== Crossroads of the World generation complete ===");
    }

    /// <summary>
    /// 61×61 grand plaza with alternating stone brick + polished andesite checkerboard.
    /// </summary>
    private async Task GeneratePlazaAsync(int cx, int cz, CancellationToken ct)
    {
        logger.LogInformation("Generating 61x61 checkerboard plaza at y={BaseY}", BaseY);

        // Lay base layer of stone bricks first, then overlay checkerboard pattern
        await rcon.SendFillAsync(
            cx - PlazaRadius, BaseY, cz - PlazaRadius,
            cx + PlazaRadius, BaseY, cz + PlazaRadius,
            "minecraft:stone_bricks", ct);

        // Overlay polished andesite in checkerboard pattern
        // Place andesite on every other block where (x + z) is odd
        for (int x = cx - PlazaRadius; x <= cx + PlazaRadius; x++)
        {
            for (int z = cz - PlazaRadius; z <= cz + PlazaRadius; z++)
            {
                if ((x + z) % 2 != 0)
                {
                    await rcon.SendSetBlockAsync(x, BaseY, z, "minecraft:polished_andesite", ct);
                }
            }
        }
    }

    /// <summary>
    /// 15×15 multi-tier fountain at dead center with water cascading down.
    /// </summary>
    private async Task GenerateFountainAsync(int cx, int cz, CancellationToken ct)
    {
        logger.LogInformation("Generating multi-tier fountain at center");

        // Base tier: 11×11 stone bricks at Y=-60
        await rcon.SendFillAsync(
            cx - FountainBaseTierHalf, BaseY, cz - FountainBaseTierHalf,
            cx + FountainBaseTierHalf, BaseY, cz + FountainBaseTierHalf,
            "minecraft:stone_bricks", ct);

        // Base tier raised rim at Y=-59 (outer ring only)
        await rcon.SendFillAsync(
            cx - FountainBaseTierHalf, BaseY + 1, cz - FountainBaseTierHalf,
            cx + FountainBaseTierHalf, BaseY + 1, cz + FountainBaseTierHalf,
            "minecraft:stone_bricks", ct);
        // Hollow out inside of base rim for water pool
        await rcon.SendFillAsync(
            cx - FountainBaseTierHalf + 1, BaseY + 1, cz - FountainBaseTierHalf + 1,
            cx + FountainBaseTierHalf - 1, BaseY + 1, cz + FountainBaseTierHalf - 1,
            "minecraft:water", ct);

        // Second tier: 7×7 stone bricks at Y=-59
        await rcon.SendFillAsync(
            cx - FountainSecondTierHalf, BaseY + 1, cz - FountainSecondTierHalf,
            cx + FountainSecondTierHalf, BaseY + 1, cz + FountainSecondTierHalf,
            "minecraft:stone_bricks", ct);

        // Second tier raised rim at Y=-58
        await rcon.SendFillAsync(
            cx - FountainSecondTierHalf, BaseY + 2, cz - FountainSecondTierHalf,
            cx + FountainSecondTierHalf, BaseY + 2, cz + FountainSecondTierHalf,
            "minecraft:stone_bricks", ct);
        // Hollow out inside of second tier rim for water
        await rcon.SendFillAsync(
            cx - FountainSecondTierHalf + 1, BaseY + 2, cz - FountainSecondTierHalf + 1,
            cx + FountainSecondTierHalf - 1, BaseY + 2, cz + FountainSecondTierHalf - 1,
            "minecraft:water", ct);

        // Third tier: 3×3 stone bricks at Y=-58
        await rcon.SendFillAsync(
            cx - FountainThirdTierHalf, BaseY + 2, cz - FountainThirdTierHalf,
            cx + FountainThirdTierHalf, BaseY + 2, cz + FountainThirdTierHalf,
            "minecraft:stone_bricks", ct);

        // Third tier pillar at Y=-57
        await rcon.SendFillAsync(
            cx - FountainThirdTierHalf, BaseY + 3, cz - FountainThirdTierHalf,
            cx + FountainThirdTierHalf, BaseY + 3, cz + FountainThirdTierHalf,
            "minecraft:stone_bricks", ct);
        // Water cascading from top tier
        await rcon.SendSetBlockAsync(cx + FountainThirdTierHalf + 1, BaseY + 3, cz, "minecraft:water", ct);
        await rcon.SendSetBlockAsync(cx - FountainThirdTierHalf - 1, BaseY + 3, cz, "minecraft:water", ct);
        await rcon.SendSetBlockAsync(cx, BaseY + 3, cz + FountainThirdTierHalf + 1, "minecraft:water", ct);
        await rcon.SendSetBlockAsync(cx, BaseY + 3, cz - FountainThirdTierHalf - 1, "minecraft:water", ct);

        // Glowstone cap at Y=-57
        await rcon.SendSetBlockAsync(cx, BaseY + 3, cz, "minecraft:glowstone", ct);
    }

    /// <summary>
    /// 4 tree-lined avenues extending from the plaza along cardinal directions.
    /// 5 blocks wide, 30 blocks long, with trees, lanterns, benches, and flower beds.
    /// </summary>
    private async Task GenerateAvenuesAsync(int cx, int cz, CancellationToken ct)
    {
        logger.LogInformation("Generating 4 tree-lined avenues");

        // Cardinal direction offsets: (dx, dz) for N, S, E, W
        var directions = new (int dx, int dz, string name, string stairFacingLeft, string stairFacingRight)[]
        {
            (0, -1, "North", "facing=east", "facing=west"),
            (0, 1, "South", "facing=west", "facing=east"),
            (1, 0, "East", "facing=south", "facing=north"),
            (-1, 0, "West", "facing=north", "facing=south"),
        };

        foreach (var (dx, dz, name, stairLeft, stairRight) in directions)
        {
            logger.LogInformation("Building {Direction} avenue", name);
            await GenerateSingleAvenueAsync(cx, cz, dx, dz, stairLeft, stairRight, ct);
        }
    }

    private async Task GenerateSingleAvenueAsync(int cx, int cz, int dx, int dz,
        string stairFacingLeft, string stairFacingRight, CancellationToken ct)
    {
        // Avenue path: 5 wide, 30 long extending from plaza edge
        int startOffset = PlazaRadius + 1;
        int endOffset = PlazaRadius + AvenueLength;

        // Lay the avenue path
        for (int i = startOffset; i <= endOffset; i++)
        {
            int ax = cx + dx * i;
            int az = cz + dz * i;

            if (dx != 0) // E-W avenue: path extends along X, width along Z
            {
                await rcon.SendFillAsync(
                    ax, BaseY, az - AvenueHalfWidth,
                    ax, BaseY, az + AvenueHalfWidth,
                    "minecraft:stone_bricks", ct);
            }
            else // N-S avenue: path extends along Z, width along X
            {
                await rcon.SendFillAsync(
                    ax - AvenueHalfWidth, BaseY, az,
                    ax + AvenueHalfWidth, BaseY, az,
                    "minecraft:stone_bricks", ct);
            }
        }

        // Trees, lanterns, benches, and flower beds every TreeSpacing blocks
        for (int i = startOffset + 4; i <= endOffset - 2; i += TreeSpacing)
        {
            int ax = cx + dx * i;
            int az = cz + dz * i;

            // Tree positions on both sides of the avenue
            int treeOffset = AvenueHalfWidth + 1;

            if (dx != 0) // E-W avenue
            {
                // Left side tree (negative Z side)
                await GenerateTreeAsync(ax, az - treeOffset, ct);
                await GenerateFlowerBedAsync(ax, az - treeOffset, ct);
                // Right side tree (positive Z side)
                await GenerateTreeAsync(ax, az + treeOffset, ct);
                await GenerateFlowerBedAsync(ax, az + treeOffset, ct);

                // Benches facing inward (stairs)
                await rcon.SendSetBlockAsync(ax, BaseY + 1, az - AvenueHalfWidth,
                    $"minecraft:stone_brick_stairs[{stairFacingLeft}]", ct);
                await rcon.SendSetBlockAsync(ax, BaseY + 1, az + AvenueHalfWidth,
                    $"minecraft:stone_brick_stairs[{stairFacingRight}]", ct);
            }
            else // N-S avenue
            {
                // Left side tree (negative X side)
                await GenerateTreeAsync(ax - treeOffset, az, ct);
                await GenerateFlowerBedAsync(ax - treeOffset, az, ct);
                // Right side tree (positive X side)
                await GenerateTreeAsync(ax + treeOffset, az, ct);
                await GenerateFlowerBedAsync(ax + treeOffset, az, ct);

                // Benches facing inward
                await rcon.SendSetBlockAsync(ax - AvenueHalfWidth, BaseY + 1, az,
                    $"minecraft:stone_brick_stairs[{stairFacingLeft}]", ct);
                await rcon.SendSetBlockAsync(ax + AvenueHalfWidth, BaseY + 1, az,
                    $"minecraft:stone_brick_stairs[{stairFacingRight}]", ct);
            }

            // Lanterns on fence posts between trees (halfway between tree positions)
            int lanternOffset = i + TreeSpacing / 2;
            if (lanternOffset <= endOffset - 1)
            {
                int lx = cx + dx * lanternOffset;
                int lz = cz + dz * lanternOffset;

                if (dx != 0)
                {
                    // Fence post + lantern on both sides
                    await rcon.SendSetBlockAsync(lx, BaseY + 1, lz - AvenueHalfWidth,
                        "minecraft:oak_fence", ct);
                    await rcon.SendSetBlockAsync(lx, BaseY + 2, lz - AvenueHalfWidth,
                        "minecraft:lantern[hanging=false]", ct);
                    await rcon.SendSetBlockAsync(lx, BaseY + 1, lz + AvenueHalfWidth,
                        "minecraft:oak_fence", ct);
                    await rcon.SendSetBlockAsync(lx, BaseY + 2, lz + AvenueHalfWidth,
                        "minecraft:lantern[hanging=false]", ct);
                }
                else
                {
                    await rcon.SendSetBlockAsync(lx - AvenueHalfWidth, BaseY + 1, lz,
                        "minecraft:oak_fence", ct);
                    await rcon.SendSetBlockAsync(lx - AvenueHalfWidth, BaseY + 2, lz,
                        "minecraft:lantern[hanging=false]", ct);
                    await rcon.SendSetBlockAsync(lx + AvenueHalfWidth, BaseY + 1, lz,
                        "minecraft:oak_fence", ct);
                    await rcon.SendSetBlockAsync(lx + AvenueHalfWidth, BaseY + 2, lz,
                        "minecraft:lantern[hanging=false]", ct);
                }
            }
        }
    }

    /// <summary>
    /// Simple oak tree: 4-high log trunk with 3×3×2 leaf canopy on top.
    /// </summary>
    private async Task GenerateTreeAsync(int x, int z, CancellationToken ct)
    {
        // Trunk: 4 blocks of oak log
        for (int y = BaseY + 1; y <= BaseY + 4; y++)
        {
            await rcon.SendSetBlockAsync(x, y, z, "minecraft:oak_log", ct);
        }

        // Leaf canopy: 3×3 at Y=-55 and Y=-54
        for (int ly = BaseY + 4; ly <= BaseY + 5; ly++)
        {
            await rcon.SendFillAsync(x - 1, ly, z - 1, x + 1, ly, z + 1, "minecraft:oak_leaves[persistent=true]", ct);
        }

        // Restore trunk through lower canopy
        await rcon.SendSetBlockAsync(x, BaseY + 4, z, "minecraft:oak_log", ct);

        // Top cap leaf
        await rcon.SendSetBlockAsync(x, BaseY + 6, z, "minecraft:oak_leaves[persistent=true]", ct);
    }

    /// <summary>
    /// Flower bed at tree bases: mixed tulips and poppies around the trunk.
    /// </summary>
    private async Task GenerateFlowerBedAsync(int x, int z, CancellationToken ct)
    {
        var flowers = new[] { "minecraft:red_tulip", "minecraft:poppy", "minecraft:orange_tulip", "minecraft:poppy" };
        var offsets = new (int dx, int dz)[] { (1, 0), (-1, 0), (0, 1), (0, -1) };

        for (int i = 0; i < offsets.Length; i++)
        {
            await rcon.SendSetBlockAsync(
                x + offsets[i].dx, BaseY + 1, z + offsets[i].dz,
                flowers[i], ct);
        }
    }

    /// <summary>
    /// 16 radial platform slots evenly distributed around the plaza perimeter.
    /// Each slot: 5×3 platform of stone bricks extending outward, with numbered sign.
    /// </summary>
    private async Task GenerateStationSlotsAsync(int cx, int cz, CancellationToken ct)
    {
        logger.LogInformation("Generating {Count} radial station platform slots", StationSlots);

        for (int slot = 0; slot < StationSlots; slot++)
        {
            double angle = 2.0 * Math.PI * slot / StationSlots;

            // Position on the plaza edge
            int edgeX = cx + (int)Math.Round(PlazaRadius * Math.Cos(angle));
            int edgeZ = cz + (int)Math.Round(PlazaRadius * Math.Sin(angle));

            // Direction outward from center
            int dirX = Math.Sign(edgeX - cx);
            int dirZ = Math.Sign(edgeZ - cz);

            // Determine platform orientation: extend outward from plaza edge
            // Platform is 5 wide (perpendicular to radial) × 3 deep (along radial)
            if (Math.Abs(Math.Cos(angle)) > Math.Abs(Math.Sin(angle)))
            {
                // Primarily E-W direction: platform extends along X, width along Z
                for (int d = 0; d < 3; d++)
                {
                    await rcon.SendFillAsync(
                        edgeX + dirX * d, BaseY, edgeZ - 2,
                        edgeX + dirX * d, BaseY, edgeZ + 2,
                        "minecraft:stone_bricks", ct);
                }

                // Slot number sign on outermost block
                string facing = dirX > 0 ? "rotation=4" : "rotation=12";
                await PlaceSlotSignAsync(edgeX + dirX * 2, edgeZ, facing, slot + 1, ct);
            }
            else
            {
                // Primarily N-S direction: platform extends along Z, width along X
                for (int d = 0; d < 3; d++)
                {
                    await rcon.SendFillAsync(
                        edgeX - 2, BaseY, edgeZ + dirZ * d,
                        edgeX + 2, BaseY, edgeZ + dirZ * d,
                        "minecraft:stone_bricks", ct);
                }

                // Slot number sign
                string facing = dirZ > 0 ? "rotation=0" : "rotation=8";
                await PlaceSlotSignAsync(edgeX, edgeZ + dirZ * 2, facing, slot + 1, ct);
            }
        }
    }

    private async Task PlaceSlotSignAsync(int x, int z, string facing, int slotNumber, CancellationToken ct)
    {
        var slotText = $"\"Slot {slotNumber}\"";
        var stationText = "\"Station\"";
        var emptyText = "\"\"";

        await rcon.SendSetBlockAsync(x, BaseY + 1, z,
            $"minecraft:oak_sign[{facing}]{{front_text:{{messages:['{stationText}','{slotText}','{emptyText}','{emptyText}']}}}}", ct);
    }

    /// <summary>
    /// Welcome signs at each avenue entrance (where avenue meets the plaza edge).
    /// </summary>
    private async Task GenerateWelcomeSignsAsync(int cx, int cz, CancellationToken ct)
    {
        logger.LogInformation("Placing welcome signs at avenue entrances");

        var welcomeText = "\"Crossroads\"";
        var subtitleText = "\"of the World\"";
        var welcomeLine = "\"Welcome!\"";
        var emptyText = "\"\"";

        // North entrance (facing south toward approaching players)
        await rcon.SendSetBlockAsync(cx, BaseY + 1, cz - PlazaRadius - 1, "minecraft:oak_fence", ct);
        await rcon.SendSetBlockAsync(cx, BaseY + 2, cz - PlazaRadius - 1,
            $"minecraft:oak_sign[rotation=8]{{front_text:{{messages:['{welcomeLine}','{welcomeText}','{subtitleText}','{emptyText}']}}}}", ct);

        // South entrance (facing north)
        await rcon.SendSetBlockAsync(cx, BaseY + 1, cz + PlazaRadius + 1, "minecraft:oak_fence", ct);
        await rcon.SendSetBlockAsync(cx, BaseY + 2, cz + PlazaRadius + 1,
            $"minecraft:oak_sign[rotation=0]{{front_text:{{messages:['{welcomeLine}','{welcomeText}','{subtitleText}','{emptyText}']}}}}", ct);

        // East entrance (facing west)
        await rcon.SendSetBlockAsync(cx + PlazaRadius + 1, BaseY + 1, cz, "minecraft:oak_fence", ct);
        await rcon.SendSetBlockAsync(cx + PlazaRadius + 1, BaseY + 2, cz,
            $"minecraft:oak_sign[rotation=12]{{front_text:{{messages:['{welcomeLine}','{welcomeText}','{subtitleText}','{emptyText}']}}}}", ct);

        // West entrance (facing east)
        await rcon.SendSetBlockAsync(cx - PlazaRadius - 1, BaseY + 1, cz, "minecraft:oak_fence", ct);
        await rcon.SendSetBlockAsync(cx - PlazaRadius - 1, BaseY + 2, cz,
            $"minecraft:oak_sign[rotation=4]{{front_text:{{messages:['{welcomeLine}','{welcomeText}','{subtitleText}','{emptyText}']}}}}", ct);
    }

    /// <summary>
    /// Decorative banners at key points around the plaza perimeter.
    /// </summary>
    private async Task GenerateDecorativeBannersAsync(int cx, int cz, CancellationToken ct)
    {
        logger.LogInformation("Placing decorative banners");

        // Banners at the four corners of the plaza on fence posts
        var corners = new (int x, int z)[]
        {
            (cx - PlazaRadius, cz - PlazaRadius),
            (cx + PlazaRadius, cz - PlazaRadius),
            (cx - PlazaRadius, cz + PlazaRadius),
            (cx + PlazaRadius, cz + PlazaRadius),
        };

        foreach (var (x, z) in corners)
        {
            await rcon.SendSetBlockAsync(x, BaseY + 1, z, "minecraft:oak_fence", ct);
            await rcon.SendSetBlockAsync(x, BaseY + 2, z, "minecraft:oak_fence", ct);
            await rcon.SendSetBlockAsync(x, BaseY + 3, z, "minecraft:blue_banner", ct);
        }

        // Additional banners at midpoints of each plaza edge
        var midpoints = new (int x, int z)[]
        {
            (cx, cz - PlazaRadius),
            (cx, cz + PlazaRadius),
            (cx - PlazaRadius, cz),
            (cx + PlazaRadius, cz),
        };

        foreach (var (x, z) in midpoints)
        {
            await rcon.SendSetBlockAsync(x, BaseY + 1, z, "minecraft:oak_fence", ct);
            await rcon.SendSetBlockAsync(x, BaseY + 2, z, "minecraft:oak_fence", ct);
            await rcon.SendSetBlockAsync(x, BaseY + 3, z, "minecraft:white_banner", ct);
        }
    }
}
