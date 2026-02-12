using WorldGen.Worker.Models;

namespace WorldGen.Worker.Generators;

public interface IVillageGenerator
{
    Task GenerateAsync(VillageGenerationRequest request, CancellationToken ct);
}
