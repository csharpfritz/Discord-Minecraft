using System.Text.Json;
using System.Text.Json.Serialization;

namespace Bridge.Data.Events;

/// <summary>
/// Structured event published to Redis when a world generation action completes.
/// Consumed by the Discord bot to post activity embeds.
/// </summary>
public sealed record WorldActivityEvent
{
    public required string Type { get; init; }
    public required string Name { get; init; }
    public int X { get; init; }
    public int Z { get; init; }
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    private static readonly JsonSerializerOptions SerializerOptions =
        new(JsonSerializerDefaults.Web);

    public string ToJson() => JsonSerializer.Serialize(this, SerializerOptions);

    public static WorldActivityEvent? FromJson(string json) =>
        JsonSerializer.Deserialize<WorldActivityEvent>(json, SerializerOptions);
}
