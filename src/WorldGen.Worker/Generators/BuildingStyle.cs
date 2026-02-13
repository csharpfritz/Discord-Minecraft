namespace WorldGen.Worker.Generators;

/// <summary>
/// Architectural style for generated buildings.
/// Style is selected deterministically from the channel ID.
/// </summary>
public enum BuildingStyle
{
    MedievalCastle = 0,
    TimberCottage = 1,
    StoneWatchtower = 2,
}
