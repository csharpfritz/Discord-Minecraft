namespace Bridge.Data.Jobs;

public sealed record TrackJobPayload(
    int SourceChannelGroupId,
    int DestinationChannelGroupId,
    string SourceVillageName,
    string DestinationVillageName,
    int SourceCenterX,
    int SourceCenterZ,
    int DestCenterX,
    int DestCenterZ
);
