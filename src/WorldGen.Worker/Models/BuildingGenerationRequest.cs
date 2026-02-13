namespace WorldGen.Worker.Models;

public record BuildingGenerationRequest(
    int JobId,
    int ChannelGroupId,
    int ChannelId,
    int VillageCenterX,
    int VillageCenterZ,
    int BuildingIndex,
    string Name,
    string? ChannelTopic = null,
    int MemberCount = 0
);
