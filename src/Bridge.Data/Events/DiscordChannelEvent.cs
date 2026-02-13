using System.Text.Json;
using System.Text.Json.Serialization;

namespace Bridge.Data.Events;

/// <summary>
/// Envelope for all Discord channel/category events published to Redis.
/// Serialized with camelCase via <see cref="JsonSerializerDefaults.Web"/>.
/// </summary>
public sealed record DiscordChannelEvent
{
    public required DiscordChannelEventType EventType { get; init; }
    public required DateTimeOffset Timestamp { get; init; }
    public required string GuildId { get; init; }

    // Channel fields (null for category-only events)
    public string? ChannelId { get; init; }
    public string? Name { get; init; }
    public int? Position { get; init; }

    // Category / channel-group fields
    public string? ChannelGroupId { get; init; }
    public string? ChannelGroupName { get; init; }

    // Channel topic (description)
    public string? Topic { get; init; }

    // ChannelUpdated fields
    public string? OldName { get; init; }
    public string? NewName { get; init; }

    private static readonly JsonSerializerOptions SerializerOptions =
        new(JsonSerializerDefaults.Web)
        {
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
        };

    public string ToJson() => JsonSerializer.Serialize(this, SerializerOptions);

    public static DiscordChannelEvent? FromJson(string json) =>
        JsonSerializer.Deserialize<DiscordChannelEvent>(json, SerializerOptions);
}
