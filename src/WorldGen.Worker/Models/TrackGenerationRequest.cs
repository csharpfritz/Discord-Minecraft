namespace WorldGen.Worker.Models;

/// <summary>
/// Request to generate minecart tracks between two villages.
/// </summary>
public record TrackGenerationRequest(
    int JobId,
    int SourceChannelGroupId,
    int DestinationChannelGroupId,
    string SourceVillageName,
    string DestinationVillageName,
    int SourceCenterX,
    int SourceCenterZ,
    int DestCenterX,
    int DestCenterZ
);
