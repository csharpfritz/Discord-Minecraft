namespace WorldGen.Worker.Models;

public record BuildingArchiveRequest(
    int JobId,
    int ChannelGroupId,
    int ChannelId,
    int BuildingIndex,
    int VillageCenterX,
    int VillageCenterZ,
    string ChannelName
);
