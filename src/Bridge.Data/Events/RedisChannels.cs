namespace Bridge.Data.Events;

/// <summary>
/// Well-known Redis pub/sub channel names shared across services.
/// </summary>
public static class RedisChannels
{
    public const string DiscordChannel = "events:discord:channel";
    public const string MinecraftPlayer = "events:minecraft:player";
    public const string WorldActivity = "events:world:activity";
}