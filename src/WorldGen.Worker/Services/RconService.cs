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

        _rcon = new RCON(addresses[0], _port, _password);
        await _rcon.ConnectAsync();
        _logger.LogInformation("Connected to RCON at {Host}:{Port}", _host, _port);
        return _rcon;
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
            _rcon?.Dispose();
            _rcon = null;
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
        _rcon?.Dispose();
        _rcon = null;
        _semaphore.Dispose();
    }
}
