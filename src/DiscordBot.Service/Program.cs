using Discord;
using Discord.WebSocket;
using DiscordBot.Service;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();
builder.AddRedisClient("redis");

builder.Services.AddSingleton(new DiscordSocketConfig
{
    GatewayIntents = GatewayIntents.Guilds,
    LogLevel = LogSeverity.Info
});

builder.Services.AddSingleton<DiscordSocketClient>();
builder.Services.AddHttpClient("BridgeApi", client =>
{
    client.BaseAddress = new Uri("https+http://bridge-api");
});
builder.Services.AddHostedService<DiscordBotWorker>();

var host = builder.Build();
host.Run();
