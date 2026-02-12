using System.Net;
using CoreRCON;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace AppHost;

public class MinecraftHealthCheck(IConfiguration configuration) : IHealthCheck
{
    private const int RconPort = 25675;
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var password = configuration["Parameters:rcon-password"] ?? string.Empty;

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(Timeout);

        try
        {
            using var rcon = new RCON(IPAddress.Loopback, (ushort)RconPort, password);
            await rcon.ConnectAsync().WaitAsync(cts.Token);
            var response = await rcon.SendCommandAsync("seed").WaitAsync(cts.Token);
            return HealthCheckResult.Healthy($"RCON connected. Response: {response}");
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            return HealthCheckResult.Unhealthy("Minecraft RCON not ready", ex);
        }
    }
}
