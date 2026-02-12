namespace Bridge.Data.Jobs;

public sealed record ArchiveBuildingJobPayload(
    int ChannelGroupId,
    int ChannelId,
    int BuildingIndex,
    int CenterX,
    int CenterZ,
    string ChannelName
);
