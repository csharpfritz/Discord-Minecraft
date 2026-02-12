using Microsoft.Extensions.Logging;
using WorldGen.Worker.Models;
using WorldGen.Worker.Services;

namespace WorldGen.Worker.Generators;

/// <summary>
/// Archives a building in-world: updates signs to show [Archived] and blocks the entrance with barriers.
/// Does NOT destroy the structure.
/// </summary>
public sealed class BuildingArchiver(RconService rcon, ILogger<BuildingArchiver> logger) : IBuildingArchiver
{
    private const int BaseY = -60; // Superflat world surface level
    private const int Footprint = 21;
    private const int HalfFootprint = Footprint / 2; // 10
    private const int Floors = 2;
    private const int FloorHeight = 5;

    public async Task ArchiveAsync(BuildingArchiveRequest request, CancellationToken ct)
    {
        var cx = request.VillageCenterX;
        var cz = request.VillageCenterZ;

        // Compute building center from ring layout (same formula as BuildingGenerator)
        double angleRad = request.BuildingIndex * (360.0 / 16) * Math.PI / 180.0;
        int bx = cx + (int)(60 * Math.Cos(angleRad));
        int bz = cz + (int)(60 * Math.Sin(angleRad));

        logger.LogInformation(
            "Archiving building '{Name}' at ({BX}, {BZ}), index {Index}",
            request.ChannelName, bx, bz, request.BuildingIndex);

        await UpdateSignsAsync(bx, bz, request.ChannelName, ct);
        await BlockEntranceAsync(bx, bz, ct);

        logger.LogInformation("Building '{Name}' archived at ({BX}, {BZ})", request.ChannelName, bx, bz);
    }

    /// <summary>
    /// Replaces all building signs with [Archived] prefix.
    /// Mirrors the sign placement from BuildingGenerator.GenerateSignsAsync.
    /// </summary>
    private async Task UpdateSignsAsync(int bx, int bz, string channelName, CancellationToken ct)
    {
        var truncatedName = channelName.Length > 10 ? channelName[..10] : channelName;
        var archivedLabel = "{\"text\":\"[Archived]\",\"color\":\"red\"}";
        var nameText = $"{{\"text\":\"#{truncatedName}\"}}";

        // Entrance sign on south face (outside, above arch)
        int maxZ = bz + HalfFootprint;
        await rcon.SendSetBlockAsync(bx, BaseY + 5, maxZ,
            $"minecraft:oak_wall_sign[facing=south]{{front_text:{{messages:['{archivedLabel}','{nameText}','\"\"','\"\"']}}}}", ct);

        // Floor signs inside each floor (on the south interior wall)
        for (int floor = 0; floor < Floors; floor++)
        {
            int signY = BaseY + 2 + floor * FloorHeight;
            int signZ = bz + HalfFootprint - 1;

            var floorLabel = $"{{\"text\":\"Floor {floor + 1}\"}}";

            await rcon.SendSetBlockAsync(bx, signY, signZ,
                $"minecraft:oak_wall_sign[facing=north]{{front_text:{{messages:['{archivedLabel}','{floorLabel}','{nameText}','\"\"']}}}}", ct);
        }
    }

    /// <summary>
    /// Blocks the 3-wide, 3-tall south entrance with barrier blocks.
    /// Mirrors the entrance from BuildingGenerator.GenerateEntranceAsync.
    /// </summary>
    private async Task BlockEntranceAsync(int bx, int bz, CancellationToken ct)
    {
        int maxZ = bz + HalfFootprint;
        await rcon.SendFillAsync(
            bx - 1, BaseY + 1, maxZ,
            bx + 1, BaseY + 4, maxZ,
            "minecraft:barrier", ct);
    }
}
