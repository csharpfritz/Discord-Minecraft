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
    private int _currentDelayMs;
    private const int MinDelayMs = 5;
    private const int MaxDelayMs = 100;
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
        _commandDelayMs = int.Parse(configuration["Rcon:CommandDelayMs"] ?? "10");
        _currentDelayMs = _commandDelayMs;
    }

    private async Task<RCON> GetConnectionAsync()
    {
        if (_rcon is not null)
            return _rcon;

        var addresses = await Dns.GetHostAddressesAsync(_host);
        // CoreRCON uses IPv4 sockets — filter out IPv6 to avoid NotSupportedException
        var ipv4 = addresses.FirstOrDefault(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            ?? IPAddress.Loopback;

        _logger.LogInformation("Connecting to RCON at {Host}:{Port} (resolved: {Address})", _host, _port, ipv4);
        var rcon = new RCON(ipv4, _port, _password);
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
            await Task.Delay(_currentDelayMs, ct);
            _currentDelayMs = Math.Max(MinDelayMs, _currentDelayMs - 1);
            return response;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "RCON command failed, resetting connection: {Command}", command);
            _currentDelayMs = Math.Min(MaxDelayMs, _currentDelayMs * 2);
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

    /// <summary>
    /// Sends multiple commands in a single semaphore acquisition with ONE delay at the end.
    /// Much faster than calling SendCommandAsync in a loop.
    /// </summary>
    public async Task<string[]> SendBatchAsync(IReadOnlyList<string> commands, CancellationToken ct = default)
    {
        if (commands.Count == 0) return Array.Empty<string>();

        await _semaphore.WaitAsync(ct);
        try
        {
            var rcon = await GetConnectionAsync();
            var responses = new string[commands.Count];
            for (int i = 0; i < commands.Count; i++)
            {
                responses[i] = await rcon.SendCommandAsync(commands[i]);
            }
            await Task.Delay(_currentDelayMs, ct); // ONE delay at end of batch
            _currentDelayMs = Math.Max(MinDelayMs, _currentDelayMs - 1);
            _logger.LogDebug("RCON batch completed: {Count} commands in batch, delay={Delay}ms", commands.Count, _currentDelayMs);
            return responses;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "RCON batch of {Count} commands failed, resetting connection", commands.Count);
            _currentDelayMs = Math.Min(MaxDelayMs, _currentDelayMs * 2);
            SafeDisposeRcon();
            throw;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <summary>
    /// Send multiple fill commands in a single batch.
    /// </summary>
    public Task<string[]> SendFillBatchAsync(IReadOnlyList<(int x1, int y1, int z1, int x2, int y2, int z2, string block)> fills, CancellationToken ct = default)
    {
        var commands = fills.Select(f => $"fill {f.x1} {f.y1} {f.z1} {f.x2} {f.y2} {f.z2} {f.block}").ToList();
        _logger.LogDebug("RCON fill batch: {Count} commands", commands.Count);
        return SendBatchAsync(commands, ct);
    }

    /// <summary>
    /// Send multiple setblock commands in a single batch.
    /// </summary>
    public Task<string[]> SendSetBlockBatchAsync(IReadOnlyList<(int x, int y, int z, string block)> blocks, CancellationToken ct = default)
    {
        var commands = blocks.Select(b => $"setblock {b.x} {b.y} {b.z} {b.block}").ToList();
        _logger.LogDebug("RCON setblock batch: {Count} commands", commands.Count);
        return SendBatchAsync(commands, ct);
    }

    public async ValueTask DisposeAsync()
    {
        SafeDisposeRcon();
        _semaphore.Dispose();
    }
}
