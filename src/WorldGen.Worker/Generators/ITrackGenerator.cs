using WorldGen.Worker.Models;

namespace WorldGen.Worker.Generators;

public interface ITrackGenerator
{
    /// <summary>
    /// Generates minecart tracks and station structures between two villages.
    /// </summary>
    Task GenerateAsync(TrackGenerationRequest request, CancellationToken ct);
}
