using System.Net;
using CoreRCON;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace WorldGen.Worker.Services;

public sealed class RconService : IAsyncDisposable
{
    private readonly ILogger<RconService> _logger;
    private readonly string _host;
    private readonly ushort _port;
    private readonly string _password;
    private readonly int _commandDelayMs;
    private RCON? _rcon;
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public RconService(IConfiguration configuration, ILogger<RconService> logger)
    {
        _logger = logger;
        var rawHost = configuration["Rcon:Host"] ?? "localhost";
        if (Uri.TryCreate(rawHost, UriKind.Absolute, out var uri))
        {
            _host = uri.Host;
            _port = uri.Port > 0 ? (ushort)uri.Port : ushort.Parse(configuration["Rcon:Port"] ?? "25575");
        }
        else
        {
            _host = rawHost;
            _port = ushort.Parse(configuration["Rcon:Port"] ?? "25575");
        }
        _password = configuration["Rcon:Password"] ?? throw new InvalidOperationException("Rcon:Password is required");
        _commandDelayMs = int.Parse(configuration["Rcon:CommandDelayMs"] ?? "50");
    }

    private async Task<RCON> GetConnectionAsync()
    {
        if (_rcon is not null)
            return _rcon;

        var addresses = await Dns.GetHostAddressesAsync(_host);
        if (addresses.Length == 0)
            throw new InvalidOperationException($"Could not resolve RCON host: {_host}");

        _logger.LogInformation("Connecting to RCON at {Host}:{Port} (resolved: {Address})", _host, _port, addresses[0]);
        var rcon = new RCON(addresses[0], _port, _password);
        await rcon.ConnectAsync();

        // Verify the connection is alive with a lightweight command
        var verify = await rcon.SendCommandAsync("seed");
        _logger.LogInformation("RCON connected and verified at {Host}:{Port} — seed: {Seed}", _host, _port, verify);

        _rcon = rcon;
        return _rcon;
    }

    private void SafeDisposeRcon()
    {
        try
        {
            _rcon?.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Suppressed exception during RCON dispose");
        }
        _rcon = null;
    }

    public async Task<string> SendCommandAsync(string command, CancellationToken ct = default)
    {
        await _semaphore.WaitAsync(ct);
        try
        {
            var rcon = await GetConnectionAsync();
            var response = await rcon.SendCommandAsync(command);
            await Task.Delay(_commandDelayMs, ct);
            return response;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "RCON command failed, resetting connection: {Command}", command);
            SafeDisposeRcon();
            throw;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public Task<string> SendFillAsync(int x1, int y1, int z1, int x2, int y2, int z2, string block, CancellationToken ct = default)
    {
        var command = $"fill {x1} {y1} {z1} {x2} {y2} {z2} {block}";
        _logger.LogDebug("RCON fill: {Command}", command);
        return SendCommandAsync(command, ct);
    }

    public Task<string> SendSetBlockAsync(int x, int y, int z, string block, CancellationToken ct = default)
    {
        var command = $"setblock {x} {y} {z} {block}";
        _logger.LogDebug("RCON setblock: {Command}", command);
        return SendCommandAsync(command, ct);
    }

    public async ValueTask DisposeAsync()
    {
        SafeDisposeRcon();
        _semaphore.Dispose();
    }
}
