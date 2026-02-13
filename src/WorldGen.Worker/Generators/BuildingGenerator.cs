using Microsoft.Extensions.Logging;
using WorldGen.Worker.Models;
using WorldGen.Worker.Services;

namespace WorldGen.Worker.Generators;

/// <summary>
/// Generates buildings in one of three architectural styles via RCON commands.
/// Style is selected deterministically from the channel ID.
/// All styles share a 21×21 footprint, 2 floors, and south-facing entrance.
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

    private static readonly string[] StyleNames = ["Medieval Castle", "Timber Cottage", "Stone Watchtower"];

    public async Task GenerateAsync(BuildingGenerationRequest request, CancellationToken ct)
    {
        var cx = request.VillageCenterX;
        var cz = request.VillageCenterZ;

        // Deterministic style selection from channel ID
        var style = (BuildingStyle)(Math.Abs(request.ChannelId % 3));

        int row = request.BuildingIndex % 2; // 0 = north row, 1 = south row
        int positionInRow = request.BuildingIndex / 2;

        int bx = cx + (positionInRow - 3) * BuildingSpacing;
        int bz = row == 0 ? cz - RowOffset : cz + RowOffset;

        logger.LogInformation(
            "Generating {Style} '{Name}' at ({BX}, {BZ}), index {Index} in village at ({CX}, {CZ})",
            StyleNames[(int)style], request.Name, bx, bz, request.BuildingIndex, cx, cz);

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

        // Walkway is shared across all styles
        await GenerateWalkwayAsync(cx, cz, bx, bz, ct);

        // Dispatch to style-specific generation
        switch (style)
        {
            case BuildingStyle.MedievalCastle:
                await GenerateMedievalCastleAsync(bx, bz, request.Name, request.ChannelTopic, ct);
                break;
            case BuildingStyle.TimberCottage:
                await GenerateTimberCottageAsync(bx, bz, request.Name, request.ChannelTopic, ct);
                break;
            case BuildingStyle.StoneWatchtower:
                await GenerateStoneWatchtowerAsync(bx, bz, request.Name, request.ChannelTopic, ct);
                break;
        }

        // Release forceloaded chunks
        await rcon.SendCommandAsync($"forceload remove {minChunkX << 4} {minChunkZ << 4} {maxChunkX << 4} {maxChunkZ << 4}", ct);

        logger.LogInformation("{Style} '{Name}' generation complete at ({BX}, {BZ})",
            StyleNames[(int)style], request.Name, bx, bz);
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

    // ═══════════════════════════════════════════════════════════════
    //  Shared helpers
    // ═══════════════════════════════════════════════════════════════

    /// <summary>21×21 foundation slab at surface level</summary>
    private async Task GenerateFoundationAsync(int bx, int bz, string block, CancellationToken ct)
    {
        await rcon.SendFillAsync(
            bx - HalfFootprint, BaseY, bz - HalfFootprint,
            bx + HalfFootprint, BaseY, bz + HalfFootprint,
            block, ct);
    }

    /// <summary>Clear air inside the walls (19×19 interior, full height)</summary>
    private async Task ClearInteriorAsync(int bx, int bz, CancellationToken ct)
    {
        await rcon.SendFillAsync(
            bx - HalfFootprint + 1, BaseY + 1, bz - HalfFootprint + 1,
            bx + HalfFootprint - 1, WallTop, bz + HalfFootprint - 1,
            "minecraft:air", ct);
    }

    /// <summary>Floor between stories (only 1 intermediate floor for 2-story building)</summary>
    private async Task GenerateFloorsAsync(int bx, int bz, string block, CancellationToken ct)
    {
        int floorY = BaseY + FloorHeight;
        await rcon.SendFillAsync(
            bx - HalfFootprint + 1, floorY, bz - HalfFootprint + 1,
            bx + HalfFootprint - 1, floorY, bz + HalfFootprint - 1,
            block, ct);
    }

    /// <summary>
    /// Signs placed LAST so they attach to solid wall blocks.
    /// Entrance sign OUTSIDE above doorway (facing south toward approaching players),
    /// channel name sign inside entrance, and floor label signs on each floor.
    /// </summary>
    private async Task GenerateSignsAsync(int bx, int bz, string channelName, string styleLabel, CancellationToken ct)
    {
        var truncatedName = channelName.Length > 15 ? channelName[..15] : channelName;

        // Minecraft 1.20+ signs need plain text in double quotes, not JSON objects
        var nameText = $"\"#{truncatedName}\"";
        var labelText = $"\"{styleLabel}\"";
        var emptyText = "\"\"";

        int maxZ = bz + HalfFootprint;

        // Exterior entrance sign — OUTSIDE the building, above the doorway, facing SOUTH
        await rcon.SendSetBlockAsync(bx, BaseY + 5, maxZ + 1,
            $"minecraft:oak_wall_sign[facing=south]{{front_text:{{messages:['{labelText}','{nameText}',{emptyText},{emptyText}]}}}}", ct);

        // Floor signs on each level — on south interior wall
        int interiorSignZ = bz + HalfFootprint - 1;
        for (int floor = 0; floor < Floors; floor++)
        {
            int signY = BaseY + 2 + floor * FloorHeight;
            int signX = bx + 3;

            var floorLabel = $"\"Floor {floor + 1}\"";
            var channelLabel = $"\"#{truncatedName}\"";

            await rcon.SendSetBlockAsync(signX, signY, interiorSignZ,
                $"minecraft:oak_wall_sign[facing=north]{{front_text:{{messages:[{emptyText},'{floorLabel}','{channelLabel}',{emptyText}]}}}}", ct);
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  Medieval Castle (original style)
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Full medieval castle generation: cobblestone walls, stone brick trim,
    /// corner turrets, crenellated parapet, arrow slit windows, 3-wide staircase.
    /// </summary>
    private async Task GenerateMedievalCastleAsync(int bx, int bz, string channelName, string? channelTopic, CancellationToken ct)
    {
        await GenerateFoundationAsync(bx, bz, "minecraft:cobblestone", ct);
        await GenerateCastleWallsAsync(bx, bz, ct);
        await GenerateCornerTurretsAsync(bx, bz, ct);
        await ClearInteriorAsync(bx, bz, ct);
        await GenerateFloorsAsync(bx, bz, "minecraft:oak_planks", ct);
        await GenerateCastleStairsAsync(bx, bz, ct);
        await GenerateCastleRoofAndParapetAsync(bx, bz, ct);
        await GenerateArrowSlitsAsync(bx, bz, ct);
        await GenerateCastleEntranceAsync(bx, bz, ct);
        await GenerateCastleLightingAsync(bx, bz, ct);
        await GenerateSignsAsync(bx, bz, channelName, "Castle Keep", ct);
        await GenerateCastleInteriorAsync(bx, bz, channelTopic, ct);
    }

    /// <summary>
    /// Cobblestone walls with stone brick trim at top and bottom courses.
    /// </summary>
    private async Task GenerateCastleWallsAsync(int bx, int bz, CancellationToken ct)
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
    /// </summary>
    private async Task GenerateCornerTurretsAsync(int bx, int bz, CancellationToken ct)
    {
        var slabBlocks = new List<(int x, int y, int z, string block)>();
        foreach (var (dx, dz) in TurretOffsets)
        {
            int tx = bx + dx;
            int tz = bz + dz;

            // Vertical fill for entire turret pillar instead of per-Y setblock
            await rcon.SendFillAsync(tx, BaseY + 1, tz, tx, WallTop + 1, tz,
                "minecraft:oak_log[axis=y]", ct);

            slabBlocks.Add((tx, WallTop + 2, tz, "minecraft:stone_brick_slab"));
        }
        await rcon.SendSetBlockBatchAsync(slabBlocks, ct);
    }

    /// <summary>
    /// 3-wide oak staircase in the NE corner with landing between floors.
    /// </summary>
    private async Task GenerateCastleStairsAsync(int bx, int bz, CancellationToken ct)
    {
        int stairX = bx + HalfFootprint - 5;
        int stairZ = bz - HalfFootprint + 1;
        int baseFloorY = BaseY + 1;

        int upperFloorY = BaseY + FloorHeight;
        await rcon.SendFillAsync(stairX, upperFloorY, stairZ,
            stairX + 2, upperFloorY, stairZ + 4, "minecraft:air", ct);

        for (int step = 0; step < 4; step++)
        {
            int sy = baseFloorY + step;
            int sz = stairZ + 4 - step;
            // Fill entire 3-wide step row at once
            await rcon.SendFillAsync(stairX, sy, sz, stairX + 2, sy, sz,
                "minecraft:oak_stairs[facing=north]", ct);
        }

        await rcon.SendFillAsync(stairX, baseFloorY + 4, stairZ,
            stairX + 2, baseFloorY + 4, stairZ + 1,
            "minecraft:oak_planks", ct);
    }

    /// <summary>
    /// Stone brick roof slab with crenellated parapet (alternating stone/air merlons).
    /// Fill full parapet edges, then batch-clear alternating positions.
    /// </summary>
    private async Task GenerateCastleRoofAndParapetAsync(int bx, int bz, CancellationToken ct)
    {
        int minX = bx - HalfFootprint;
        int maxX = bx + HalfFootprint;
        int minZ = bz - HalfFootprint;
        int maxZ = bz + HalfFootprint;

        // Solid roof slab
        await rcon.SendFillAsync(minX, RoofY, minZ, maxX, RoofY, maxZ,
            "minecraft:stone_brick_slab", ct);

        // Crenellated parapet — fill full edges, then clear alternating blocks
        await rcon.SendFillAsync(minX, RoofY + 1, minZ, maxX, RoofY + 1, minZ, "minecraft:stone_bricks", ct);
        await rcon.SendFillAsync(minX, RoofY + 1, maxZ, maxX, RoofY + 1, maxZ, "minecraft:stone_bricks", ct);
        await rcon.SendFillAsync(minX, RoofY + 1, minZ + 1, minX, RoofY + 1, maxZ - 1, "minecraft:stone_bricks", ct);
        await rcon.SendFillAsync(maxX, RoofY + 1, minZ + 1, maxX, RoofY + 1, maxZ - 1, "minecraft:stone_bricks", ct);

        // Clear alternating positions (every other block) for crenellation pattern
        var crenelClears = new List<(int x, int y, int z, string block)>();
        for (int x = minX + 1; x <= maxX; x += 2)
        {
            crenelClears.Add((x, RoofY + 1, minZ, "minecraft:air"));
            crenelClears.Add((x, RoofY + 1, maxZ, "minecraft:air"));
        }
        for (int z = minZ + 1; z <= maxZ - 1; z += 2)
        {
            crenelClears.Add((minX, RoofY + 1, z, "minecraft:air"));
            crenelClears.Add((maxX, RoofY + 1, z, "minecraft:air"));
        }
        await rcon.SendSetBlockBatchAsync(crenelClears, ct);
    }

    /// <summary>
    /// Arrow slit windows: 1-wide, 2-tall air gaps in walls on each face per floor.
    /// </summary>
    private async Task GenerateArrowSlitsAsync(int bx, int bz, CancellationToken ct)
    {
        int minX = bx - HalfFootprint;
        int maxX = bx + HalfFootprint;
        int minZ = bz - HalfFootprint;
        int maxZ = bz + HalfFootprint;

        int[] windowOffsets = [-6, -3, 3, 6];

        for (int floor = 0; floor < Floors; floor++)
        {
            int windowBaseY = BaseY + 2 + floor * FloorHeight;

            foreach (int offset in windowOffsets)
            {
                await rcon.SendFillAsync(bx + offset, windowBaseY, minZ,
                    bx + offset, windowBaseY + 1, minZ, "minecraft:air", ct);

                if (floor > 0 || (offset != -3 && offset != 3))
                {
                    await rcon.SendFillAsync(bx + offset, windowBaseY, maxZ,
                        bx + offset, windowBaseY + 1, maxZ, "minecraft:air", ct);
                }

                await rcon.SendFillAsync(minX, windowBaseY, bz + offset,
                    minX, windowBaseY + 1, bz + offset, "minecraft:air", ct);

                await rcon.SendFillAsync(maxX, windowBaseY, bz + offset,
                    maxX, windowBaseY + 1, bz + offset, "minecraft:air", ct);
            }
        }
    }

    /// <summary>
    /// 3-wide, 4-tall arched doorway on the south face with stone brick arch.
    /// </summary>
    private async Task GenerateCastleEntranceAsync(int bx, int bz, CancellationToken ct)
    {
        int maxZ = bz + HalfFootprint;

        await rcon.SendFillAsync(bx - 1, BaseY + 1, maxZ,
            bx + 1, BaseY + 4, maxZ, "minecraft:air", ct);

        await rcon.SendSetBlockAsync(bx - 1, BaseY + 4, maxZ, "minecraft:stone_bricks", ct);
        await rcon.SendSetBlockAsync(bx + 1, BaseY + 4, maxZ, "minecraft:stone_bricks", ct);
    }

    /// <summary>
    /// Wall-mounted torches for lighting on interior walls, spaced every 4 blocks.
    /// </summary>
    private async Task GenerateCastleLightingAsync(int bx, int bz, CancellationToken ct)
    {
        int minZ = bz - HalfFootprint + 1;
        int maxZ = bz + HalfFootprint - 1;

        var torches = new List<(int x, int y, int z, string block)>();

        for (int floor = 0; floor < Floors; floor++)
        {
            int torchY = BaseY + 3 + floor * FloorHeight;

            for (int x = bx - 8; x <= bx + 8; x += 4)
                torches.Add((x, torchY, minZ, "minecraft:wall_torch[facing=south]"));

            for (int x = bx - 8; x <= bx + 8; x += 4)
            {
                if (floor == 0 && x >= bx - 1 && x <= bx + 1)
                    continue;
                torches.Add((x, torchY, maxZ, "minecraft:wall_torch[facing=north]"));
            }

            int minX = bx - HalfFootprint + 1;
            int maxX = bx + HalfFootprint - 1;

            for (int z = bz - 8; z <= bz + 8; z += 4)
                torches.Add((minX, torchY, z, "minecraft:wall_torch[facing=east]"));

            for (int z = bz - 8; z <= bz + 8; z += 4)
                torches.Add((maxX, torchY, z, "minecraft:wall_torch[facing=west]"));
        }

        // Entrance torches
        int entranceZ = bz + HalfFootprint;
        torches.Add((bx - 2, BaseY + 3, entranceZ, "minecraft:wall_torch[facing=south]"));
        torches.Add((bx + 2, BaseY + 3, entranceZ, "minecraft:wall_torch[facing=south]"));

        await rcon.SendSetBlockBatchAsync(torches, ct);
    }

    // ═══════════════════════════════════════════════════════════════
    //  Timber Cottage
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Warm timber-frame cottage with oak log posts, birch plank infill,
    /// peaked A-frame dark oak roof, 2×2 glass pane windows, hanging lanterns.
    /// </summary>
    private async Task GenerateTimberCottageAsync(int bx, int bz, string channelName, string? channelTopic, CancellationToken ct)
    {
        await GenerateFoundationAsync(bx, bz, "minecraft:oak_planks", ct);
        await GenerateCottageWallsAsync(bx, bz, ct);
        await ClearInteriorAsync(bx, bz, ct);
        await GenerateFloorsAsync(bx, bz, "minecraft:oak_planks", ct);
        await GenerateCottageStairsAsync(bx, bz, ct);
        await GenerateCottageRoofAsync(bx, bz, ct);
        await GenerateCottageWindowsAsync(bx, bz, ct);
        await GenerateCottageEntranceAsync(bx, bz, ct);
        await GenerateCottageInteriorAsync(bx, bz, channelTopic, ct);
        await GenerateCottageLightingAsync(bx, bz, ct);
        await GenerateSignsAsync(bx, bz, channelName, "Cottage", ct);
    }

    /// <summary>
    /// Oak log frame posts at corners and midpoints, birch plank infill.
    /// </summary>
    private async Task GenerateCottageWallsAsync(int bx, int bz, CancellationToken ct)
    {
        int minX = bx - HalfFootprint;
        int maxX = bx + HalfFootprint;
        int minZ = bz - HalfFootprint;
        int maxZ = bz + HalfFootprint;

        // Birch plank infill walls (all 4 faces)
        await rcon.SendFillAsync(minX, BaseY + 1, minZ, maxX, WallTop, minZ, "minecraft:birch_planks", ct);
        await rcon.SendFillAsync(minX, BaseY + 1, maxZ, maxX, WallTop, maxZ, "minecraft:birch_planks", ct);
        await rcon.SendFillAsync(minX, BaseY + 1, minZ, minX, WallTop, maxZ, "minecraft:birch_planks", ct);
        await rcon.SendFillAsync(maxX, BaseY + 1, minZ, maxX, WallTop, maxZ, "minecraft:birch_planks", ct);

        // Oak log frame posts at corners and midpoints — vertical fills
        int[] xPosts = [minX, bx, maxX];
        int[] zPosts = [minZ, bz, maxZ];

        var postFills = new List<(int x1, int y1, int z1, int x2, int y2, int z2, string block)>();
        foreach (int px in xPosts)
        {
            postFills.Add((px, BaseY + 1, minZ, px, WallTop, minZ, "minecraft:oak_log"));
            postFills.Add((px, BaseY + 1, maxZ, px, WallTop, maxZ, "minecraft:oak_log"));
        }
        foreach (int pz in zPosts)
        {
            if (pz == minZ || pz == maxZ) continue;
            postFills.Add((minX, BaseY + 1, pz, minX, WallTop, pz, "minecraft:oak_log"));
            postFills.Add((maxX, BaseY + 1, pz, maxX, WallTop, pz, "minecraft:oak_log"));
        }
        await rcon.SendFillBatchAsync(postFills, ct);
    }

    /// <summary>3-wide oak staircase for the cottage</summary>
    private async Task GenerateCottageStairsAsync(int bx, int bz, CancellationToken ct)
    {
        int stairX = bx + HalfFootprint - 5;
        int stairZ = bz - HalfFootprint + 1;
        int baseFloorY = BaseY + 1;

        int upperFloorY = BaseY + FloorHeight;
        await rcon.SendFillAsync(stairX, upperFloorY, stairZ,
            stairX + 2, upperFloorY, stairZ + 4, "minecraft:air", ct);

        for (int step = 0; step < 4; step++)
        {
            int sy = baseFloorY + step;
            int sz = stairZ + 4 - step;
            await rcon.SendFillAsync(stairX, sy, sz, stairX + 2, sy, sz,
                "minecraft:oak_stairs[facing=north]", ct);
        }

        await rcon.SendFillAsync(stairX, baseFloorY + 4, stairZ,
            stairX + 2, baseFloorY + 4, stairZ + 1,
            "minecraft:oak_planks", ct);
    }

    /// <summary>
    /// Peaked A-frame roof using dark oak stairs. Ridge runs east-west.
    /// 1-block overhang on north and south sides.
    /// </summary>
    private async Task GenerateCottageRoofAsync(int bx, int bz, CancellationToken ct)
    {
        int minX = bx - HalfFootprint;
        int maxX = bx + HalfFootprint;
        int minZ = bz - HalfFootprint;
        int maxZ = bz + HalfFootprint;

        // A-frame peaked roof stepping inward from north/south
        for (int layer = 0; layer <= HalfFootprint; layer++)
        {
            int roofY = RoofY + layer;
            int northZ = minZ - 1 + layer; // overhang 1 block
            int southZ = maxZ + 1 - layer;

            if (northZ > bz || southZ < bz) break;

            if (northZ == southZ)
            {
                // Ridge line
                await rcon.SendFillAsync(minX, roofY, northZ, maxX, roofY, northZ,
                    "minecraft:dark_oak_slab[type=bottom]", ct);
            }
            else
            {
                // North slope
                await rcon.SendFillAsync(minX, roofY, northZ, maxX, roofY, northZ,
                    "minecraft:dark_oak_stairs[facing=south]", ct);
                // South slope
                await rcon.SendFillAsync(minX, roofY, southZ, maxX, roofY, southZ,
                    "minecraft:dark_oak_stairs[facing=north]", ct);
            }
        }
    }

    /// <summary>
    /// 2×2 glass pane windows, 3 per wall per floor. Flower boxes under ground floor windows.
    /// </summary>
    private async Task GenerateCottageWindowsAsync(int bx, int bz, CancellationToken ct)
    {
        int minX = bx - HalfFootprint;
        int maxX = bx + HalfFootprint;
        int minZ = bz - HalfFootprint;
        int maxZ = bz + HalfFootprint;

        int[] windowOffsets = [-7, 0, 7]; // 3 windows per wall

        for (int floor = 0; floor < Floors; floor++)
        {
            int windowBaseY = BaseY + 2 + floor * FloorHeight;

            foreach (int offset in windowOffsets)
            {
                // North wall (2×2 glass panes)
                await rcon.SendFillAsync(bx + offset, windowBaseY, minZ,
                    bx + offset + 1, windowBaseY + 1, minZ, "minecraft:glass_pane", ct);

                // South wall (skip center on ground floor for entrance)
                if (floor > 0 || offset != 0)
                {
                    await rcon.SendFillAsync(bx + offset, windowBaseY, maxZ,
                        bx + offset + 1, windowBaseY + 1, maxZ, "minecraft:glass_pane", ct);
                }

                // West wall
                await rcon.SendFillAsync(minX, windowBaseY, bz + offset,
                    minX, windowBaseY + 1, bz + offset + 1, "minecraft:glass_pane", ct);

                // East wall
                await rcon.SendFillAsync(maxX, windowBaseY, bz + offset,
                    maxX, windowBaseY + 1, bz + offset + 1, "minecraft:glass_pane", ct);

                // Flower boxes under ground floor windows (trapdoor shelf)
                if (floor == 0)
                {
                    await rcon.SendSetBlockAsync(bx + offset, BaseY + 1, minZ - 1,
                        "minecraft:oak_trapdoor[facing=south,half=top,open=true]", ct);
                    await rcon.SendSetBlockAsync(bx + offset + 1, BaseY + 1, minZ - 1,
                        "minecraft:oak_trapdoor[facing=south,half=top,open=true]", ct);

                    if (offset != 0)
                    {
                        await rcon.SendSetBlockAsync(bx + offset, BaseY + 1, maxZ + 1,
                            "minecraft:oak_trapdoor[facing=north,half=top,open=true]", ct);
                        await rcon.SendSetBlockAsync(bx + offset + 1, BaseY + 1, maxZ + 1,
                            "minecraft:oak_trapdoor[facing=north,half=top,open=true]", ct);
                    }
                }
            }
        }
    }

    /// <summary>3-wide, 3-tall oak plank arched entrance on south face</summary>
    private async Task GenerateCottageEntranceAsync(int bx, int bz, CancellationToken ct)
    {
        int maxZ = bz + HalfFootprint;

        // Clear 3-wide, 3-tall opening
        await rcon.SendFillAsync(bx - 1, BaseY + 1, maxZ,
            bx + 1, BaseY + 3, maxZ, "minecraft:air", ct);

        // Oak plank arch at top
        await rcon.SendSetBlockAsync(bx - 1, BaseY + 3, maxZ, "minecraft:oak_planks", ct);
        await rcon.SendSetBlockAsync(bx + 1, BaseY + 3, maxZ, "minecraft:oak_planks", ct);
    }

    /// <summary>
    /// Furnished cottage interior:
    /// Ground floor: hearth + kitchen (fireplace, cauldron, crafting table, furnace, barrel)
    /// 2nd floor: study + bookshelves (desk, chair, bookshelves along walls)
    /// Channel topic sign on ground floor if topic is set.
    /// </summary>
    private async Task GenerateCottageInteriorAsync(int bx, int bz, string? channelTopic, CancellationToken ct)
    {
        int minX = bx - HalfFootprint + 2;
        int maxX = bx + HalfFootprint - 2;
        int minZ = bz - HalfFootprint + 2;
        int maxZ = bz + HalfFootprint - 2;

        // ── Ground floor: Hearth + Kitchen ──
        int gy = BaseY + 1;

        // Fireplace hearth in NW corner — campfire with chimney
        var groundBlocks = new List<(int x, int y, int z, string block)>
        {
            (minX, gy, minZ, "minecraft:campfire[lit=true]"),
            (minX, gy + 1, minZ, "minecraft:chain"),
            // Kitchen items along north wall
            (minX + 2, gy, minZ, "minecraft:crafting_table"),
            (minX + 3, gy, minZ, "minecraft:furnace[facing=south]"),
            (minX + 4, gy, minZ, "minecraft:smoker[facing=south]"),
            (minX + 5, gy, minZ, "minecraft:barrel[facing=up]"),
            // Dining area in center-west
            (minX + 1, gy, bz - 1, "minecraft:oak_slab[type=bottom]"), // table
            (minX + 2, gy, bz - 1, "minecraft:oak_slab[type=bottom]"),
            (minX + 3, gy, bz - 1, "minecraft:oak_slab[type=bottom]"),
            (minX + 1, gy, bz, "minecraft:oak_stairs[facing=north]"),  // chairs
            (minX + 3, gy, bz, "minecraft:oak_stairs[facing=north]"),
            // Cauldron near fireplace
            (minX + 1, gy, minZ, "minecraft:cauldron"),
        };
        await rcon.SendSetBlockBatchAsync(groundBlocks, ct);

        // ── 2nd floor: Study + Bookshelves ──
        int sy = BaseY + FloorHeight + 1;

        // Bookshelves along north wall
        await rcon.SendFillAsync(minX, sy, minZ, minX + 6, sy + 2, minZ, "minecraft:bookshelf", ct);

        // Bookshelves along west wall
        await rcon.SendFillAsync(minX, sy, minZ + 1, minX, sy + 2, minZ + 4, "minecraft:bookshelf", ct);

        var studyBlocks = new List<(int x, int y, int z, string block)>
        {
            // Writing desk + chair
            (minX + 3, sy, minZ + 2, "minecraft:oak_slab[type=bottom]"), // desk
            (minX + 4, sy, minZ + 2, "minecraft:oak_slab[type=bottom]"),
            (minX + 3, sy, minZ + 3, "minecraft:oak_stairs[facing=north]"), // chair
            // Lectern for reading
            (minX + 6, sy, minZ + 2, "minecraft:lectern[facing=west]"),
            // Flower pot for decoration
            (minX + 2, sy, minZ + 1, "minecraft:flower_pot"),
            // Beds along east wall
            (maxX, sy, bz - 2, "minecraft:red_bed[facing=west,part=foot]"),
            (maxX - 1, sy, bz - 2, "minecraft:red_bed[facing=west,part=head]"),
            (maxX, sy, bz + 1, "minecraft:red_bed[facing=west,part=foot]"),
            (maxX - 1, sy, bz + 1, "minecraft:red_bed[facing=west,part=head]"),
        };
        await rcon.SendSetBlockBatchAsync(studyBlocks, ct);

        // Channel topic sign on ground floor wall
        await PlaceTopicSignAsync(bx - 3, BaseY + 2, bz + HalfFootprint - 1, "north", channelTopic, ct);
    }

    /// <summary>Hanging lanterns from ceiling on each floor — batched</summary>
    private async Task GenerateCottageLightingAsync(int bx, int bz, CancellationToken ct)
    {
        var lanterns = new List<(int x, int y, int z, string block)>();
        for (int floor = 0; floor < Floors; floor++)
        {
            int ceilingY = (floor == 0) ? BaseY + FloorHeight : WallTop;
            int lanternY = ceilingY - 1;

            for (int x = bx - 8; x <= bx + 8; x += 5)
            {
                for (int z = bz - 8; z <= bz + 8; z += 5)
                {
                    lanterns.Add((x, lanternY, z, "minecraft:lantern[hanging=true]"));
                }
            }
        }
        await rcon.SendSetBlockBatchAsync(lanterns, ct);
    }

    // ═══════════════════════════════════════════════════════════════
    //  Stone Watchtower
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Imposing stone watchtower with stone brick walls, mossy base course,
    /// pyramid roof cap, lancet windows, corner buttresses, observation deck.
    /// </summary>
    private async Task GenerateStoneWatchtowerAsync(int bx, int bz, string channelName, string? channelTopic, CancellationToken ct)
    {
        await GenerateFoundationAsync(bx, bz, "minecraft:stone_bricks", ct);
        await GenerateWatchtowerWallsAsync(bx, bz, ct);
        await GenerateWatchtowerButtressesAsync(bx, bz, ct);
        await ClearInteriorAsync(bx, bz, ct);
        await GenerateFloorsAsync(bx, bz, "minecraft:stone_bricks", ct);
        await GenerateWatchtowerStairsAsync(bx, bz, ct);
        await GenerateWatchtowerRoofAsync(bx, bz, ct);
        await GenerateWatchtowerWindowsAsync(bx, bz, ct);
        await GenerateWatchtowerEntranceAsync(bx, bz, ct);
        await GenerateWatchtowerLightingAsync(bx, bz, ct);
        await GenerateSignsAsync(bx, bz, channelName, "Watchtower", ct);
        await GenerateWatchtowerInteriorAsync(bx, bz, channelTopic, ct);
    }

    /// <summary>Stone brick walls with mossy stone brick base course.</summary>
    private async Task GenerateWatchtowerWallsAsync(int bx, int bz, CancellationToken ct)
    {
        int minX = bx - HalfFootprint;
        int maxX = bx + HalfFootprint;
        int minZ = bz - HalfFootprint;
        int maxZ = bz + HalfFootprint;

        // Full stone brick walls
        await rcon.SendFillAsync(minX, BaseY + 1, minZ, maxX, WallTop, minZ, "minecraft:stone_bricks", ct);
        await rcon.SendFillAsync(minX, BaseY + 1, maxZ, maxX, WallTop, maxZ, "minecraft:stone_bricks", ct);
        await rcon.SendFillAsync(minX, BaseY + 1, minZ, minX, WallTop, maxZ, "minecraft:stone_bricks", ct);
        await rcon.SendFillAsync(maxX, BaseY + 1, minZ, maxX, WallTop, maxZ, "minecraft:stone_bricks", ct);

        // Mossy stone brick base course
        await rcon.SendFillAsync(minX, BaseY + 1, minZ, maxX, BaseY + 1, minZ, "minecraft:mossy_stone_bricks", ct);
        await rcon.SendFillAsync(minX, BaseY + 1, maxZ, maxX, BaseY + 1, maxZ, "minecraft:mossy_stone_bricks", ct);
        await rcon.SendFillAsync(minX, BaseY + 1, minZ, minX, BaseY + 1, maxZ, "minecraft:mossy_stone_bricks", ct);
        await rcon.SendFillAsync(maxX, BaseY + 1, minZ, maxX, BaseY + 1, maxZ, "minecraft:mossy_stone_bricks", ct);
    }

    /// <summary>Stone brick step buttresses at each corner extending outward.</summary>
    private async Task GenerateWatchtowerButtressesAsync(int bx, int bz, CancellationToken ct)
    {
        var blocks = new List<(int x, int y, int z, string block)>();
        foreach (var (dx, dz) in TurretOffsets)
        {
            int cornerX = bx + dx;
            int cornerZ = bz + dz;
            int outX = dx < 0 ? -1 : 1;
            int outZ = dz < 0 ? -1 : 1;

            for (int layer = 0; layer < 3; layer++)
            {
                int y = BaseY + 1 + layer;
                int reach = 3 - layer;
                string facingX = outX > 0 ? "east" : "west";
                string facingZ = outZ > 0 ? "south" : "north";

                for (int r = 1; r <= reach; r++)
                {
                    blocks.Add((cornerX + outX * r, y, cornerZ,
                        $"minecraft:stone_brick_stairs[facing={facingX}]"));
                }
                for (int r = 1; r <= reach; r++)
                {
                    blocks.Add((cornerX, y, cornerZ + outZ * r,
                        $"minecraft:stone_brick_stairs[facing={facingZ}]"));
                }
            }
        }
        await rcon.SendSetBlockBatchAsync(blocks, ct);
    }

    /// <summary>3-wide stone brick staircase for the watchtower</summary>
    private async Task GenerateWatchtowerStairsAsync(int bx, int bz, CancellationToken ct)
    {
        int stairX = bx + HalfFootprint - 5;
        int stairZ = bz - HalfFootprint + 1;
        int baseFloorY = BaseY + 1;

        int upperFloorY = BaseY + FloorHeight;
        await rcon.SendFillAsync(stairX, upperFloorY, stairZ,
            stairX + 2, upperFloorY, stairZ + 4, "minecraft:air", ct);

        for (int step = 0; step < 4; step++)
        {
            int sy = baseFloorY + step;
            int sz = stairZ + 4 - step;
            await rcon.SendFillAsync(stairX, sy, sz, stairX + 2, sy, sz,
                "minecraft:stone_brick_stairs[facing=north]", ct);
        }

        await rcon.SendFillAsync(stairX, baseFloorY + 4, stairZ,
            stairX + 2, baseFloorY + 4, stairZ + 1,
            "minecraft:stone_bricks", ct);
    }

    /// <summary>
    /// Stone brick pyramid cap (3 stepped layers) with parapet and observation deck.
    /// </summary>
    private async Task GenerateWatchtowerRoofAsync(int bx, int bz, CancellationToken ct)
    {
        int minX = bx - HalfFootprint;
        int maxX = bx + HalfFootprint;
        int minZ = bz - HalfFootprint;
        int maxZ = bz + HalfFootprint;

        // Flat roof slab
        await rcon.SendFillAsync(minX, RoofY, minZ, maxX, RoofY, maxZ,
            "minecraft:stone_brick_slab", ct);

        // Crenellated parapet — fill full edges then batch-clear alternating blocks
        await rcon.SendFillAsync(minX, RoofY + 1, minZ, maxX, RoofY + 1, minZ, "minecraft:stone_bricks", ct);
        await rcon.SendFillAsync(minX, RoofY + 1, maxZ, maxX, RoofY + 1, maxZ, "minecraft:stone_bricks", ct);
        await rcon.SendFillAsync(minX, RoofY + 1, minZ + 1, minX, RoofY + 1, maxZ - 1, "minecraft:stone_bricks", ct);
        await rcon.SendFillAsync(maxX, RoofY + 1, minZ + 1, maxX, RoofY + 1, maxZ - 1, "minecraft:stone_bricks", ct);

        var crenelClears = new List<(int x, int y, int z, string block)>();
        for (int x = minX + 1; x <= maxX; x += 2)
        {
            crenelClears.Add((x, RoofY + 1, minZ, "minecraft:air"));
            crenelClears.Add((x, RoofY + 1, maxZ, "minecraft:air"));
        }
        for (int z = minZ + 1; z <= maxZ - 1; z += 2)
        {
            crenelClears.Add((minX, RoofY + 1, z, "minecraft:air"));
            crenelClears.Add((maxX, RoofY + 1, z, "minecraft:air"));
        }
        await rcon.SendSetBlockBatchAsync(crenelClears, ct);

        // Stepped pyramid cap (3 layers, centered)
        for (int layer = 1; layer <= 3; layer++)
        {
            int inset = layer * 2;
            int pyMinX = minX + inset;
            int pyMaxX = maxX - inset;
            int pyMinZ = minZ + inset;
            int pyMaxZ = maxZ - inset;
            if (pyMinX > pyMaxX || pyMinZ > pyMaxZ) break;

            await rcon.SendFillAsync(pyMinX, RoofY + layer, pyMinZ, pyMaxX, RoofY + layer, pyMaxZ,
                "minecraft:stone_brick_slab", ct);
        }

        // Observation deck: glass pane railings inside the parapet
        await rcon.SendFillAsync(minX + 1, RoofY + 1, minZ + 1, maxX - 1, RoofY + 1, minZ + 1, "minecraft:glass_pane", ct);
        await rcon.SendFillAsync(minX + 1, RoofY + 1, maxZ - 1, maxX - 1, RoofY + 1, maxZ - 1, "minecraft:glass_pane", ct);
        await rcon.SendFillAsync(minX + 1, RoofY + 1, minZ + 1, minX + 1, RoofY + 1, maxZ - 1, "minecraft:glass_pane", ct);
        await rcon.SendFillAsync(maxX - 1, RoofY + 1, minZ + 1, maxX - 1, RoofY + 1, maxZ - 1, "minecraft:glass_pane", ct);
    }

    /// <summary>
    /// Tall narrow 1×3 lancet windows (glass panes), 2 per wall per floor.
    /// </summary>
    private async Task GenerateWatchtowerWindowsAsync(int bx, int bz, CancellationToken ct)
    {
        int minX = bx - HalfFootprint;
        int maxX = bx + HalfFootprint;
        int minZ = bz - HalfFootprint;
        int maxZ = bz + HalfFootprint;

        int[] windowOffsets = [-5, 5]; // 2 windows per wall

        for (int floor = 0; floor < Floors; floor++)
        {
            int windowBaseY = BaseY + 2 + floor * FloorHeight;

            foreach (int offset in windowOffsets)
            {
                // North wall lancet (1×3)
                await rcon.SendFillAsync(bx + offset, windowBaseY, minZ,
                    bx + offset, windowBaseY + 2, minZ, "minecraft:glass_pane", ct);

                // South wall lancet
                await rcon.SendFillAsync(bx + offset, windowBaseY, maxZ,
                    bx + offset, windowBaseY + 2, maxZ, "minecraft:glass_pane", ct);

                // West wall lancet
                await rcon.SendFillAsync(minX, windowBaseY, bz + offset,
                    minX, windowBaseY + 2, bz + offset, "minecraft:glass_pane", ct);

                // East wall lancet
                await rcon.SendFillAsync(maxX, windowBaseY, bz + offset,
                    maxX, windowBaseY + 2, bz + offset, "minecraft:glass_pane", ct);
            }
        }
    }

    /// <summary>
    /// Iron door frame with stone brick arch, slightly recessed entrance.
    /// </summary>
    private async Task GenerateWatchtowerEntranceAsync(int bx, int bz, CancellationToken ct)
    {
        int maxZ = bz + HalfFootprint;

        // Clear 3-wide, 4-tall opening
        await rcon.SendFillAsync(bx - 1, BaseY + 1, maxZ,
            bx + 1, BaseY + 4, maxZ, "minecraft:air", ct);

        // Stone brick arch across all 3 blocks at top
        await rcon.SendSetBlockAsync(bx - 1, BaseY + 4, maxZ, "minecraft:stone_bricks", ct);
        await rcon.SendSetBlockAsync(bx, BaseY + 4, maxZ, "minecraft:stone_bricks", ct);
        await rcon.SendSetBlockAsync(bx + 1, BaseY + 4, maxZ, "minecraft:stone_bricks", ct);

        // Recess the actual doorway 1 block deeper
        await rcon.SendFillAsync(bx - 1, BaseY + 1, maxZ - 1,
            bx + 1, BaseY + 3, maxZ - 1, "minecraft:air", ct);
    }

    /// <summary>Wall-mounted soul torches for blue-flame atmosphere — batched</summary>
    private async Task GenerateWatchtowerLightingAsync(int bx, int bz, CancellationToken ct)
    {
        int minX = bx - HalfFootprint + 1;
        int maxX = bx + HalfFootprint - 1;
        int minZ = bz - HalfFootprint + 1;
        int maxZ = bz + HalfFootprint - 1;

        var torches = new List<(int x, int y, int z, string block)>();
        for (int floor = 0; floor < Floors; floor++)
        {
            int torchY = BaseY + 3 + floor * FloorHeight;

            for (int x = bx - 8; x <= bx + 8; x += 4)
                torches.Add((x, torchY, minZ, "minecraft:soul_wall_torch[facing=south]"));

            for (int x = bx - 8; x <= bx + 8; x += 4)
            {
                if (floor == 0 && x >= bx - 1 && x <= bx + 1)
                    continue;
                torches.Add((x, torchY, maxZ, "minecraft:soul_wall_torch[facing=north]"));
            }

            for (int z = bz - 8; z <= bz + 8; z += 4)
                torches.Add((minX, torchY, z, "minecraft:soul_wall_torch[facing=east]"));

            for (int z = bz - 8; z <= bz + 8; z += 4)
                torches.Add((maxX, torchY, z, "minecraft:soul_wall_torch[facing=west]"));
        }
        await rcon.SendSetBlockBatchAsync(torches, ct);
    }

    // ═══════════════════════════════════════════════════════════════
    //  Interior furnishing (placed AFTER floors, stairs, lighting, signs)
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Places a wall sign with the channel topic text if set.
    /// Placed on ground floor interior wall for context.
    /// </summary>
    private async Task PlaceTopicSignAsync(int x, int y, int z, string facing, string? topic, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(topic)) return;

        // Truncate topic to fit on sign (15 chars per line, 2 usable lines)
        var line1 = topic.Length > 15 ? topic[..15] : topic;
        var line2 = topic.Length > 15 ? (topic.Length > 30 ? topic[15..30] : topic[15..]) : "";
        var emptyText = "\"\"";

        await rcon.SendSetBlockAsync(x, y, z,
            $"minecraft:oak_wall_sign[facing={facing}]{{front_text:{{messages:['\"§oTopic\"','\"{line1}\"','\"{line2}\"',{emptyText}]}}}}", ct);
    }

    /// <summary>
    /// Medieval Castle interior furnishing:
    /// Ground floor: Throne room — throne (stairs), red carpet, banner displays
    /// 2nd floor: Armory — armor stands, weapon racks (item frames), anvil
    /// </summary>
    private async Task GenerateCastleInteriorAsync(int bx, int bz, string? channelTopic, CancellationToken ct)
    {
        int minX = bx - HalfFootprint + 2;
        int maxX = bx + HalfFootprint - 2;
        int minZ = bz - HalfFootprint + 2;
        int maxZ = bz + HalfFootprint - 2;

        // ── Ground floor: Throne Room ──
        int gy = BaseY + 1;

        // Red carpet runner from entrance to throne (center aisle)
        await rcon.SendFillAsync(bx - 1, gy, bz, bx + 1, gy, maxZ - 1, "minecraft:red_carpet", ct);

        // Raised throne platform at north end
        await rcon.SendFillAsync(bx - 2, gy, minZ, bx + 2, gy, minZ + 1, "minecraft:polished_andesite", ct);

        var throneBlocks = new List<(int x, int y, int z, string block)>
        {
            // Throne chair (quartz stairs facing south, like a seat)
            (bx, gy + 1, minZ, "minecraft:quartz_stairs[facing=south]"),
            // Armrests
            (bx - 1, gy + 1, minZ, "minecraft:stone_brick_wall"),
            (bx + 1, gy + 1, minZ, "minecraft:stone_brick_wall"),
            // Throne back
            (bx, gy + 2, minZ, "minecraft:stone_brick_wall"),
            // Banners flanking throne
            (bx - 3, gy + 2, minZ, "minecraft:red_banner"),
            (bx + 3, gy + 2, minZ, "minecraft:red_banner"),
            // Banquet table along west wall
            (minX, gy, bz - 2, "minecraft:oak_slab[type=bottom]"),
            (minX, gy, bz - 1, "minecraft:oak_slab[type=bottom]"),
            (minX, gy, bz, "minecraft:oak_slab[type=bottom]"),
            (minX, gy, bz + 1, "minecraft:oak_slab[type=bottom]"),
            (minX + 1, gy, bz - 2, "minecraft:oak_stairs[facing=west]"),
            (minX + 1, gy, bz + 1, "minecraft:oak_stairs[facing=west]"),
        };
        await rcon.SendSetBlockBatchAsync(throneBlocks, ct);

        // ── 2nd floor: Armory ──
        int ay = BaseY + FloorHeight + 1;

        var armoryBlocks = new List<(int x, int y, int z, string block)>
        {
            // Anvil for weapon smithing
            (minX + 1, ay, minZ + 1, "minecraft:anvil[facing=south]"),
            // Grindstone
            (minX + 3, ay, minZ + 1, "minecraft:grindstone[facing=south]"),
            // Smithing table
            (minX + 5, ay, minZ + 1, "minecraft:smithing_table"),
            // Armor stands (represented by armor stand entities placed via blocks)
            // Use fence + stone button as visual stand
            (maxX - 1, ay, minZ + 1, "minecraft:oak_fence"),
            (maxX - 3, ay, minZ + 1, "minecraft:oak_fence"),
            (maxX - 5, ay, minZ + 1, "minecraft:oak_fence"),
            (maxX - 1, ay + 1, minZ + 1, "minecraft:stone_button[face=floor]"),
            (maxX - 3, ay + 1, minZ + 1, "minecraft:stone_button[face=floor]"),
            (maxX - 5, ay + 1, minZ + 1, "minecraft:stone_button[face=floor]"),
            // Weapon rack (item frames need entities, use chains instead)
            (minX, ay + 2, bz, "minecraft:chain"),
            (minX, ay + 2, bz + 2, "minecraft:chain"),
            (minX, ay + 2, bz - 2, "minecraft:chain"),
            // Chest for storage
            (maxX, ay, maxZ - 1, "minecraft:chest[facing=west]"),
            (maxX, ay, maxZ - 2, "minecraft:chest[facing=west]"),
        };
        await rcon.SendSetBlockBatchAsync(armoryBlocks, ct);

        // Channel topic sign
        await PlaceTopicSignAsync(bx - 3, BaseY + 2, maxZ, "north", channelTopic, ct);
    }

    /// <summary>
    /// Stone Watchtower interior furnishing:
    /// Ground floor: Map/planning room — cartography table, lectern, map wall
    /// 2nd floor: Brewing/supplies — brewing stands, cauldron, chests
    /// </summary>
    private async Task GenerateWatchtowerInteriorAsync(int bx, int bz, string? channelTopic, CancellationToken ct)
    {
        int minX = bx - HalfFootprint + 2;
        int maxX = bx + HalfFootprint - 2;
        int minZ = bz - HalfFootprint + 2;
        int maxZ = bz + HalfFootprint - 2;

        // ── Ground floor: Map/Planning Room ──
        int gy = BaseY + 1;

        // Central planning table (large oak slab table)
        await rcon.SendFillAsync(bx - 2, gy, bz - 2, bx + 2, gy, bz + 2, "minecraft:oak_slab[type=bottom]", ct);

        var planningBlocks = new List<(int x, int y, int z, string block)>
        {
            // Cartography table in NW
            (minX, gy, minZ, "minecraft:cartography_table"),
            // Lectern with commands
            (minX + 2, gy, minZ, "minecraft:lectern[facing=south]"),
            // Chairs around planning table
            (bx - 3, gy, bz, "minecraft:oak_stairs[facing=east]"),
            (bx + 3, gy, bz, "minecraft:oak_stairs[facing=west]"),
            (bx, gy, bz - 3, "minecraft:oak_stairs[facing=south]"),
            (bx, gy, bz + 3, "minecraft:oak_stairs[facing=north]"),
            // Compass and clock display shelves
            (maxX, gy, minZ, "minecraft:chiseled_bookshelf"),
            (maxX, gy, minZ + 1, "minecraft:chiseled_bookshelf"),
            // Supply barrels along west wall
            (minX, gy, bz - 1, "minecraft:barrel[facing=east]"),
            (minX, gy, bz + 1, "minecraft:barrel[facing=east]"),
        };
        await rcon.SendSetBlockBatchAsync(planningBlocks, ct);

        // ── 2nd floor: Brewing + Supplies ──
        int by = BaseY + FloorHeight + 1;

        var brewingBlocks = new List<(int x, int y, int z, string block)>
        {
            // Brewing stands along north wall
            (minX + 1, by, minZ, "minecraft:brewing_stand"),
            (minX + 3, by, minZ, "minecraft:brewing_stand"),
            // Cauldron for water
            (minX + 5, by, minZ, "minecraft:water_cauldron[level=3]"),
            // Supply chests along east wall
            (maxX, by, minZ + 1, "minecraft:chest[facing=west]"),
            (maxX, by, minZ + 3, "minecraft:chest[facing=west]"),
            // Potion ingredient shelves
            (minX, by, bz - 2, "minecraft:bookshelf"),
            (minX, by, bz - 1, "minecraft:bookshelf"),
            (minX, by + 1, bz - 2, "minecraft:bookshelf"),
            (minX, by + 1, bz - 1, "minecraft:bookshelf"),
            // Workbench
            (minX + 2, by, bz + 2, "minecraft:crafting_table"),
            // Soul campfire for atmosphere
            (bx, by, bz, "minecraft:soul_campfire[lit=true]"),
        };
        await rcon.SendSetBlockBatchAsync(brewingBlocks, ct);

        // Channel topic sign
        await PlaceTopicSignAsync(bx - 3, BaseY + 2, maxZ, "north", channelTopic, ct);
    }
}
