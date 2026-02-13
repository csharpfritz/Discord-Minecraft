namespace Bridge.Data.Jobs;

/// <summary>
/// Payload for UpdateBuilding jobs that place pinned Discord message
/// displays (signs + lectern) inside a building.
/// </summary>
public sealed record UpdateBuildingJobPayload(
    int ChannelGroupId,
    int ChannelId,
    int BuildingIndex,
    int CenterX,
    int CenterZ,
    string ChannelName,
    PinData Pin
);

/// <summary>
/// Represents a pinned Discord message to display in a building.
/// </summary>
public sealed record PinData(
    string Author,
    string Content,
    DateTimeOffset Timestamp
);
