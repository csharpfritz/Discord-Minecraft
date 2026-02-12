using Microsoft.Extensions.Logging;
using WorldGen.Worker.Models;
using WorldGen.Worker.Services;

namespace WorldGen.Worker.Generators;

public sealed class VillageGenerator(RconService rcon, ILogger<VillageGenerator> logger) : IVillageGenerator
{
    private const int BaseY = -60; // Superflat world surface level
    private const int PlazaRadius = 15; // 31x31 platform = radius 15 from center
    private const int WallHeight = 3;
    private const int LightSpacing = 4;

    public async Task GenerateAsync(VillageGenerationRequest request, CancellationToken ct)
    {
        var cx = request.CenterX;
        var cz = request.CenterZ;

        logger.LogInformation("Generating village '{Name}' at ({CenterX}, {CenterZ}), index {Index}",
            request.Name, cx, cz, request.VillageIndex);

        await GeneratePlatformAsync(cx, cz, ct);
        await GeneratePerimeterWallAsync(cx, cz, ct);
        await GenerateFountainAsync(cx, cz, ct);
        await GenerateLightingAsync(cx, cz, ct);
        await GenerateSignsAsync(cx, cz, request.Name, ct);
        await GenerateWelcomePathsAsync(cx, cz, ct);

        logger.LogInformation("Village '{Name}' generation complete at ({CenterX}, {CenterZ})",
            request.Name, cx, cz);
    }

    /// <summary>
    /// Stone brick platform: 31×31 at y=64
    /// </summary>
    private async Task GeneratePlatformAsync(int cx, int cz, CancellationToken ct)
    {
        logger.LogInformation("Generating 31x31 stone brick platform at y={BaseY}", BaseY);

        await rcon.SendFillAsync(
            cx - PlazaRadius, BaseY, cz - PlazaRadius,
            cx + PlazaRadius, BaseY, cz + PlazaRadius,
            "minecraft:stone_bricks", ct);
    }

    /// <summary>
    /// Perimeter wall: stone bricks, 3 blocks high with 3-wide openings at cardinal directions
    /// </summary>
    private async Task GeneratePerimeterWallAsync(int cx, int cz, CancellationToken ct)
    {
        logger.LogInformation("Generating perimeter wall, {WallHeight} blocks high", WallHeight);

        int minX = cx - PlazaRadius;
        int maxX = cx + PlazaRadius;
        int minZ = cz - PlazaRadius;
        int maxZ = cz + PlazaRadius;
        int wallTop = BaseY + WallHeight;

        // North wall (negative Z) — two segments with 3-wide gap at center
        await rcon.SendFillAsync(minX, BaseY + 1, minZ, cx - 2, wallTop, minZ, "minecraft:stone_bricks", ct);
        await rcon.SendFillAsync(cx + 2, BaseY + 1, minZ, maxX, wallTop, minZ, "minecraft:stone_bricks", ct);

        // South wall (positive Z) — two segments with gap
        await rcon.SendFillAsync(minX, BaseY + 1, maxZ, cx - 2, wallTop, maxZ, "minecraft:stone_bricks", ct);
        await rcon.SendFillAsync(cx + 2, BaseY + 1, maxZ, maxX, wallTop, maxZ, "minecraft:stone_bricks", ct);

        // West wall (negative X) — two segments with gap
        await rcon.SendFillAsync(minX, BaseY + 1, minZ, minX, wallTop, cz - 2, "minecraft:stone_bricks", ct);
        await rcon.SendFillAsync(minX, BaseY + 1, cz + 2, minX, wallTop, maxZ, "minecraft:stone_bricks", ct);

        // East wall (positive X) — two segments with gap
        await rcon.SendFillAsync(maxX, BaseY + 1, minZ, maxX, wallTop, cz - 2, "minecraft:stone_bricks", ct);
        await rcon.SendFillAsync(maxX, BaseY + 1, cz + 2, maxX, wallTop, maxZ, "minecraft:stone_bricks", ct);
    }

    /// <summary>
    /// Central fountain: water source block in a stone brick basin
    /// </summary>
    private async Task GenerateFountainAsync(int cx, int cz, CancellationToken ct)
    {
        logger.LogInformation("Generating central fountain");

        // Basin walls (3x3 outer, 1x1 water inside, raised 1 block above platform)
        await rcon.SendFillAsync(cx - 1, BaseY + 1, cz - 1, cx + 1, BaseY + 1, cz + 1, "minecraft:stone_bricks", ct);

        // Hollow out the center for water
        await rcon.SendSetBlockAsync(cx, BaseY + 1, cz, "minecraft:water", ct);
    }

    /// <summary>
    /// Lighting: glowstone at corners and along paths every 4 blocks
    /// </summary>
    private async Task GenerateLightingAsync(int cx, int cz, CancellationToken ct)
    {
        logger.LogInformation("Placing glowstone lighting");

        int minX = cx - PlazaRadius;
        int maxX = cx + PlazaRadius;
        int minZ = cz - PlazaRadius;
        int maxZ = cz + PlazaRadius;

        // Corner glowstone (on top of walls)
        await rcon.SendSetBlockAsync(minX, BaseY + WallHeight + 1, minZ, "minecraft:glowstone", ct);
        await rcon.SendSetBlockAsync(maxX, BaseY + WallHeight + 1, minZ, "minecraft:glowstone", ct);
        await rcon.SendSetBlockAsync(minX, BaseY + WallHeight + 1, maxZ, "minecraft:glowstone", ct);
        await rcon.SendSetBlockAsync(maxX, BaseY + WallHeight + 1, maxZ, "minecraft:glowstone", ct);

        // Path lighting every 4 blocks along the cardinal paths (at ground level + 1)
        for (int offset = LightSpacing; offset <= PlazaRadius; offset += LightSpacing)
        {
            // North-South path (along X = cx)
            await rcon.SendSetBlockAsync(cx - 1, BaseY + 1, cz - offset, "minecraft:glowstone", ct);
            await rcon.SendSetBlockAsync(cx + 1, BaseY + 1, cz - offset, "minecraft:glowstone", ct);
            await rcon.SendSetBlockAsync(cx - 1, BaseY + 1, cz + offset, "minecraft:glowstone", ct);
            await rcon.SendSetBlockAsync(cx + 1, BaseY + 1, cz + offset, "minecraft:glowstone", ct);

            // East-West path (along Z = cz)
            await rcon.SendSetBlockAsync(cx - offset, BaseY + 1, cz - 1, "minecraft:glowstone", ct);
            await rcon.SendSetBlockAsync(cx + offset, BaseY + 1, cz - 1, "minecraft:glowstone", ct);
            await rcon.SendSetBlockAsync(cx - offset, BaseY + 1, cz + 1, "minecraft:glowstone", ct);
            await rcon.SendSetBlockAsync(cx + offset, BaseY + 1, cz + 1, "minecraft:glowstone", ct);
        }
    }

    /// <summary>
    /// Village name sign: oak sign at center facing each cardinal direction
    /// </summary>
    private async Task GenerateSignsAsync(int cx, int cz, string villageName, CancellationToken ct)
    {
        logger.LogInformation("Placing village name signs for '{VillageName}'", villageName);

        // Signs on top of fountain basin edges, facing outward
        var truncatedName = villageName.Length > 15 ? villageName[..15] : villageName;
        var signText = $"{{\"text\":\"{truncatedName}\"}}";

        // North-facing sign (on south side of basin)
        await rcon.SendSetBlockAsync(cx, BaseY + 2, cz + 1,
            $"minecraft:oak_sign[rotation=0]{{front_text:{{messages:['\"\"','{signText}','\"\"','\"\"']}}}}", ct);

        // South-facing sign (on north side of basin)
        await rcon.SendSetBlockAsync(cx, BaseY + 2, cz - 1,
            $"minecraft:oak_sign[rotation=8]{{front_text:{{messages:['\"\"','{signText}','\"\"','\"\"']}}}}", ct);

        // East-facing sign (on west side of basin)
        await rcon.SendSetBlockAsync(cx - 1, BaseY + 2, cz,
            $"minecraft:oak_sign[rotation=4]{{front_text:{{messages:['\"\"','{signText}','\"\"','\"\"']}}}}", ct);

        // West-facing sign (on east side of basin)
        await rcon.SendSetBlockAsync(cx + 1, BaseY + 2, cz,
            $"minecraft:oak_sign[rotation=12]{{front_text:{{messages:['\"\"','{signText}','\"\"','\"\"']}}}}", ct);
    }

    /// <summary>
    /// Welcome paths: stone brick path from each cardinal opening toward the building ring
    /// </summary>
    private async Task GenerateWelcomePathsAsync(int cx, int cz, CancellationToken ct)
    {
        logger.LogInformation("Generating welcome paths from cardinal openings");

        int minX = cx - PlazaRadius;
        int maxX = cx + PlazaRadius;
        int minZ = cz - PlazaRadius;
        int maxZ = cz + PlazaRadius;

        // North path (from wall opening outward, 3 blocks wide)
        await rcon.SendFillAsync(cx - 1, BaseY, minZ - 10, cx + 1, BaseY, minZ, "minecraft:stone_bricks", ct);

        // South path
        await rcon.SendFillAsync(cx - 1, BaseY, maxZ, cx + 1, BaseY, maxZ + 10, "minecraft:stone_bricks", ct);

        // West path
        await rcon.SendFillAsync(minX - 10, BaseY, cz - 1, minX, BaseY, cz + 1, "minecraft:stone_bricks", ct);

        // East path
        await rcon.SendFillAsync(maxX, BaseY, cz - 1, maxX + 10, BaseY, cz + 1, "minecraft:stone_bricks", ct);
    }
}
