using System.Text.Json;
using System.Text.Json.Serialization;

namespace Acceptance.Tests.Infrastructure;

/// <summary>
/// Client for querying BlueMap's static JSON data.
///
/// BlueMap doesn't expose a REST API — it serves static JSON files
/// that the web frontend loads. This client fetches those files
/// to verify markers and map state.
///
/// Key endpoints:
/// - /data/markers.json — all marker sets (villages, buildings)
/// - /maps/ — list of available maps
/// - /maps/{mapId}/settings.json — map configuration
/// </summary>
public sealed class BlueMapClient
{
    private readonly HttpClient _http;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public BlueMapClient(HttpClient http)
    {
        _http = http;
    }

    /// <summary>
    /// Fetches the main markers JSON file.
    /// Returns null if the file doesn't exist yet (BlueMap not fully rendered).
    /// </summary>
    public async Task<BlueMapMarkersResponse?> GetMarkersAsync(string mapId = "world")
    {
        try
        {
            // BlueMap may store markers per-map or globally depending on version
            // Try per-map first
            var response = await _http.GetAsync($"/maps/{mapId}/markers.json");
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<BlueMapMarkersResponse>(json, JsonOptions);
            }

            // Fall back to global markers
            response = await _http.GetAsync("/data/markers.json");
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<BlueMapMarkersResponse>(json, JsonOptions);
            }

            return null;
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    /// <summary>
    /// Fetches available maps from BlueMap.
    /// </summary>
    public async Task<List<string>> GetMapIdsAsync()
    {
        try
        {
            var response = await _http.GetAsync("/maps.json");
            if (!response.IsSuccessStatusCode) return [];

            var json = await response.Content.ReadAsStringAsync();
            var maps = JsonSerializer.Deserialize<List<BlueMapMapInfo>>(json, JsonOptions);
            return maps?.Select(m => m.Id).ToList() ?? [];
        }
        catch (HttpRequestException)
        {
            return [];
        }
    }

    /// <summary>
    /// Checks if BlueMap's web server is responding.
    /// </summary>
    public async Task<bool> IsReadyAsync()
    {
        try
        {
            var response = await _http.GetAsync("/");
            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException)
        {
            return false;
        }
    }

    /// <summary>
    /// Gets village markers from the discord-villages marker set.
    /// </summary>
    public async Task<List<BlueMapMarker>> GetVillageMarkersAsync(string mapId = "world")
    {
        var markers = await GetMarkersAsync(mapId);
        if (markers?.MarkerSets == null) return [];

        if (markers.MarkerSets.TryGetValue("discord-villages", out var villageSet))
        {
            return villageSet.Markers?.Values.ToList() ?? [];
        }

        return [];
    }

    /// <summary>
    /// Gets building markers from the discord-buildings marker set.
    /// </summary>
    public async Task<List<BlueMapMarker>> GetBuildingMarkersAsync(string mapId = "world")
    {
        var markers = await GetMarkersAsync(mapId);
        if (markers?.MarkerSets == null) return [];

        if (markers.MarkerSets.TryGetValue("discord-buildings", out var buildingSet))
        {
            return buildingSet.Markers?.Values.ToList() ?? [];
        }

        return [];
    }
}

/// <summary>
/// Top-level response from markers.json
/// </summary>
public sealed class BlueMapMarkersResponse
{
    [JsonPropertyName("markerSets")]
    public Dictionary<string, BlueMapMarkerSet>? MarkerSets { get; set; }
}

/// <summary>
/// A named set of markers (e.g., "discord-villages", "discord-buildings")
/// </summary>
public sealed class BlueMapMarkerSet
{
    public string? Label { get; set; }
    public bool Toggleable { get; set; } = true;
    public bool DefaultHidden { get; set; } = false;
    public int Sorting { get; set; } = 0;
    public Dictionary<string, BlueMapMarker>? Markers { get; set; }
}

/// <summary>
/// Individual marker data (POI, Shape, etc.)
/// </summary>
public sealed class BlueMapMarker
{
    public string Type { get; set; } = "poi";
    public string? Label { get; set; }
    public BlueMapPosition? Position { get; set; }
    public string? Detail { get; set; }
    public string? Icon { get; set; }
    public bool Listed { get; set; } = true;
}

/// <summary>
/// 3D position in Minecraft world
/// </summary>
public sealed class BlueMapPosition
{
    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }
}

/// <summary>
/// Map info from maps.json
/// </summary>
public sealed class BlueMapMapInfo
{
    public string Id { get; set; } = string.Empty;
    public string? Name { get; set; }
    public string? World { get; set; }
}
