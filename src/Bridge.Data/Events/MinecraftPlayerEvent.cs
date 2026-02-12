using System.Text.Json;
using System.Text.Json.Serialization;

namespace Bridge.Data.Events;

/// <summary>
/// Represents a Minecraft player event published by the Paper Bridge Plugin.
/// Matches the JSON schema produced by PlayerEventListener.java.
/// </summary>
public sealed record MinecraftPlayerEvent
{
    public required string EventType { get; init; }
    public required string PlayerUuid { get; init; }
    public required string PlayerName { get; init; }
    public required DateTimeOffset Timestamp { get; init; }

    private static readonly JsonSerializerOptions SerializerOptions =
        new(JsonSerializerDefaults.Web)
        {
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
        };

    public string ToJson() => JsonSerializer.Serialize(this, SerializerOptions);

    public static MinecraftPlayerEvent? FromJson(string json) =>
        JsonSerializer.Deserialize<MinecraftPlayerEvent>(json, SerializerOptions);
}
