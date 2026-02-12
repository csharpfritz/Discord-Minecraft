namespace Bridge.Data.Entities;

public sealed class ChannelGroup
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public required string DiscordId { get; set; }
    public int Position { get; set; }
    public int CenterX { get; set; }
    public int CenterZ { get; set; }
    public int VillageIndex { get; set; }
    public int? VillageX { get; set; }
    public int? VillageZ { get; set; }
    public bool IsArchived { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<Channel> Channels { get; set; } = [];
}
