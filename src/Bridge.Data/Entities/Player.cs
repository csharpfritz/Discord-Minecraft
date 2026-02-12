namespace Bridge.Data.Entities;

public sealed class Player
{
    public int Id { get; set; }
    public required string DiscordId { get; set; }
    public string? MinecraftUuid { get; set; }
    public string? MinecraftUsername { get; set; }
    public int? LastLocationX { get; set; }
    public int? LastLocationY { get; set; }
    public int? LastLocationZ { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LinkedAt { get; set; }
}
