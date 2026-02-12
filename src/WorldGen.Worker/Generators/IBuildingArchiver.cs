using WorldGen.Worker.Models;

namespace WorldGen.Worker.Generators;

public interface IBuildingArchiver
{
    Task ArchiveAsync(BuildingArchiveRequest request, CancellationToken ct);
}
