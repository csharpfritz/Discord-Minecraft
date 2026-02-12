namespace WorldGen.Worker.Models;

public record VillageGenerationRequest(
    int JobId,
    int ChannelGroupId,
    string Name,
    int VillageIndex,
    int CenterX,
    int CenterZ
);
