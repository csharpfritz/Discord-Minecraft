using Microsoft.Extensions.Logging;
using WorldGen.Worker.Models;
using WorldGen.Worker.Services;

namespace WorldGen.Worker.Generators;

/// <summary>
/// Generates minecart rail tracks between villages with station structures at each end.
/// Tracks run at Y=-59 (1 above superflat surface) with powered rails every 8 blocks.
/// Stations include covered platforms with departure signs, destination maps, and minecart dispensers.
/// </summary>
public sealed class TrackGenerator(RconService rcon, ILogger<TrackGenerator> logger) : ITrackGenerator
{
    private const int TrackY = -59; // elevated trackbed (superflat surface + 1)
    private const int TrackbedY = -60; // support block under tracks (superflat surface)
    private const int PoweredRailInterval = 8;
    private const int StationOffset = 30; // station distance south of village center

    // Station platform dimensions
    private const int PlatformLength = 9; // along track direction (expanded for shelter)
    private const int PlatformWidth = 5; // perpendicular to track (wider for amenities)

    public async Task GenerateAsync(TrackGenerationRequest request, CancellationToken ct)
    {
        logger.LogInformation(
            "Generating track from '{Source}' ({SX},{SZ}) to '{Dest}' ({DX},{DZ})",
            request.SourceVillageName, request.SourceCenterX, request.SourceCenterZ,
            request.DestinationVillageName, request.DestCenterX, request.DestCenterZ);

        // Station positions: south edge of each village
        int srcStationX = request.SourceCenterX;
        int srcStationZ = request.SourceCenterZ + StationOffset;
        int dstStationX = request.DestCenterX;
        int dstStationZ = request.DestCenterZ + StationOffset;

        // Determine unique platform Z offset per destination to avoid overlapping platforms
        int srcPlatformOffset = GetPlatformOffset(request.SourceCenterX, request.SourceCenterZ,
            request.DestCenterX, request.DestCenterZ);
        int dstPlatformOffset = GetPlatformOffset(request.DestCenterX, request.DestCenterZ,
            request.SourceCenterX, request.SourceCenterZ);

        int srcPlatformZ = srcStationZ + srcPlatformOffset * (PlatformWidth + 3);
        int dstPlatformZ = dstStationZ + dstPlatformOffset * (PlatformWidth + 3);

        // Forceload all chunks along the track path before placing blocks
        // Tracks can span large distances — forceload in segments
        await ForceloadTrackRegionAsync(srcStationX, srcPlatformZ, dstStationX, dstPlatformZ, add: true, ct);

        await GenerateStationPlatformAsync(srcStationX, srcPlatformZ,
            request.DestinationVillageName, request.SourceVillageName, ct);
        await GenerateStationPlatformAsync(dstStationX, dstPlatformZ,
            request.SourceVillageName, request.DestinationVillageName, ct);

        await GenerateTrackPathAsync(
            srcStationX, srcPlatformZ,
            dstStationX, dstPlatformZ,
            ct);

        // Release forceloaded chunks
        await ForceloadTrackRegionAsync(srcStationX, srcPlatformZ, dstStationX, dstPlatformZ, add: false, ct);

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
    /// Generates a deterministic platform offset index based on the direction to the destination.
    /// Uses angle-based hashing so each destination gets a unique slot.
    /// </summary>
    private static int GetPlatformOffset(int fromX, int fromZ, int toX, int toZ)
    {
        double angle = Math.Atan2(toZ - fromZ, toX - fromX);
        // Map angle [-PI, PI] to [0, 2PI], then to a slot index
        if (angle < 0) angle += 2 * Math.PI;
        int slot = (int)(angle / (2 * Math.PI) * 8); // up to 8 platform slots
        return slot;
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
        for (int z = cz - halfLen; z <= cz + halfLen; z++)
        {
            await rcon.SendSetBlockAsync(cx, TrackbedY, z, "minecraft:stone_bricks", ct);
            await rcon.SendSetBlockAsync(cx, TrackY, z, "minecraft:powered_rail[powered=true]", ct);
        }

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

        // 7. Sign support blocks at north and south ends (outside the track path for clear entry/exit)
        // Place supports on the EAST side so they don't block track
        await rcon.SendSetBlockAsync(cx + halfWidth + 1, TrackY, cz - halfLen, "minecraft:stone_bricks", ct);
        await rcon.SendSetBlockAsync(cx + halfWidth + 1, TrackY + 1, cz - halfLen, "minecraft:stone_bricks", ct);
        await rcon.SendSetBlockAsync(cx + halfWidth + 1, TrackY + 2, cz - halfLen, "minecraft:stone_bricks", ct);
        await rcon.SendSetBlockAsync(cx + halfWidth + 1, TrackY, cz + halfLen, "minecraft:stone_bricks", ct);
        await rcon.SendSetBlockAsync(cx + halfWidth + 1, TrackY + 1, cz + halfLen, "minecraft:stone_bricks", ct);
        await rcon.SendSetBlockAsync(cx + halfWidth + 1, TrackY + 2, cz + halfLen, "minecraft:stone_bricks", ct);

        // 8. Destination signs with village names — plain quoted strings, NOT JSON objects
        var truncatedDest = destinationName.Length > 15 ? destinationName[..15] : destinationName;
        var truncatedLocal = localVillageName.Length > 12 ? localVillageName[..12] : localVillageName;
        var destText = $"\"{truncatedDest}\"";
        var localText = $"\"{truncatedLocal}\"";
        var arrowText = "\"↑\""; // North arrow for north-south travel
        var stationText = "\"§lStation\""; // Bold "Station"
        var arrivedText = "\"§2Welcome!\""; // Green "Welcome!"
        var fromText = $"\"§7From: {truncatedLocal}\""; // Gray "From:"
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

        // 10. Lantern lighting under the shelter roof (warm lighting)
        await rcon.SendSetBlockAsync(cx - halfWidth + 1, TrackY + 2, cz - 2, "minecraft:lantern[hanging=true]", ct);
        await rcon.SendSetBlockAsync(cx - halfWidth + 1, TrackY + 2, cz + 2, "minecraft:lantern[hanging=true]", ct);
        await rcon.SendSetBlockAsync(cx + halfWidth - 1, TrackY + 2, cz - 2, "minecraft:lantern[hanging=true]", ct);
        await rcon.SendSetBlockAsync(cx + halfWidth - 1, TrackY + 2, cz + 2, "minecraft:lantern[hanging=true]", ct);

        // 11. Decorative benches (stairs facing inward toward track) on east and west sides
        await rcon.SendSetBlockAsync(cx - halfWidth + 1, TrackY, cz - 2,
            "minecraft:oak_stairs[facing=east]", ct);
        await rcon.SendSetBlockAsync(cx - halfWidth + 1, TrackY, cz + 2,
            "minecraft:oak_stairs[facing=east]", ct);
        await rcon.SendSetBlockAsync(cx + halfWidth - 1, TrackY, cz - 2,
            "minecraft:oak_stairs[facing=west]", ct);
        await rcon.SendSetBlockAsync(cx + halfWidth - 1, TrackY, cz + 2,
            "minecraft:oak_stairs[facing=west]", ct);

        // 12. Flower pot decorations near shelter posts (welcoming touch)
        await rcon.SendSetBlockAsync(cx - halfWidth + 1, TrackY, cz - halfLen + 1,
            "minecraft:potted_red_tulip", ct);
        await rcon.SendSetBlockAsync(cx + halfWidth - 1, TrackY, cz - halfLen + 1,
            "minecraft:potted_blue_orchid", ct);
    }

    /// <summary>
    /// Lays track between two station platforms using an L-shaped path with collision-safe offset.
    /// Rails don't support diagonal placement. Since stations run north-south, we go Z-first then X.
    /// The track path is offset by a hash of both station coordinates to minimize track collisions.
    /// IMPORTANT: Rail segments MUST OVERLAP at corners for Minecraft rails to auto-connect into curves.
    /// </summary>
    private async Task GenerateTrackPathAsync(int srcX, int srcZ, int dstX, int dstZ, CancellationToken ct)
    {
        logger.LogInformation("Laying track path from ({SX},{SZ}) to ({DX},{DZ})",
            srcX, srcZ, dstX, dstZ);

        // Calculate a track-specific X offset to reduce collisions when tracks cross
        // Different village pairs will use slightly different corner X values
        int trackOffset = GetTrackCornerOffset(srcX, srcZ, dstX, dstZ);

        // L-shaped path: travel along Z first (matching station orientation), then along X
        int cornerZ = dstZ;
        int cornerX = srcX + trackOffset;

        // Segment 1: vertical (along Z axis) from source station going north/south
        // EXTEND to cornerZ to reach the corner block
        await LayRailSegmentAsync(srcX, srcZ, srcX, cornerZ, ct);

        // Short connector from srcX to offset cornerX if needed
        if (trackOffset != 0)
        {
            // This segment includes both srcX,cornerZ and cornerX,cornerZ
            await LayRailSegmentAsync(srcX, cornerZ, cornerX, cornerZ, ct);
        }

        // Segment 2: horizontal (along X axis) from corner to destination station
        // START from cornerX (overlapping at the corner block) so the corner rail sees both directions
        await LayRailSegmentAsync(cornerX, cornerZ, dstX, dstZ, ct);

        // Place corner rail LAST so it can detect neighbors from both directions
        // This ensures Minecraft creates a curved rail at the L-shaped turn
        await PlaceCornerRailAsync(cornerX, cornerZ, ct);
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
    /// Generates a small offset (0-3 blocks) based on track endpoints to reduce track overlap.
    /// Tracks between different village pairs will likely use different Z offsets at corners.
    /// </summary>
    private static int GetTrackCornerOffset(int srcX, int srcZ, int dstX, int dstZ)
    {
        // Simple hash of coordinates to produce 0-3 offset
        int hash = Math.Abs(srcX ^ srcZ ^ dstX ^ dstZ);
        return (hash % 4) - 2; // Range: -2 to +1
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

        // Place rails block-by-block: powered every 8, regular otherwise
        for (int i = start; i <= end; i++)
        {
            int x = isHorizontal ? i : fixedCoord;
            int z = isHorizontal ? fixedCoord : i;

            bool isPowered = (i - start) % PoweredRailInterval == 0;

            if (isPowered)
            {
                // Redstone block under powered rail for permanent activation
                await rcon.SendSetBlockAsync(x, TrackbedY, z,
                    "minecraft:redstone_block", ct);
                await rcon.SendSetBlockAsync(x, TrackY, z,
                    "minecraft:powered_rail[powered=true]", ct);
            }
            else
            {
                await rcon.SendSetBlockAsync(x, TrackY, z,
                    "minecraft:rail", ct);
            }
        }
    }
}
