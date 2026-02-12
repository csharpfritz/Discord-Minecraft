using System.Net;
using CoreRCON;

// Parse arguments: host port password
if (args.Length < 3)
{
    Console.WriteLine("Usage: RconTest <host> <port> <password>");
    Console.WriteLine("Example: RconTest localhost 25575 minecraft");
    return 1;
}

var host = args[0];
var port = ushort.Parse(args[1]);
var password = args[2];

Console.WriteLine($"Connecting to RCON at {host}:{port}...");

try
{
    var addresses = await Dns.GetHostAddressesAsync(host);
    if (addresses.Length == 0)
    {
        Console.Error.WriteLine($"Could not resolve host: {host}");
        return 1;
    }

    using var rcon = new RCON(addresses[0], port, password);
    await rcon.ConnectAsync();
    Console.WriteLine("Connected successfully.");

    var response = await rcon.SendCommandAsync("list");
    Console.WriteLine($"Server response: {response}");

    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"RCON connection failed: {ex.GetType().Name}: {ex.Message}");
    return 1;
}
