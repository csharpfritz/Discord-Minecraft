using System.Net.Http.Json;

namespace WorldGen.Worker.Services;

/// <summary>
/// Calls the Bridge Plugin's BlueMap marker HTTP endpoints.
/// All methods are fire-and-forget safe — exceptions are caught and logged.
/// </summary>
public sealed class MarkerService(HttpClient httpClient, ILogger<MarkerService> logger)
{
    public async Task SetVillageMarkerAsync(string villageId, string label, int x, int z, CancellationToken ct)
    {
        var payload = new { id = villageId, label, x, z };
        try
        {
            var response = await httpClient.PostAsJsonAsync("/api/markers/village", payload, ct);
            response.EnsureSuccessStatusCode();
            logger.LogInformation("Set village marker '{Label}' at ({X},{Z})", label, x, z);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to set village marker '{Label}' — BlueMap may not be available", label);
        }
    }

    public async Task SetBuildingMarkerAsync(string buildingId, string label, int x, int z, CancellationToken ct)
    {
        var payload = new { id = buildingId, label, x, z };
        try
        {
            var response = await httpClient.PostAsJsonAsync("/api/markers/building", payload, ct);
            response.EnsureSuccessStatusCode();
            logger.LogInformation("Set building marker '{Label}' at ({X},{Z})", label, x, z);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to set building marker '{Label}' — BlueMap may not be available", label);
        }
    }

    public async Task ArchiveBuildingMarkerAsync(string buildingId, CancellationToken ct)
    {
        var payload = new { id = buildingId };
        try
        {
            var response = await httpClient.PostAsJsonAsync("/api/markers/building/archive", payload, ct);
            response.EnsureSuccessStatusCode();
            logger.LogInformation("Archived building marker '{Id}'", buildingId);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to archive building marker '{Id}' — BlueMap may not be available", buildingId);
        }
    }

    public async Task ArchiveVillageMarkerAsync(string villageId, CancellationToken ct)
    {
        var payload = new { id = villageId };
        try
        {
            var response = await httpClient.PostAsJsonAsync("/api/markers/village/archive", payload, ct);
            response.EnsureSuccessStatusCode();
            logger.LogInformation("Archived village marker '{Id}'", villageId);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to archive village marker '{Id}' — BlueMap may not be available", villageId);
        }
    }
}
