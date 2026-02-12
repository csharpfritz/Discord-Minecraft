using WorldGen.Worker.Models;

namespace WorldGen.Worker.Generators;

public interface IBuildingGenerator
{
    Task GenerateAsync(BuildingGenerationRequest request, CancellationToken ct);
}
