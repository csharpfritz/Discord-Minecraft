using System.Text.Json;
using System.Text.Json.Serialization;

namespace Bridge.Data.Jobs;

/// <summary>
/// Job envelope pushed to Redis list <c>queue:worldgen</c>.
/// </summary>
public sealed record WorldGenJob
{
    public required WorldGenJobType JobType { get; init; }
    public required int JobId { get; init; }
    public required string Payload { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    private static readonly JsonSerializerOptions SerializerOptions =
        new(JsonSerializerDefaults.Web)
        {
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
        };

    public string ToJson() => JsonSerializer.Serialize(this, SerializerOptions);

    public static WorldGenJob? FromJson(string json) =>
        JsonSerializer.Deserialize<WorldGenJob>(json, SerializerOptions);
}
