using Bridge.Data;
using Microsoft.Extensions.Logging;
using WorldGen.Worker.Models;
using WorldGen.Worker.Services;

namespace WorldGen.Worker.Generators;

/// <summary>
/// Generates minecart rail tracks between villages with station structures at each end.
/// Tracks run at Y=-59 (1 above superflat surface) with powered rails every 8 blocks.
/// Stations include covered platforms with departure signs, destination maps, and minecart dispensers.
/// Hub-and-spoke topology: village stations use south-offset placement; Crossroads stations
/// use radial slot positioning around the plaza perimeter.
/// </summary>
public sealed class TrackGenerator(RconService rcon, ILogger<TrackGenerator> logger) : ITrackGenerator
{
    private const int TrackY = -59; // elevated trackbed (superflat surface + 1)
    private const int TrackbedY = -60; // support block under tracks (superflat surface)
    private const int PoweredRailInterval = 8;
    private const int StationOffset = WorldConstants.VillageStationOffset; // station at south edge of plaza

    // Station platform dimensions
    private const int PlatformLength = 9; // along track direction (expanded for shelter)
    private const int PlatformWidth = 5; // perpendicular to track (wider for amenities)

    // Crossroads radial station positioning
    private const int CrossroadsStationRadius = WorldConstants.CrossroadsStationRadius;
    private const int CrossroadsStationSlots = WorldConstants.CrossroadsStationSlots;

    public async Task GenerateAsync(TrackGenerationRequest request, CancellationToken ct)
    {
        logger.LogInformation(
            "Generating track from '{Source}' ({SX},{SZ}) to '{Dest}' ({DX},{DZ})",
            request.SourceVillageName, request.SourceCenterX, request.SourceCenterZ,
            request.DestinationVillageName, request.DestCenterX, request.DestCenterZ);

        bool destIsCrossroads = request.DestCenterX == 0 && request.DestCenterZ == 0;

        // Source station: always south of village plaza
        int srcStationX = request.SourceCenterX;
        int srcStationZ = request.SourceCenterZ + StationOffset;

        int dstStationX, dstStationZ;

        if (destIsCrossroads)
        {
            // Crossroads end: use radial slot based on angle from hub to village
            var (slotX, slotZ) = GetCrossroadsSlotPosition(request.SourceCenterX, request.SourceCenterZ);
            dstStationX = slotX;
            dstStationZ = slotZ;
        }
        else
        {
            // Standard village destination: south of village plaza
            dstStationX = request.DestCenterX;
            dstStationZ = request.DestCenterZ + StationOffset;
        }

        // Hub-and-spoke: each village has exactly one track to Crossroads,
        // so no platform offset is needed — station is always at the village's expected position.
        int srcPlatformZ = srcStationZ;

        int dstPlatformX = dstStationX;
        int dstPlatformZ = dstStationZ;

        // Forceload all chunks along the track path before placing blocks
        await ForceloadTrackRegionAsync(srcStationX, srcPlatformZ, dstPlatformX, dstPlatformZ, add: true, ct);

        await GenerateStationPlatformAsync(srcStationX, srcPlatformZ,
            request.DestinationVillageName, request.SourceVillageName, ct);
        await GenerateStationPlatformAsync(dstPlatformX, dstPlatformZ,
            request.SourceVillageName, request.DestinationVillageName, ct);

        await GenerateTrackPathAsync(
            srcStationX, srcPlatformZ,
            dstPlatformX, dstPlatformZ,
            ct);

        // NOTE: We intentionally do NOT release forceloaded chunks along track paths.
        // Without permanent forceloading, intermediate track chunks won't be loaded
        // during gameplay, causing minecarts to fall into unloaded chunks and the
        // track to "disappear" mid-ride. Forceloaded track chunks ensure continuous
        // minecart travel between villages.
        // 
        // Potential future optimization: Use spawn chunks or ticket-based loading
        // only for the track corridor (1-2 chunk width) rather than the full bounding box.

        logger.LogInformation(
            "Track generation complete: '{Source}' \u2194 '{Dest}'",
            request.SourceVillageName, request.DestinationVillageName);
    }

    /// <summary>
    /// Forceloads or releases chunks along the track path between two stations.
    /// Uses bounding box approach — forceloads the rectangle covering both stations.
    /// </summary>
    private async Task ForceloadTrackRegionAsync(int x1, int z1, int x2, int z2, bool add, CancellationToken ct)
    {
        // Platform is now oriented north-south: length along Z, width along X
        int minX = Math.Min(x1, x2) - PlatformWidth;
        int maxX = Math.Max(x1, x2) + PlatformWidth;
        int minZ = Math.Min(z1, z2) - PlatformLength;
        int maxZ = Math.Max(z1, z2) + PlatformLength;

        int minChunkX = minX >> 4;
        int maxChunkX = maxX >> 4;
        int minChunkZ = minZ >> 4;
        int maxChunkZ = maxZ >> 4;

        string cmd = add ? "forceload add" : "forceload remove";
        await rcon.SendCommandAsync($"{cmd} {minChunkX << 4} {minChunkZ << 4} {maxChunkX << 4} {maxChunkZ << 4}", ct);
    }

    /// <summary>
    /// Calculates the radial station slot position at Crossroads for a village.
    /// Uses the angle from Crossroads (0,0) to the village to pick one of 16 evenly
    /// spaced slots around the plaza perimeter at CrossroadsStationRadius.
    /// </summary>
    private static (int X, int Z) GetCrossroadsSlotPosition(int villageCenterX, int villageCenterZ)
    {
        double angle = Math.Atan2(villageCenterZ, villageCenterX);
        if (angle < 0) angle += 2 * Math.PI;
        int slotIndex = (int)(angle / (2 * Math.PI) * CrossroadsStationSlots) % CrossroadsStationSlots;
        double slotAngle = slotIndex * (2 * Math.PI / CrossroadsStationSlots);
        int slotX = (int)(CrossroadsStationRadius * Math.Cos(slotAngle));
        int slotZ = (int)(CrossroadsStationRadius * Math.Sin(slotAngle));
        return (slotX, slotZ);
    }

    /// <summary>
    /// Builds a covered station platform with stone brick base, oak shelter roof,
    /// destination/arrival signs, minecart dispenser, and welcoming amenities.
    /// Platform runs north-south (along Z), 9 blocks long, 5 blocks wide.
    /// Track enters from south (higher Z) and exits to north (lower Z).
    /// </summary>
    private async Task GenerateStationPlatformAsync(int cx, int cz, string destinationName, string localVillageName, CancellationToken ct)
    {
        logger.LogInformation("Generating station platform at ({X},{Z}) for destination '{Dest}'",
            cx, cz, destinationName);

        int halfLen = PlatformLength / 2; // 4 (along Z axis now)
        int halfWidth = PlatformWidth / 2; // 2 (along X axis now)

        // 1. Foundation: Stone brick platform base (rotated: length along Z, width along X)
        await rcon.SendFillAsync(
            cx - halfWidth, TrackbedY, cz - halfLen,
            cx + halfWidth, TrackbedY, cz + halfLen,
            "minecraft:stone_bricks", ct);

        // 2. Clear air above platform (5 blocks high for shelter structure)
        await rcon.SendFillAsync(
            cx - halfWidth, TrackY, cz - halfLen,
            cx + halfWidth, TrackY + 4, cz + halfLen,
            "minecraft:air", ct);

        // 3. Rail track down the center of the platform running north-south (along Z)
        // Use fill for the trackbed and powered rails along the entire platform center
        await rcon.SendFillAsync(cx, TrackbedY, cz - halfLen, cx, TrackbedY, cz + halfLen,
            "minecraft:stone_bricks", ct);
        await rcon.SendFillAsync(cx, TrackY, cz - halfLen, cx, TrackY, cz + halfLen,
            "minecraft:powered_rail[powered=true]", ct);

        // 4. Stone brick slab walkways on east and west sides of the track
        await rcon.SendFillAsync(
            cx - halfWidth, TrackY, cz - halfLen,
            cx - 1, TrackY, cz + halfLen,
            "minecraft:stone_brick_slab", ct);
        await rcon.SendFillAsync(
            cx + 1, TrackY, cz - halfLen,
            cx + halfWidth, TrackY, cz + halfLen,
            "minecraft:stone_brick_slab", ct);

        // 5. Shelter structure: oak fence posts at corners
        await rcon.SendFillAsync(cx - halfWidth, TrackY, cz - halfLen,
            cx - halfWidth, TrackY + 3, cz - halfLen, "minecraft:oak_fence", ct);
        await rcon.SendFillAsync(cx + halfWidth, TrackY, cz - halfLen,
            cx + halfWidth, TrackY + 3, cz - halfLen, "minecraft:oak_fence", ct);
        await rcon.SendFillAsync(cx - halfWidth, TrackY, cz + halfLen,
            cx - halfWidth, TrackY + 3, cz + halfLen, "minecraft:oak_fence", ct);
        await rcon.SendFillAsync(cx + halfWidth, TrackY, cz + halfLen,
            cx + halfWidth, TrackY + 3, cz + halfLen, "minecraft:oak_fence", ct);

        // 6. Shelter roof: oak slabs covering the platform
        await rcon.SendFillAsync(
            cx - halfWidth, TrackY + 3, cz - halfLen,
            cx + halfWidth, TrackY + 3, cz + halfLen,
            "minecraft:oak_slab[type=top]", ct);

        // 7. Sign support blocks at north and south ends — batch as vertical fills
        await rcon.SendFillAsync(cx + halfWidth + 1, TrackY, cz - halfLen,
            cx + halfWidth + 1, TrackY + 2, cz - halfLen, "minecraft:stone_bricks", ct);
        await rcon.SendFillAsync(cx + halfWidth + 1, TrackY, cz + halfLen,
            cx + halfWidth + 1, TrackY + 2, cz + halfLen, "minecraft:stone_bricks", ct);

        // 8. Destination signs with village names — plain quoted strings, NOT JSON objects
        var truncatedDest = destinationName.Length > 15 ? destinationName[..15] : destinationName;
        var truncatedLocal = localVillageName.Length > 12 ? localVillageName[..12] : localVillageName;
        var destText = $"\"{truncatedDest}\"";
        var localText = $"\"{truncatedLocal}\"";
        var arrowText = "\"\u2191\""; // North arrow for north-south travel
        var stationText = "\"\u00a7lStation\""; // Bold "Station"
        var arrivedText = "\"\u00a72Welcome!\""; // Green "Welcome!"
        var fromText = $"\"\u00a77From: {truncatedLocal}\""; // Gray "From:"
        var emptyText = "\"\"";

        // Departure sign at south end (facing west so passengers can read it from platform)
        await rcon.SendSetBlockAsync(cx + halfWidth + 1, TrackY + 2, cz + halfLen,
            $"minecraft:oak_wall_sign[facing=west]{{front_text:{{messages:['{stationText}','{arrowText}','{destText}',{emptyText}]}}}}", ct);

        // Arrival sign at north end (facing west)
        await rcon.SendSetBlockAsync(cx + halfWidth + 1, TrackY + 2, cz - halfLen,
            $"minecraft:oak_wall_sign[facing=west]{{front_text:{{messages:['{arrivedText}','{localText}','{fromText}',{emptyText}]}}}}", ct);

        // 9. Button-activated minecart dispenser with descriptive sign (on west side)
        await rcon.SendSetBlockAsync(cx - halfWidth + 1, TrackbedY, cz + halfLen - 1,
            "minecraft:dispenser[facing=up]", ct);
        await rcon.SendSetBlockAsync(cx - halfWidth + 1, TrackY, cz + halfLen - 1,
            "minecraft:stone_button[face=floor,facing=east]", ct);

        // Load 64 minecarts into the dispenser
        await rcon.SendCommandAsync(
            $"data merge block {cx - halfWidth + 1} {TrackbedY} {cz + halfLen - 1} " +
            "{Items:[{Slot:0b,id:\"minecraft:minecart\",count:64}]}", ct);

        // Dispenser instruction sign (on west wall, facing east into platform)
        var getCartText = "\"Get Minecart\"";
        var pressText = "\"Press Button\"";
        await rcon.SendSetBlockAsync(cx - halfWidth, TrackY + 1, cz + halfLen - 1,
            $"minecraft:oak_wall_sign[facing=east]{{front_text:{{messages:[{emptyText},'{getCartText}','{pressText}',{emptyText}]}}}}", ct);

        // 10-12. Batch remaining station decorations: lanterns, benches, flower pots
        var stationDecor = new List<(int x, int y, int z, string block)>
        {
            // Lanterns
            (cx - halfWidth + 1, TrackY + 2, cz - 2, "minecraft:lantern[hanging=true]"),
            (cx - halfWidth + 1, TrackY + 2, cz + 2, "minecraft:lantern[hanging=true]"),
            (cx + halfWidth - 1, TrackY + 2, cz - 2, "minecraft:lantern[hanging=true]"),
            (cx + halfWidth - 1, TrackY + 2, cz + 2, "minecraft:lantern[hanging=true]"),
            // Benches
            (cx - halfWidth + 1, TrackY, cz - 2, "minecraft:oak_stairs[facing=east]"),
            (cx - halfWidth + 1, TrackY, cz + 2, "minecraft:oak_stairs[facing=east]"),
            (cx + halfWidth - 1, TrackY, cz - 2, "minecraft:oak_stairs[facing=west]"),
            (cx + halfWidth - 1, TrackY, cz + 2, "minecraft:oak_stairs[facing=west]"),
            // Flower pots
            (cx - halfWidth + 1, TrackY, cz - halfLen + 1, "minecraft:potted_red_tulip"),
            (cx + halfWidth - 1, TrackY, cz - halfLen + 1, "minecraft:potted_blue_orchid")
        };
        await rcon.SendSetBlockBatchAsync(stationDecor, ct);
    }

    /// <summary>
    /// Lays track between two station platforms using an L-shaped path.
    /// Rails don't support diagonal placement. We use X-first then Z approach:
    /// the track exits the village station heading east/west (X direction) to avoid
    /// crossing through the village plaza, then turns toward the destination along Z.
    /// The corner at (dstX, srcZ) is far from both stations.
    /// </summary>
    private async Task GenerateTrackPathAsync(int srcX, int srcZ, int dstX, int dstZ, CancellationToken ct)
    {
        logger.LogInformation("Laying track path from ({SX},{SZ}) to ({DX},{DZ})",
            srcX, srcZ, dstX, dstZ);

        // L-shaped path: X segment from source, then Z segment to destination
        // Corner at (dstX, srcZ) — same X as dest, same Z as source
        int cornerX = dstX;
        int cornerZ = srcZ;

        // Segment 1: horizontal (along X axis) from source toward corner
        // Goes from srcX to cornerX along Z = srcZ
        await LayRailSegmentAsync(srcX, srcZ, cornerX, cornerZ, ct);

        // Segment 2: vertical (along Z axis) from corner to destination
        // Goes from srcZ to dstZ along X = dstX
        // Start one block past cornerZ to avoid double-placing the corner rail
        int startZ = cornerZ < dstZ ? cornerZ + 1 : cornerZ - 1;
        if (srcZ != dstZ) // Only needed if there's vertical travel
        {
            await LayRailSegmentAsync(dstX, startZ, dstX, dstZ, ct);
        }

        // Place corner rail LAST so it detects neighbors and forms a curve
        // Only needed if we actually have a corner (different X and Z)
        if (srcX != dstX && srcZ != dstZ)
        {
            await PlaceCornerRailAsync(cornerX, cornerZ, ct);
        }
    }

    /// <summary>
    /// Places the corner rail block, ensuring it detects neighbors and creates a curve.
    /// Called AFTER both connecting segments are placed so the rail auto-orients correctly.
    /// </summary>
    private async Task PlaceCornerRailAsync(int x, int z, CancellationToken ct)
    {
        // Ensure trackbed exists at corner
        await rcon.SendSetBlockAsync(x, TrackbedY, z, "minecraft:stone_bricks", ct);
        // Place rail - it will auto-detect neighbors and form a curve
        await rcon.SendSetBlockAsync(x, TrackY, z, "minecraft:rail", ct);
    }

    /// <summary>
    /// Lays a straight rail segment (either along X or Z axis) with powered rails at intervals.
    /// </summary>
    private async Task LayRailSegmentAsync(int x1, int z1, int x2, int z2, CancellationToken ct)
    {
        bool isHorizontal = z1 == z2;
        int start, end, fixedCoord;

        if (isHorizontal)
        {
            fixedCoord = z1;
            start = Math.Min(x1, x2);
            end = Math.Max(x1, x2);
        }
        else
        {
            fixedCoord = x1;
            start = Math.Min(z1, z2);
            end = Math.Max(z1, z2);
        }

        if (start == end)
            return; // zero-length segment, skip

        // Lay stone brick trackbed using /fill (efficient bulk command)
        if (isHorizontal)
        {
            await rcon.SendFillAsync(start, TrackbedY, fixedCoord, end, TrackbedY, fixedCoord,
                "minecraft:stone_bricks", ct);
        }
        else
        {
            await rcon.SendFillAsync(fixedCoord, TrackbedY, start, fixedCoord, TrackbedY, end,
                "minecraft:stone_bricks", ct);
        }

        // Clear air above trackbed (2 blocks for player in minecart)
        if (isHorizontal)
        {
            await rcon.SendFillAsync(start, TrackY, fixedCoord, end, TrackY + 1, fixedCoord,
                "minecraft:air", ct);
        }
        else
        {
            await rcon.SendFillAsync(fixedCoord, TrackY, start, fixedCoord, TrackY + 1, end,
                "minecraft:air", ct);
        }

        // Determine rail shape based on direction
        // Horizontal (along X axis): shape=east_west
        // Vertical (along Z axis): shape=north_south
        string poweredRailShape = isHorizontal ? "east_west" : "north_south";

        // Place rails: fill 7-block regular rail runs, individual setblock for powered rail + redstone
        var poweredBlocks = new List<(int x, int y, int z, string block)>();
        int segStart = start;
        for (int i = start; i <= end; i++)
        {
            bool isPowered = (i - start) % PoweredRailInterval == 0;

            if (isPowered)
            {
                int x = isHorizontal ? i : fixedCoord;
                int z = isHorizontal ? fixedCoord : i;

                // Flush any pending regular rail segment before this powered rail
                if (segStart < i)
                {
                    if (isHorizontal)
                        await rcon.SendFillAsync(segStart, TrackY, fixedCoord, i - 1, TrackY, fixedCoord, "minecraft:rail", ct);
                    else
                        await rcon.SendFillAsync(fixedCoord, TrackY, segStart, fixedCoord, TrackY, i - 1, "minecraft:rail", ct);
                }

                // Powered rail + redstone block
                poweredBlocks.Add((x, TrackbedY, z, "minecraft:redstone_block"));
                poweredBlocks.Add((x, TrackY, z,
                    $"minecraft:powered_rail[powered=true,shape={poweredRailShape}]"));

                segStart = i + 1;
            }
        }

        // Flush remaining regular rail segment after last powered rail
        if (segStart <= end)
        {
            if (isHorizontal)
                await rcon.SendFillAsync(segStart, TrackY, fixedCoord, end, TrackY, fixedCoord, "minecraft:rail", ct);
            else
                await rcon.SendFillAsync(fixedCoord, TrackY, segStart, fixedCoord, TrackY, end, "minecraft:rail", ct);
        }

        if (poweredBlocks.Count > 0)
            await rcon.SendSetBlockBatchAsync(poweredBlocks, ct);
    }
}
