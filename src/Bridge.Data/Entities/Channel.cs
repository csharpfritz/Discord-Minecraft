namespace Bridge.Data.Entities;

public sealed class Channel
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public required string DiscordId { get; set; }
    public int ChannelGroupId { get; set; }
    public int BuildingIndex { get; set; }
    public int CoordinateX { get; set; }
    public int CoordinateZ { get; set; }
    public int? BuildingX { get; set; }
    public int? BuildingZ { get; set; }
    public bool IsArchived { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ChannelGroup ChannelGroup { get; set; } = null!;
}
