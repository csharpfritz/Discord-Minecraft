using Microsoft.Extensions.Logging;
using WorldGen.Worker.Models;
using WorldGen.Worker.Services;

namespace WorldGen.Worker.Generators;

/// <summary>
/// Generates minecart rail tracks between villages with station structures at each end.
/// Tracks run at y=65 (slightly elevated) with powered rails every 8 blocks.
/// Stations include departure platforms, destination signs, and button-activated minecart dispensers.
/// </summary>
public sealed class TrackGenerator(RconService rcon, ILogger<TrackGenerator> logger) : ITrackGenerator
{
    private const int TrackY = 65; // elevated trackbed
    private const int TrackbedY = 64; // support block under tracks
    private const int PoweredRailInterval = 8;
    private const int StationOffset = 30; // station distance south of village center

    // Station platform dimensions
    private const int PlatformLength = 7; // along track direction
    private const int PlatformWidth = 3; // perpendicular to track

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

        int srcPlatformZ = srcStationZ + srcPlatformOffset * (PlatformWidth + 2);
        int dstPlatformZ = dstStationZ + dstPlatformOffset * (PlatformWidth + 2);

        await GenerateStationPlatformAsync(srcStationX, srcPlatformZ,
            request.DestinationVillageName, ct);
        await GenerateStationPlatformAsync(dstStationX, dstPlatformZ,
            request.SourceVillageName, ct);

        await GenerateTrackPathAsync(
            srcStationX, srcPlatformZ,
            dstStationX, dstPlatformZ,
            ct);

        logger.LogInformation(
            "Track generation complete: '{Source}' \u2194 '{Dest}'",
            request.SourceVillageName, request.DestinationVillageName);
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
    /// Builds a departure platform with stone brick base, destination sign, and minecart dispenser.
    /// Platform runs east-west (along X), 7 blocks long, 3 blocks wide.
    /// </summary>
    private async Task GenerateStationPlatformAsync(int cx, int cz, string destinationName, CancellationToken ct)
    {
        logger.LogInformation("Generating station platform at ({X},{Z}) for destination '{Dest}'",
            cx, cz, destinationName);

        int halfLen = PlatformLength / 2; // 3
        int halfWidth = PlatformWidth / 2; // 1

        // Stone brick platform base
        await rcon.SendFillAsync(
            cx - halfLen, TrackbedY, cz - halfWidth,
            cx + halfLen, TrackbedY, cz + halfWidth,
            "minecraft:stone_bricks", ct);

        // Clear air above platform (3 blocks high for player headroom)
        await rcon.SendFillAsync(
            cx - halfLen, TrackY, cz - halfWidth,
            cx + halfLen, TrackY + 3, cz + halfWidth,
            "minecraft:air", ct);

        // Rail track down the center of the platform
        for (int x = cx - halfLen; x <= cx + halfLen; x++)
        {
            await rcon.SendSetBlockAsync(x, TrackbedY, cz, "minecraft:stone_bricks", ct);
            await rcon.SendSetBlockAsync(x, TrackY, cz, "minecraft:powered_rail[powered=true]", ct);
        }

        // Stone brick slab edges on both sides of the track (platform walkway)
        await rcon.SendFillAsync(
            cx - halfLen, TrackY, cz - halfWidth,
            cx + halfLen, TrackY, cz - halfWidth,
            "minecraft:stone_brick_slab", ct);
        await rcon.SendFillAsync(
            cx - halfLen, TrackY, cz + halfWidth,
            cx + halfLen, TrackY, cz + halfWidth,
            "minecraft:stone_brick_slab", ct);

        // Sign support blocks (signs need a solid block behind them)
        await rcon.SendSetBlockAsync(cx - halfLen - 1, TrackY, cz, "minecraft:stone_bricks", ct);
        await rcon.SendSetBlockAsync(cx - halfLen - 1, TrackY + 1, cz, "minecraft:stone_bricks", ct);
        await rcon.SendSetBlockAsync(cx + halfLen + 1, TrackY, cz, "minecraft:stone_bricks", ct);
        await rcon.SendSetBlockAsync(cx + halfLen + 1, TrackY + 1, cz, "minecraft:stone_bricks", ct);

        // Destination signs
        var truncatedDest = destinationName.Length > 15 ? destinationName[..15] : destinationName;
        var destText = $"{{\"text\":\"{truncatedDest}\"}}";
        var arrowText = "{\"text\":\"\u2192\"}";
        var stationText = "{\"text\":\"Station\"}";
        var arrivedText = "{\"text\":\"Arrived\"}";

        // Departure sign at west end
        await rcon.SendSetBlockAsync(cx - halfLen - 1, TrackY + 1, cz,
            $"minecraft:oak_wall_sign[facing=west]{{front_text:{{messages:['{stationText}','{arrowText}','{destText}','\"\"']}}}}", ct);

        // Arrival sign at east end
        await rcon.SendSetBlockAsync(cx + halfLen + 1, TrackY + 1, cz,
            $"minecraft:oak_wall_sign[facing=east]{{front_text:{{messages:['{stationText}','{arrivedText}','{destText}','\"\"']}}}}", ct);

        // Button-activated minecart dispenser at west end
        await rcon.SendSetBlockAsync(cx - halfLen, TrackbedY, cz - halfWidth,
            "minecraft:dispenser[facing=up]", ct);

        // Stone button on top of the dispenser
        await rcon.SendSetBlockAsync(cx - halfLen, TrackY, cz - halfWidth,
            "minecraft:stone_button[face=floor,facing=north]", ct);

        // Load minecarts into the dispenser
        await rcon.SendCommandAsync(
            $"data merge block {cx - halfLen} {TrackbedY} {cz - halfWidth} " +
            "{Items:[{Slot:0b,id:\"minecraft:minecart\",count:64}]}", ct);

        // Glowstone lighting at platform corners
        await rcon.SendSetBlockAsync(cx - halfLen, TrackY + 2, cz - halfWidth, "minecraft:glowstone", ct);
        await rcon.SendSetBlockAsync(cx + halfLen, TrackY + 2, cz - halfWidth, "minecraft:glowstone", ct);
        await rcon.SendSetBlockAsync(cx - halfLen, TrackY + 2, cz + halfWidth, "minecraft:glowstone", ct);
        await rcon.SendSetBlockAsync(cx + halfLen, TrackY + 2, cz + halfWidth, "minecraft:glowstone", ct);
    }

    /// <summary>
    /// Lays track between two station platforms using an L-shaped path.
    /// Rails don't support diagonal placement, so we go X-first then Z.
    /// Powered rails every 8 blocks, with redstone blocks underneath for activation.
    /// </summary>
    private async Task GenerateTrackPathAsync(int srcX, int srcZ, int dstX, int dstZ, CancellationToken ct)
    {
        logger.LogInformation("Laying track path from ({SX},{SZ}) to ({DX},{DZ})",
            srcX, srcZ, dstX, dstZ);

        // L-shaped path: travel along X first, then along Z
        int cornerX = dstX;
        int cornerZ = srcZ;

        // Segment 1: horizontal (along X axis) from source to corner
        await LayRailSegmentAsync(srcX, srcZ, cornerX, cornerZ, ct);

        // Segment 2: vertical (along Z axis) from corner to destination
        await LayRailSegmentAsync(cornerX, cornerZ, dstX, dstZ, ct);
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
