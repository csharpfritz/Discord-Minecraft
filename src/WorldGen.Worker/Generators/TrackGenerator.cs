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

        // Source station: always south of village plaza, always north-south
        int srcStationX = request.SourceCenterX;
        int srcStationZ = request.SourceCenterZ + StationOffset;

        int dstStationX, dstStationZ;
        var dstOrientation = StationOrientation.NorthSouth;

        if (destIsCrossroads)
        {
            // Crossroads end: use radial slot based on angle from hub to village
            var (slotX, slotZ, orientation) = GetCrossroadsSlotPosition(request.SourceCenterX, request.SourceCenterZ);
            dstStationX = slotX;
            dstStationZ = slotZ;
            dstOrientation = orientation;
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
            request.DestinationVillageName, request.SourceVillageName,
            StationOrientation.NorthSouth, ct);
        await GenerateStationPlatformAsync(dstPlatformX, dstPlatformZ,
            request.SourceVillageName, request.DestinationVillageName,
            dstOrientation, ct);

        // Build a cobblestone walkway from Crossroads station back toward plaza edge
        if (destIsCrossroads)
        {
            await GenerateCrossroadsWalkwayAsync(dstPlatformX, dstPlatformZ, ct);
        }

        await GenerateTrackPathAsync(
            srcStationX, srcPlatformZ,
            dstPlatformX, dstPlatformZ,
            destIsCrossroads, dstOrientation,
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
    /// Returns slot coordinates AND the radial orientation for the station platform.
    /// </summary>
    internal static (int X, int Z, StationOrientation Orientation) GetCrossroadsSlotPosition(int villageCenterX, int villageCenterZ)
    {
        double angle = Math.Atan2(villageCenterZ, villageCenterX);
        if (angle < 0) angle += 2 * Math.PI;
        int slotIndex = (int)(angle / (2 * Math.PI) * CrossroadsStationSlots) % CrossroadsStationSlots;
        double slotAngle = slotIndex * (2 * Math.PI / CrossroadsStationSlots);
        int slotX = (int)(CrossroadsStationRadius * Math.Cos(slotAngle));
        int slotZ = (int)(CrossroadsStationRadius * Math.Sin(slotAngle));

        // Orientation is radial: if the slot is primarily east/west of center, the platform
        // runs east-west so rails point toward the village. Otherwise north-south.
        var orientation = Math.Abs(Math.Cos(slotAngle)) > Math.Abs(Math.Sin(slotAngle))
            ? StationOrientation.EastWest
            : StationOrientation.NorthSouth;

        return (slotX, slotZ, orientation);
    }

    /// <summary>
    /// Builds a covered station platform with stone brick base, oak shelter roof,
    /// destination/arrival signs, minecart dispenser, and welcoming amenities.
    /// Orientation determines whether the platform runs along Z (NorthSouth) or X (EastWest).
    /// </summary>
    private async Task GenerateStationPlatformAsync(int cx, int cz, string destinationName, string localVillageName,
        StationOrientation orientation, CancellationToken ct)
    {
        logger.LogInformation("Generating station platform at ({X},{Z}) orientation={Orientation} for destination '{Dest}'",
            cx, cz, orientation, destinationName);

        int halfLen = PlatformLength / 2; // 4
        int halfWidth = PlatformWidth / 2; // 2

        // For EastWest orientation, swap: length runs along X, width along Z
        bool ew = orientation == StationOrientation.EastWest;
        int halfLenX = ew ? halfLen : halfWidth;
        int halfLenZ = ew ? halfWidth : halfLen;

        // 1. Foundation: Stone brick platform base
        await rcon.SendFillAsync(
            cx - halfLenX, TrackbedY, cz - halfLenZ,
            cx + halfLenX, TrackbedY, cz + halfLenZ,
            "minecraft:stone_bricks", ct);

        // 2. Clear air above platform (5 blocks high for shelter structure)
        await rcon.SendFillAsync(
            cx - halfLenX, TrackY, cz - halfLenZ,
            cx + halfLenX, TrackY + 4, cz + halfLenZ,
            "minecraft:air", ct);

        // 3. Rail track down the center of the platform
        if (ew)
        {
            await rcon.SendFillAsync(cx - halfLen, TrackbedY, cz, cx + halfLen, TrackbedY, cz,
                "minecraft:stone_bricks", ct);
            await rcon.SendFillAsync(cx - halfLen, TrackY, cz, cx + halfLen, TrackY, cz,
                "minecraft:powered_rail[powered=true,shape=east_west]", ct);
        }
        else
        {
            await rcon.SendFillAsync(cx, TrackbedY, cz - halfLen, cx, TrackbedY, cz + halfLen,
                "minecraft:stone_bricks", ct);
            await rcon.SendFillAsync(cx, TrackY, cz - halfLen, cx, TrackY, cz + halfLen,
                "minecraft:powered_rail[powered=true]", ct);
        }

        // 4. Stone brick slab walkways on both sides of the track
        if (ew)
        {
            await rcon.SendFillAsync(
                cx - halfLen, TrackY, cz - halfWidth,
                cx + halfLen, TrackY, cz - 1,
                "minecraft:stone_brick_slab", ct);
            await rcon.SendFillAsync(
                cx - halfLen, TrackY, cz + 1,
                cx + halfLen, TrackY, cz + halfWidth,
                "minecraft:stone_brick_slab", ct);
        }
        else
        {
            await rcon.SendFillAsync(
                cx - halfWidth, TrackY, cz - halfLen,
                cx - 1, TrackY, cz + halfLen,
                "minecraft:stone_brick_slab", ct);
            await rcon.SendFillAsync(
                cx + 1, TrackY, cz - halfLen,
                cx + halfWidth, TrackY, cz + halfLen,
                "minecraft:stone_brick_slab", ct);
        }

        // 5. Shelter structure: oak fence posts at corners
        await rcon.SendFillAsync(cx - halfLenX, TrackY, cz - halfLenZ,
            cx - halfLenX, TrackY + 3, cz - halfLenZ, "minecraft:oak_fence", ct);
        await rcon.SendFillAsync(cx + halfLenX, TrackY, cz - halfLenZ,
            cx + halfLenX, TrackY + 3, cz - halfLenZ, "minecraft:oak_fence", ct);
        await rcon.SendFillAsync(cx - halfLenX, TrackY, cz + halfLenZ,
            cx - halfLenX, TrackY + 3, cz + halfLenZ, "minecraft:oak_fence", ct);
        await rcon.SendFillAsync(cx + halfLenX, TrackY, cz + halfLenZ,
            cx + halfLenX, TrackY + 3, cz + halfLenZ, "minecraft:oak_fence", ct);

        // 6. Shelter roof: oak slabs covering the platform
        await rcon.SendFillAsync(
            cx - halfLenX, TrackY + 3, cz - halfLenZ,
            cx + halfLenX, TrackY + 3, cz + halfLenZ,
            "minecraft:oak_slab[type=top]", ct);

        // 7–12. Signs, dispenser, and decorations (orientation-dependent)
        if (ew)
        {
            await PlaceStationDetailsEastWestAsync(cx, cz, halfLen, halfWidth, destinationName, localVillageName, ct);
        }
        else
        {
            await PlaceStationDetailsNorthSouthAsync(cx, cz, halfLen, halfWidth, destinationName, localVillageName, ct);
        }
    }

    /// <summary>
    /// Places signs, dispenser, and decorations for a north-south oriented station.
    /// Track enters from south (higher Z) and exits to north (lower Z).
    /// </summary>
    private async Task PlaceStationDetailsNorthSouthAsync(int cx, int cz, int halfLen, int halfWidth,
        string destinationName, string localVillageName, CancellationToken ct)
    {
        var truncatedDest = destinationName.Length > 15 ? destinationName[..15] : destinationName;
        var truncatedLocal = localVillageName.Length > 12 ? localVillageName[..12] : localVillageName;
        var destText = $"\"{truncatedDest}\"";
        var localText = $"\"{truncatedLocal}\"";
        var arrowText = "\"\u2191\"";
        var stationText = "\"\u00a7lStation\"";
        var arrivedText = "\"\u00a72Welcome!\"";
        var fromText = $"\"\u00a77From: {truncatedLocal}\"";
        var emptyText = "\"\"";

        // Sign support blocks at north and south ends
        await rcon.SendFillAsync(cx + halfWidth + 1, TrackY, cz - halfLen,
            cx + halfWidth + 1, TrackY + 2, cz - halfLen, "minecraft:stone_bricks", ct);
        await rcon.SendFillAsync(cx + halfWidth + 1, TrackY, cz + halfLen,
            cx + halfWidth + 1, TrackY + 2, cz + halfLen, "minecraft:stone_bricks", ct);

        // Departure sign at south end (facing west)
        await rcon.SendSetBlockAsync(cx + halfWidth + 1, TrackY + 2, cz + halfLen,
            $"minecraft:oak_wall_sign[facing=west]{{front_text:{{messages:['{stationText}','{arrowText}','{destText}',{emptyText}]}}}}", ct);

        // Arrival sign at north end (facing west)
        await rcon.SendSetBlockAsync(cx + halfWidth + 1, TrackY + 2, cz - halfLen,
            $"minecraft:oak_wall_sign[facing=west]{{front_text:{{messages:['{arrivedText}','{localText}','{fromText}',{emptyText}]}}}}", ct);

        // Button-activated minecart dispenser (on west side)
        await rcon.SendSetBlockAsync(cx - halfWidth + 1, TrackbedY, cz + halfLen - 1,
            "minecraft:dispenser[facing=up]", ct);
        await rcon.SendSetBlockAsync(cx - halfWidth + 1, TrackY, cz + halfLen - 1,
            "minecraft:stone_button[face=floor,facing=east]", ct);
        await rcon.SendCommandAsync(
            $"data merge block {cx - halfWidth + 1} {TrackbedY} {cz + halfLen - 1} " +
            "{Items:[{Slot:0b,id:\"minecraft:minecart\",count:64}]}", ct);

        var getCartText = "\"Get Minecart\"";
        var pressText = "\"Press Button\"";
        await rcon.SendSetBlockAsync(cx - halfWidth, TrackY + 1, cz + halfLen - 1,
            $"minecraft:oak_wall_sign[facing=east]{{front_text:{{messages:[{emptyText},'{getCartText}','{pressText}',{emptyText}]}}}}", ct);

        // Decorations: lanterns, benches, flower pots
        var stationDecor = new List<(int x, int y, int z, string block)>
        {
            (cx - halfWidth + 1, TrackY + 2, cz - 2, "minecraft:lantern[hanging=true]"),
            (cx - halfWidth + 1, TrackY + 2, cz + 2, "minecraft:lantern[hanging=true]"),
            (cx + halfWidth - 1, TrackY + 2, cz - 2, "minecraft:lantern[hanging=true]"),
            (cx + halfWidth - 1, TrackY + 2, cz + 2, "minecraft:lantern[hanging=true]"),
            (cx - halfWidth + 1, TrackY, cz - 2, "minecraft:oak_stairs[facing=east]"),
            (cx - halfWidth + 1, TrackY, cz + 2, "minecraft:oak_stairs[facing=east]"),
            (cx + halfWidth - 1, TrackY, cz - 2, "minecraft:oak_stairs[facing=west]"),
            (cx + halfWidth - 1, TrackY, cz + 2, "minecraft:oak_stairs[facing=west]"),
            (cx - halfWidth + 1, TrackY, cz - halfLen + 1, "minecraft:potted_red_tulip"),
            (cx + halfWidth - 1, TrackY, cz - halfLen + 1, "minecraft:potted_blue_orchid")
        };
        await rcon.SendSetBlockBatchAsync(stationDecor, ct);
    }

    /// <summary>
    /// Places signs, dispenser, and decorations for an east-west oriented station.
    /// Track runs along X axis. Signs face north/south so passengers on the walkway can read them.
    /// </summary>
    private async Task PlaceStationDetailsEastWestAsync(int cx, int cz, int halfLen, int halfWidth,
        string destinationName, string localVillageName, CancellationToken ct)
    {
        var truncatedDest = destinationName.Length > 15 ? destinationName[..15] : destinationName;
        var truncatedLocal = localVillageName.Length > 12 ? localVillageName[..12] : localVillageName;
        var destText = $"\"{truncatedDest}\"";
        var localText = $"\"{truncatedLocal}\"";
        var arrowText = "\"\u2192\""; // Right arrow for east-west travel
        var stationText = "\"\u00a7lStation\"";
        var arrivedText = "\"\u00a72Welcome!\"";
        var fromText = $"\"\u00a77From: {truncatedLocal}\"";
        var emptyText = "\"\"";

        // Sign support blocks at east and west ends (on the south side of platform)
        await rcon.SendFillAsync(cx - halfLen, TrackY, cz + halfWidth + 1,
            cx - halfLen, TrackY + 2, cz + halfWidth + 1, "minecraft:stone_bricks", ct);
        await rcon.SendFillAsync(cx + halfLen, TrackY, cz + halfWidth + 1,
            cx + halfLen, TrackY + 2, cz + halfWidth + 1, "minecraft:stone_bricks", ct);

        // Departure sign at one end (facing north into platform)
        await rcon.SendSetBlockAsync(cx + halfLen, TrackY + 2, cz + halfWidth + 1,
            $"minecraft:oak_wall_sign[facing=north]{{front_text:{{messages:['{stationText}','{arrowText}','{destText}',{emptyText}]}}}}", ct);

        // Arrival sign at other end (facing north)
        await rcon.SendSetBlockAsync(cx - halfLen, TrackY + 2, cz + halfWidth + 1,
            $"minecraft:oak_wall_sign[facing=north]{{front_text:{{messages:['{arrivedText}','{localText}','{fromText}',{emptyText}]}}}}", ct);

        // Button-activated minecart dispenser (on north side, near east end)
        await rcon.SendSetBlockAsync(cx + halfLen - 1, TrackbedY, cz - halfWidth + 1,
            "minecraft:dispenser[facing=up]", ct);
        await rcon.SendSetBlockAsync(cx + halfLen - 1, TrackY, cz - halfWidth + 1,
            "minecraft:stone_button[face=floor,facing=south]", ct);
        await rcon.SendCommandAsync(
            $"data merge block {cx + halfLen - 1} {TrackbedY} {cz - halfWidth + 1} " +
            "{Items:[{Slot:0b,id:\"minecraft:minecart\",count:64}]}", ct);

        var getCartText = "\"Get Minecart\"";
        var pressText = "\"Press Button\"";
        await rcon.SendSetBlockAsync(cx + halfLen - 1, TrackY + 1, cz - halfWidth,
            $"minecraft:oak_wall_sign[facing=south]{{front_text:{{messages:[{emptyText},'{getCartText}','{pressText}',{emptyText}]}}}}", ct);

        // Decorations: lanterns, benches, flower pots (rotated for E-W orientation)
        var stationDecor = new List<(int x, int y, int z, string block)>
        {
            (cx - 2, TrackY + 2, cz - halfWidth + 1, "minecraft:lantern[hanging=true]"),
            (cx + 2, TrackY + 2, cz - halfWidth + 1, "minecraft:lantern[hanging=true]"),
            (cx - 2, TrackY + 2, cz + halfWidth - 1, "minecraft:lantern[hanging=true]"),
            (cx + 2, TrackY + 2, cz + halfWidth - 1, "minecraft:lantern[hanging=true]"),
            (cx - 2, TrackY, cz - halfWidth + 1, "minecraft:oak_stairs[facing=south]"),
            (cx + 2, TrackY, cz - halfWidth + 1, "minecraft:oak_stairs[facing=south]"),
            (cx - 2, TrackY, cz + halfWidth - 1, "minecraft:oak_stairs[facing=north]"),
            (cx + 2, TrackY, cz + halfWidth - 1, "minecraft:oak_stairs[facing=north]"),
            (cx - halfLen + 1, TrackY, cz - halfWidth + 1, "minecraft:potted_red_tulip"),
            (cx - halfLen + 1, TrackY, cz + halfWidth - 1, "minecraft:potted_blue_orchid")
        };
        await rcon.SendSetBlockBatchAsync(stationDecor, ct);
    }

    /// <summary>
    /// Builds a cobblestone walkway from a Crossroads station back toward the plaza edge,
    /// connecting the station at CrossroadsStationRadius (35) to the plaza perimeter (30).
    /// </summary>
    private async Task GenerateCrossroadsWalkwayAsync(int stationX, int stationZ, CancellationToken ct)
    {
        // Direction from station back toward plaza center (0,0)
        int dirX = stationX == 0 ? 0 : (stationX > 0 ? -1 : 1);
        int dirZ = stationZ == 0 ? 0 : (stationZ > 0 ? -1 : 1);

        // Lay cobblestone path from station toward plaza edge
        int stationRadius = WorldConstants.CrossroadsStationRadius;
        int plazaRadius = WorldConstants.CrossroadsPlazaRadius;
        int gap = stationRadius - plazaRadius; // 5 blocks

        var pathBlocks = new List<(int x1, int y1, int z1, int x2, int y2, int z2, string block)>();
        for (int d = 1; d <= gap; d++)
        {
            int px = stationX + dirX * d;
            int pz = stationZ + dirZ * d;
            // 3-wide walkway perpendicular to the approach direction
            if (Math.Abs(dirX) >= Math.Abs(dirZ))
            {
                // Approaching along X, widen along Z
                pathBlocks.Add((px, TrackbedY, pz - 1, px, TrackbedY, pz + 1, "minecraft:cobblestone"));
            }
            else
            {
                // Approaching along Z, widen along X
                pathBlocks.Add((px - 1, TrackbedY, pz, px + 1, TrackbedY, pz, "minecraft:cobblestone"));
            }
        }

        if (pathBlocks.Count > 0)
            await rcon.SendFillBatchAsync(pathBlocks, ct);
    }

    /// <summary>
    /// Lays track between two station platforms using an L-shaped path.
    /// Rails don't support diagonal placement. The L-path direction is chosen so
    /// the LAST segment before the destination matches the destination station's orientation:
    /// - If dest is Crossroads with E-W station: Z-first then X (X approach connects to E-W station)
    /// - If dest is Crossroads with N-S station: X-first then Z (Z approach connects to N-S station)
    /// - If dest is a village (always N-S): X-first then Z (existing behavior)
    /// </summary>
    private async Task GenerateTrackPathAsync(int srcX, int srcZ, int dstX, int dstZ,
        bool destIsCrossroads, StationOrientation dstOrientation, CancellationToken ct)
    {
        logger.LogInformation("Laying track path from ({SX},{SZ}) to ({DX},{DZ})",
            srcX, srcZ, dstX, dstZ);

        int cornerX, cornerZ;

        // Choose L-path direction so final segment aligns with destination station
        bool xFirst;
        if (destIsCrossroads && dstOrientation == StationOrientation.EastWest)
        {
            // E-W station: approach along X last, so go Z-first then X
            xFirst = false;
        }
        else
        {
            // N-S station (village or Crossroads): approach along Z last, so go X-first then Z
            xFirst = true;
        }

        if (xFirst)
        {
            // Corner at (dstX, srcZ)
            cornerX = dstX;
            cornerZ = srcZ;

            await LayRailSegmentAsync(srcX, srcZ, cornerX, cornerZ, ct);

            if (srcZ != dstZ)
            {
                int startZ = cornerZ < dstZ ? cornerZ + 1 : cornerZ - 1;
                await LayRailSegmentAsync(dstX, startZ, dstX, dstZ, ct);
            }
        }
        else
        {
            // Corner at (srcX, dstZ)
            cornerX = srcX;
            cornerZ = dstZ;

            await LayRailSegmentAsync(srcX, srcZ, srcX, cornerZ, ct);

            if (srcX != dstX)
            {
                int startX = cornerX < dstX ? cornerX + 1 : cornerX - 1;
                await LayRailSegmentAsync(startX, dstZ, dstX, dstZ, ct);
            }
        }

        // Place corner rail LAST so it detects neighbors and forms a curve
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
