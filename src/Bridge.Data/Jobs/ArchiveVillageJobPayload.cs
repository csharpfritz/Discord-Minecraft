namespace Bridge.Data.Jobs;

public sealed record ArchiveVillageJobPayload(
    int ChannelGroupId,
    int CenterX,
    int CenterZ,
    string VillageName,
    List<ArchiveBuildingJobPayload> Buildings
);
