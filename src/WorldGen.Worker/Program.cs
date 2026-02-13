using Bridge.Data;
using WorldGen.Worker;
using WorldGen.Worker.Generators;
using WorldGen.Worker.Services;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();
builder.AddRedisClient("redis");
builder.AddNpgsqlDbContext<BridgeDbContext>("bridgedb");

builder.Services.AddSingleton<RconService>();
builder.Services.AddSingleton<ICrossroadsGenerator, CrossroadsGenerator>();
builder.Services.AddSingleton<IVillageGenerator, VillageGenerator>();
builder.Services.AddSingleton<IBuildingGenerator, BuildingGenerator>();
builder.Services.AddSingleton<IBuildingArchiver, BuildingArchiver>();
builder.Services.AddSingleton<ITrackGenerator, TrackGenerator>();
builder.Services.AddHttpClient<MarkerService>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Plugin:BaseUrl"] ?? "http://localhost:8180");
});
builder.Services.AddHostedService<CrossroadsInitializationService>();
builder.Services.AddHostedService<WorldGenJobProcessor>();

var host = builder.Build();
host.Run();
