namespace WorldGen.Worker.Models;

public record VillageGenerationRequest(
    int JobId,
    int ChannelGroupId,
    string Name,
    int VillageIndex,
    int CenterX,
    int CenterZ,
    int BuildingCount = 0 // Number of buildings in the village, used for fountain/walkway scaling
);
