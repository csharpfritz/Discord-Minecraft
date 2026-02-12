namespace Bridge.Data.Jobs;

public sealed record VillageJobPayload(
    int ChannelGroupId,
    int VillageIndex,
    int CenterX,
    int CenterZ,
    string VillageName
);
