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

    // Village fence: encompasses all buildings with 3-block buffer
    // New "main street" layout: buildings in two rows at ±20 Z, max ~100 X from center
    // Building footprint is 21, so max edge is ~110 X and ~30 Z
    // Fence at 120 blocks X and 60 blocks Z (rectangular) would be ideal, but using circular fence
    // Fence at 75 blocks gives buffer around the tighter building layout
    private const int FenceRadius = 75;

    public async Task GenerateAsync(VillageGenerationRequest request, CancellationToken ct)
    {
        var cx = request.CenterX;
        var cz = request.CenterZ;

        logger.LogInformation("Generating village '{Name}' at ({CenterX}, {CenterZ}), index {Index}",
            request.Name, cx, cz, request.VillageIndex);

        // Forceload chunks covering the village fence perimeter before placing blocks
        int radius = FenceRadius + 5; // include fence and gates
        int minChunkX = (cx - radius) >> 4;
        int maxChunkX = (cx + radius) >> 4;
        int minChunkZ = (cz - radius) >> 4;
        int maxChunkZ = (cz + radius) >> 4;
        await rcon.SendCommandAsync($"forceload add {minChunkX << 4} {minChunkZ << 4} {maxChunkX << 4} {maxChunkZ << 4}", ct);

        await GeneratePlatformAsync(cx, cz, ct);
        await GeneratePerimeterWallAsync(cx, cz, ct);
        
        // Build large fountain if building count is known and >= 4, otherwise simple fountain
        // (Village created before buildings → buildingCount defaults to 0 → simple fountain initially)
        bool largeFountain = request.BuildingCount >= 4;
        await GenerateFountainAsync(cx, cz, largeFountain, ct);
        
        // Perimeter walkway always built; building walkways created when buildings are added
        await GeneratePerimeterWalkwayAsync(cx, cz, ct);
        
        await GenerateLightingAsync(cx, cz, ct);
        await GenerateSignsAsync(cx, cz, request.Name, ct);
        await GenerateWelcomePathsAsync(cx, cz, ct);
        await GenerateVillageFenceAsync(cx, cz, ct);

        // Release forceloaded chunks
        await rcon.SendCommandAsync($"forceload remove {minChunkX << 4} {minChunkZ << 4} {maxChunkX << 4} {maxChunkZ << 4}", ct);

        logger.LogInformation("Village '{Name}' generation complete at ({CenterX}, {CenterZ})",
            request.Name, cx, cz);
    }

    /// <summary>
    /// Stone brick platform: 31×31 at y=-60 (superflat surface level)
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
    /// Central fountain: scales based on village size.
    /// Small villages: simple 3x3 basin with water
    /// Larger villages (4+ buildings): 7x7 decorative fountain with center pillar
    /// </summary>
    private async Task GenerateFountainAsync(int cx, int cz, bool largeFountain, CancellationToken ct)
    {
        if (largeFountain)
        {
            logger.LogInformation("Generating large decorative fountain (7x7)");
            
            // Large fountain: 7x7 stone brick base
            await rcon.SendFillAsync(cx - 3, BaseY, cz - 3, cx + 3, BaseY, cz + 3, "minecraft:stone_bricks", ct);
            
            // Raised basin walls (outer ring at y+1)
            await rcon.SendFillAsync(cx - 3, BaseY + 1, cz - 3, cx + 3, BaseY + 1, cz - 3, "minecraft:stone_brick_slab", ct);
            await rcon.SendFillAsync(cx - 3, BaseY + 1, cz + 3, cx + 3, BaseY + 1, cz + 3, "minecraft:stone_brick_slab", ct);
            await rcon.SendFillAsync(cx - 3, BaseY + 1, cz - 3, cx - 3, BaseY + 1, cz + 3, "minecraft:stone_brick_slab", ct);
            await rcon.SendFillAsync(cx + 3, BaseY + 1, cz - 3, cx + 3, BaseY + 1, cz + 3, "minecraft:stone_brick_slab", ct);
            
            // Water pool inside (5x5 area)
            await rcon.SendFillAsync(cx - 2, BaseY + 1, cz - 2, cx + 2, BaseY + 1, cz + 2, "minecraft:water", ct);
            
            // Center pillar — stone bricks with glowstone cap
            await rcon.SendFillAsync(cx, BaseY + 1, cz, cx, BaseY + 3, cz, "minecraft:stone_bricks", ct);
            await rcon.SendSetBlockAsync(cx, BaseY + 4, cz, "minecraft:glowstone", ct);
        }
        else
        {
            logger.LogInformation("Generating simple fountain (3x3)");

            // Basin walls (3x3 outer, 1x1 water inside, raised 1 block above platform)
            await rcon.SendFillAsync(cx - 1, BaseY + 1, cz - 1, cx + 1, BaseY + 1, cz + 1, "minecraft:stone_bricks", ct);

            // Hollow out the center for water
            await rcon.SendSetBlockAsync(cx, BaseY + 1, cz, "minecraft:water", ct);
        }
    }

    /// <summary>
    /// Perimeter walkway: cobblestone path around the inside edge of the fence.
    /// Building-specific walkways are generated by BuildingGenerator.
    /// </summary>
    private async Task GeneratePerimeterWalkwayAsync(int cx, int cz, CancellationToken ct)
    {
        logger.LogInformation("Generating perimeter cobblestone walkway");
        
        // Perimeter path inside the fence (3 blocks wide, at FenceRadius - 5)
        int perimeterRadius = FenceRadius - 5;
        
        // North perimeter path
        await rcon.SendFillAsync(cx - perimeterRadius, BaseY, cz - perimeterRadius, cx + perimeterRadius, BaseY, cz - perimeterRadius + 2, "minecraft:cobblestone", ct);
        
        // South perimeter path
        await rcon.SendFillAsync(cx - perimeterRadius, BaseY, cz + perimeterRadius - 2, cx + perimeterRadius, BaseY, cz + perimeterRadius, "minecraft:cobblestone", ct);
        
        // West perimeter path
        await rcon.SendFillAsync(cx - perimeterRadius, BaseY, cz - perimeterRadius, cx - perimeterRadius + 2, BaseY, cz + perimeterRadius, "minecraft:cobblestone", ct);
        
        // East perimeter path  
        await rcon.SendFillAsync(cx + perimeterRadius - 2, BaseY, cz - perimeterRadius, cx + perimeterRadius, BaseY, cz + perimeterRadius, "minecraft:cobblestone", ct);
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

    /// <summary>
    /// Village perimeter fence: oak fence around the entire village (outside all buildings)
    /// with fence gates at the 4 cardinal entrances.
    /// </summary>
    private async Task GenerateVillageFenceAsync(int cx, int cz, CancellationToken ct)
    {
        logger.LogInformation("Generating village perimeter fence at radius {FenceRadius}", FenceRadius);

        int minX = cx - FenceRadius;
        int maxX = cx + FenceRadius;
        int minZ = cz - FenceRadius;
        int maxZ = cz + FenceRadius;

        // North fence — oak fence with gap for gate at center
        await rcon.SendFillAsync(minX, BaseY + 1, minZ, cx - 2, BaseY + 1, minZ, "minecraft:oak_fence", ct);
        await rcon.SendFillAsync(cx + 2, BaseY + 1, minZ, maxX, BaseY + 1, minZ, "minecraft:oak_fence", ct);

        // South fence — oak fence with gap for gate
        await rcon.SendFillAsync(minX, BaseY + 1, maxZ, cx - 2, BaseY + 1, maxZ, "minecraft:oak_fence", ct);
        await rcon.SendFillAsync(cx + 2, BaseY + 1, maxZ, maxX, BaseY + 1, maxZ, "minecraft:oak_fence", ct);

        // West fence — oak fence with gap for gate
        await rcon.SendFillAsync(minX, BaseY + 1, minZ, minX, BaseY + 1, cz - 2, "minecraft:oak_fence", ct);
        await rcon.SendFillAsync(minX, BaseY + 1, cz + 2, minX, BaseY + 1, maxZ, "minecraft:oak_fence", ct);

        // East fence — oak fence with gap for gate
        await rcon.SendFillAsync(maxX, BaseY + 1, minZ, maxX, BaseY + 1, cz - 2, "minecraft:oak_fence", ct);
        await rcon.SendFillAsync(maxX, BaseY + 1, cz + 2, maxX, BaseY + 1, maxZ, "minecraft:oak_fence", ct);

        // Fence gates at cardinal entrances (3-wide gates)
        // North gate
        await rcon.SendFillAsync(cx - 1, BaseY + 1, minZ, cx + 1, BaseY + 1, minZ, "minecraft:oak_fence_gate[facing=north]", ct);

        // South gate
        await rcon.SendFillAsync(cx - 1, BaseY + 1, maxZ, cx + 1, BaseY + 1, maxZ, "minecraft:oak_fence_gate[facing=south]", ct);

        // West gate
        await rcon.SendFillAsync(minX, BaseY + 1, cz - 1, minX, BaseY + 1, cz + 1, "minecraft:oak_fence_gate[facing=west]", ct);

        // East gate
        await rcon.SendFillAsync(maxX, BaseY + 1, cz - 1, maxX, BaseY + 1, cz + 1, "minecraft:oak_fence_gate[facing=east]", ct);

        // Corner fence posts with lanterns for visibility
        await rcon.SendSetBlockAsync(minX, BaseY + 1, minZ, "minecraft:oak_fence", ct);
        await rcon.SendSetBlockAsync(maxX, BaseY + 1, minZ, "minecraft:oak_fence", ct);
        await rcon.SendSetBlockAsync(minX, BaseY + 1, maxZ, "minecraft:oak_fence", ct);
        await rcon.SendSetBlockAsync(maxX, BaseY + 1, maxZ, "minecraft:oak_fence", ct);

        await rcon.SendSetBlockAsync(minX, BaseY + 2, minZ, "minecraft:lantern[hanging=false]", ct);
        await rcon.SendSetBlockAsync(maxX, BaseY + 2, minZ, "minecraft:lantern[hanging=false]", ct);
        await rcon.SendSetBlockAsync(minX, BaseY + 2, maxZ, "minecraft:lantern[hanging=false]", ct);
        await rcon.SendSetBlockAsync(maxX, BaseY + 2, maxZ, "minecraft:lantern[hanging=false]", ct);
    }
}
