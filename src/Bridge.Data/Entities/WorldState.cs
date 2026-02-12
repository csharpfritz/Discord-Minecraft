namespace Bridge.Data.Entities;

public sealed class WorldState
{
    public int Id { get; set; }
    public required string Key { get; set; }
    public int VillageCenterX { get; set; }
    public int VillageCenterZ { get; set; }
    public int BuildingWidth { get; set; }
    public int BuildingDepth { get; set; }
    public int BuildingHeight { get; set; }
    public int? TrackStartX { get; set; }
    public int? TrackStartZ { get; set; }
    public int? TrackEndX { get; set; }
    public int? TrackEndZ { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
