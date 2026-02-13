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

        // Build order: Foundation → Structures → Details → Lighting → Signs → Amenities
        await GeneratePlazaAsync(cx, cz, ct);
        await GenerateFountainAsync(cx, cz, ct);
        await GenerateAvenuesAsync(cx, cz, ct);
        await GenerateStationSlotsAsync(cx, cz, ct);
        await GenerateWelcomeSignsAsync(cx, cz, ct);
        await GenerateDecorativeBannersAsync(cx, cz, ct);
        await GenerateSpawnPressurePlateAsync(cx, cz, ct);
        await GenerateInfoKioskAsync(cx, cz, ct);

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

        // Overlay polished andesite in alternating-row pattern (decorative stripes)
        // One fill per z-row instead of 1,860 individual setblock calls
        var rowFills = new List<(int x1, int y1, int z1, int x2, int y2, int z2, string block)>();
        for (int z = cz - PlazaRadius; z <= cz + PlazaRadius; z++)
        {
            if ((z % 2 != 0))
            {
                rowFills.Add((cx - PlazaRadius, BaseY, z, cx + PlazaRadius, BaseY, z,
                    "minecraft:polished_andesite"));
            }
        }
        await rcon.SendFillBatchAsync(rowFills, ct);
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

        // Lay the entire avenue path with a single fill command per avenue
        if (dx != 0) // E-W avenue: path extends along X, width along Z
        {
            int x1 = cx + dx * startOffset;
            int x2 = cx + dx * endOffset;
            await rcon.SendFillAsync(
                Math.Min(x1, x2), BaseY, cz - AvenueHalfWidth,
                Math.Max(x1, x2), BaseY, cz + AvenueHalfWidth,
                "minecraft:stone_bricks", ct);
        }
        else // N-S avenue: path extends along Z, width along X
        {
            int z1 = cz + dz * startOffset;
            int z2 = cz + dz * endOffset;
            await rcon.SendFillAsync(
                cx - AvenueHalfWidth, BaseY, Math.Min(z1, z2),
                cx + AvenueHalfWidth, BaseY, Math.Max(z1, z2),
                "minecraft:stone_bricks", ct);
        }

        // Trees, lanterns, benches, and flower beds every TreeSpacing blocks
        // Collect decoration setblocks for batching
        var decorBlocks = new List<(int x, int y, int z, string block)>();
        for (int i = startOffset + 4; i <= endOffset - 2; i += TreeSpacing)
        {
            int ax = cx + dx * i;
            int az = cz + dz * i;

            int treeOffset = AvenueHalfWidth + 1;

            if (dx != 0) // E-W avenue
            {
                await GenerateTreeAsync(ax, az - treeOffset, ct);
                await GenerateTreeAsync(ax, az + treeOffset, ct);
                GenerateFlowerBedBlocks(ax, az - treeOffset, decorBlocks);
                GenerateFlowerBedBlocks(ax, az + treeOffset, decorBlocks);

                decorBlocks.Add((ax, BaseY + 1, az - AvenueHalfWidth,
                    $"minecraft:stone_brick_stairs[{stairFacingLeft}]"));
                decorBlocks.Add((ax, BaseY + 1, az + AvenueHalfWidth,
                    $"minecraft:stone_brick_stairs[{stairFacingRight}]"));
            }
            else // N-S avenue
            {
                await GenerateTreeAsync(ax - treeOffset, az, ct);
                await GenerateTreeAsync(ax + treeOffset, az, ct);
                GenerateFlowerBedBlocks(ax - treeOffset, az, decorBlocks);
                GenerateFlowerBedBlocks(ax + treeOffset, az, decorBlocks);

                decorBlocks.Add((ax - AvenueHalfWidth, BaseY + 1, az,
                    $"minecraft:stone_brick_stairs[{stairFacingLeft}]"));
                decorBlocks.Add((ax + AvenueHalfWidth, BaseY + 1, az,
                    $"minecraft:stone_brick_stairs[{stairFacingRight}]"));
            }

            // Lanterns on fence posts between trees
            int lanternOffset = i + TreeSpacing / 2;
            if (lanternOffset <= endOffset - 1)
            {
                int lx = cx + dx * lanternOffset;
                int lz = cz + dz * lanternOffset;

                if (dx != 0)
                {
                    decorBlocks.Add((lx, BaseY + 1, lz - AvenueHalfWidth, "minecraft:oak_fence"));
                    decorBlocks.Add((lx, BaseY + 2, lz - AvenueHalfWidth, "minecraft:lantern[hanging=false]"));
                    decorBlocks.Add((lx, BaseY + 1, lz + AvenueHalfWidth, "minecraft:oak_fence"));
                    decorBlocks.Add((lx, BaseY + 2, lz + AvenueHalfWidth, "minecraft:lantern[hanging=false]"));
                }
                else
                {
                    decorBlocks.Add((lx - AvenueHalfWidth, BaseY + 1, lz, "minecraft:oak_fence"));
                    decorBlocks.Add((lx - AvenueHalfWidth, BaseY + 2, lz, "minecraft:lantern[hanging=false]"));
                    decorBlocks.Add((lx + AvenueHalfWidth, BaseY + 1, lz, "minecraft:oak_fence"));
                    decorBlocks.Add((lx + AvenueHalfWidth, BaseY + 2, lz, "minecraft:lantern[hanging=false]"));
                }
            }
        }

        if (decorBlocks.Count > 0)
            await rcon.SendSetBlockBatchAsync(decorBlocks, ct);
    }

    /// <summary>
    /// Simple oak tree: 4-high log trunk with 3×3×2 leaf canopy on top.
    /// </summary>
    private async Task GenerateTreeAsync(int x, int z, CancellationToken ct)
    {
        // Trunk: 4-block vertical fill instead of per-Y setblock
        await rcon.SendFillAsync(x, BaseY + 1, z, x, BaseY + 4, z, "minecraft:oak_log", ct);

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
    /// Collects flower bed block positions into a batch list (no RCON calls).
    /// </summary>
    private static void GenerateFlowerBedBlocks(int x, int z, List<(int x, int y, int z, string block)> blocks)
    {
        var flowers = new[] { "minecraft:red_tulip", "minecraft:poppy", "minecraft:orange_tulip", "minecraft:poppy" };
        var offsets = new (int dx, int dz)[] { (1, 0), (-1, 0), (0, 1), (0, -1) };

        for (int i = 0; i < offsets.Length; i++)
        {
            blocks.Add((x + offsets[i].dx, BaseY + 1, z + offsets[i].dz, flowers[i]));
        }
    }

    /// <summary>
    /// 16 radial platform slots evenly distributed around the plaza perimeter.
    /// Each slot: 5×3 platform of stone bricks extending outward, with numbered sign.
    /// </summary>
    private async Task GenerateStationSlotsAsync(int cx, int cz, CancellationToken ct)
    {
        logger.LogInformation("Generating {Count} radial station platform slots", StationSlots);

        var slotFills = new List<(int x1, int y1, int z1, int x2, int y2, int z2, string block)>();
        var slotSigns = new List<(int x, int y, int z, string block)>();

        for (int slot = 0; slot < StationSlots; slot++)
        {
            double angle = 2.0 * Math.PI * slot / StationSlots;

            int edgeX = cx + (int)Math.Round(PlazaRadius * Math.Cos(angle));
            int edgeZ = cz + (int)Math.Round(PlazaRadius * Math.Sin(angle));

            int dirX = Math.Sign(edgeX - cx);
            int dirZ = Math.Sign(edgeZ - cz);

            if (Math.Abs(Math.Cos(angle)) > Math.Abs(Math.Sin(angle)))
            {
                for (int d = 0; d < 3; d++)
                {
                    slotFills.Add((edgeX + dirX * d, BaseY, edgeZ - 2,
                        edgeX + dirX * d, BaseY, edgeZ + 2, "minecraft:stone_bricks"));
                }

                string facing = dirX > 0 ? "rotation=4" : "rotation=12";
                CollectSlotSign(edgeX + dirX * 2, edgeZ, facing, slot + 1, slotSigns);
            }
            else
            {
                for (int d = 0; d < 3; d++)
                {
                    slotFills.Add((edgeX - 2, BaseY, edgeZ + dirZ * d,
                        edgeX + 2, BaseY, edgeZ + dirZ * d, "minecraft:stone_bricks"));
                }

                string facing = dirZ > 0 ? "rotation=0" : "rotation=8";
                CollectSlotSign(edgeX, edgeZ + dirZ * 2, facing, slot + 1, slotSigns);
            }
        }

        await rcon.SendFillBatchAsync(slotFills, ct);
        await rcon.SendSetBlockBatchAsync(slotSigns, ct);
    }

    private static void CollectSlotSign(int x, int z, string facing, int slotNumber,
        List<(int x, int y, int z, string block)> blocks)
    {
        var slotText = $"\"Slot {slotNumber}\"";
        var stationText = "\"Station\"";
        var emptyText = "\"\"";

        blocks.Add((x, BaseY + 1, z,
            $"minecraft:oak_sign[{facing}]{{front_text:{{messages:['{stationText}','{slotText}','{emptyText}','{emptyText}']}}}}"));
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

        var blocks = new List<(int x, int y, int z, string block)>
        {
            (cx, BaseY + 1, cz - PlazaRadius - 1, "minecraft:oak_fence"),
            (cx, BaseY + 2, cz - PlazaRadius - 1,
                $"minecraft:oak_sign[rotation=8]{{front_text:{{messages:['{welcomeLine}','{welcomeText}','{subtitleText}','{emptyText}']}}}}"),
            (cx, BaseY + 1, cz + PlazaRadius + 1, "minecraft:oak_fence"),
            (cx, BaseY + 2, cz + PlazaRadius + 1,
                $"minecraft:oak_sign[rotation=0]{{front_text:{{messages:['{welcomeLine}','{welcomeText}','{subtitleText}','{emptyText}']}}}}"),
            (cx + PlazaRadius + 1, BaseY + 1, cz, "minecraft:oak_fence"),
            (cx + PlazaRadius + 1, BaseY + 2, cz,
                $"minecraft:oak_sign[rotation=12]{{front_text:{{messages:['{welcomeLine}','{welcomeText}','{subtitleText}','{emptyText}']}}}}"),
            (cx - PlazaRadius - 1, BaseY + 1, cz, "minecraft:oak_fence"),
            (cx - PlazaRadius - 1, BaseY + 2, cz,
                $"minecraft:oak_sign[rotation=4]{{front_text:{{messages:['{welcomeLine}','{welcomeText}','{subtitleText}','{emptyText}']}}}}")
        };

        await rcon.SendSetBlockBatchAsync(blocks, ct);
    }

    private async Task GenerateDecorativeBannersAsync(int cx, int cz, CancellationToken ct)
    {
        logger.LogInformation("Placing decorative banners");

        var blocks = new List<(int x, int y, int z, string block)>();

        var corners = new (int x, int z)[]
        {
            (cx - PlazaRadius, cz - PlazaRadius),
            (cx + PlazaRadius, cz - PlazaRadius),
            (cx - PlazaRadius, cz + PlazaRadius),
            (cx + PlazaRadius, cz + PlazaRadius),
        };

        foreach (var (x, z) in corners)
        {
            blocks.Add((x, BaseY + 1, z, "minecraft:oak_fence"));
            blocks.Add((x, BaseY + 2, z, "minecraft:oak_fence"));
            blocks.Add((x, BaseY + 3, z, "minecraft:blue_banner"));
        }

        var midpoints = new (int x, int z)[]
        {
            (cx, cz - PlazaRadius),
            (cx, cz + PlazaRadius),
            (cx - PlazaRadius, cz),
            (cx + PlazaRadius, cz),
        };

        foreach (var (x, z) in midpoints)
        {
            blocks.Add((x, BaseY + 1, z, "minecraft:oak_fence"));
            blocks.Add((x, BaseY + 2, z, "minecraft:oak_fence"));
            blocks.Add((x, BaseY + 3, z, "minecraft:white_banner"));
        }

        await rcon.SendSetBlockBatchAsync(blocks, ct);
    }

    /// <summary>
    /// Places a golden pressure plate at the world spawn point.
    /// Stepping on it triggers the welcome walkthrough via the Paper plugin.
    /// </summary>
    private async Task GenerateSpawnPressurePlateAsync(int cx, int cz, CancellationToken ct)
    {
        logger.LogInformation("Placing golden pressure plate at spawn");
        // Place on top of the fountain base tier, right at the spawn point offset
        // Spawn is at (0, -59, 0), pressure plate goes next to the fountain at an accessible spot
        // Place it 8 blocks south of center — on the plaza, easily reachable from spawn
        await rcon.SendSetBlockAsync(cx, BaseY + 1, cz + 8, "minecraft:light_weighted_pressure_plate", ct);
        // Gold blocks underneath to make it visually distinct
        await rcon.SendSetBlockAsync(cx, BaseY, cz + 8, "minecraft:gold_block", ct);
        // Small gold accent ring
        var accents = new List<(int x, int y, int z, string block)>
        {
            (cx - 1, BaseY, cz + 8, "minecraft:gold_block"),
            (cx + 1, BaseY, cz + 8, "minecraft:gold_block"),
            (cx, BaseY, cz + 7, "minecraft:gold_block"),
            (cx, BaseY, cz + 9, "minecraft:gold_block"),
        };
        await rcon.SendSetBlockBatchAsync(accents, ct);
    }

    /// <summary>
    /// Places a lectern with a written book at the Crossroads plaza — the info kiosk.
    /// The book contains a world guide explaining villages, buildings, navigation, and commands.
    /// </summary>
    private async Task GenerateInfoKioskAsync(int cx, int cz, CancellationToken ct)
    {
        logger.LogInformation("Placing info kiosk lectern at Crossroads plaza");
        // Place lectern 8 blocks east of center, at ground+1
        int kioskX = cx + 8;
        int kioskZ = cz;

        // Lectern on a small quartz platform
        await rcon.SendSetBlockAsync(kioskX, BaseY, kioskZ, "minecraft:quartz_block", ct);
        await rcon.SendSetBlockAsync(kioskX, BaseY + 1, kioskZ, "minecraft:lectern[facing=west,has_book=true]", ct);

        // Give the lectern a written book using the data command.
        // In 1.20.5+, written_book_content pages are raw text components (not single-quoted JSON strings).
        string bookNbt = "{Book:{id:\"minecraft:written_book\",count:1,components:{\"minecraft:written_book_content\":" +
            "{title:\"World Guide\",author:\"Crossroads\",pages:[" +
            "[{text:\"Welcome to the\\nCrossroads of\\nthe World!\\n\\n\",bold:true,color:\"gold\"},{text:\"This guide will\\nhelp you navigate\\nour Discord world.\"}]," +
            "[{text:\"Villages\\n\\n\",bold:true,color:\"green\"},{text:\"Each Discord channel\\ncategory becomes a\\nMinecraft village.\\n\\nVillages have plazas,\\nbuildings, and rail\\nstations.\"}]," +
            "[{text:\"Buildings\\n\\n\",bold:true,color:\"aqua\"},{text:\"Each Discord channel\\nbecomes a building\\nin its village.\\n\\nSigns on buildings\\nshow the channel name.\"}]," +
            "[{text:\"Minecarts\\n\\n\",bold:true,color:\"yellow\"},{text:\"Rail tracks connect\\nevery village to the\\nCrossroads hub.\\n\\nHop in a minecart at\\nany station to travel!\"}]," +
            "[{text:\"/goto Command\\n\\n\",bold:true,color:\"light_purple\"},{text:\"Use /goto <name>\\nto teleport directly\\nto any channel\\nbuilding.\\n\\nExample:\\n/goto general\"}]" +
            "]}}}}";

        await rcon.SendCommandAsync($"data merge block {kioskX} {BaseY + 1} {kioskZ} {bookNbt}", ct);
    }
}
