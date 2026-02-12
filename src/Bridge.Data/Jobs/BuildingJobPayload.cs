namespace Bridge.Data.Jobs;

public sealed record BuildingJobPayload(
    int ChannelGroupId,
    int ChannelId,
    int VillageIndex,
    int BuildingIndex,
    int CenterX,
    int CenterZ,
    string ChannelName,
    string VillageName
);
