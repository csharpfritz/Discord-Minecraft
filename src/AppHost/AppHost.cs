var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres")
    .AddDatabase("bridgedb");

var redis = builder.AddRedis("redis");

var rconPassword = builder.AddParameter("rcon-password", secret: true);

var minecraft = builder.AddContainer("minecraft", "itzg/minecraft-server")
    .WithEnvironment("TYPE", "PAPER")
    .WithEnvironment("EULA", "TRUE")
    .WithEnvironment("MODE", "creative")
    .WithEnvironment("DIFFICULTY", "peaceful")
    .WithEnvironment("LEVEL_TYPE", "FLAT")
    .WithEnvironment("ENABLE_RCON", "true")
    .WithEnvironment("RCON_PASSWORD", rconPassword)
    .WithEndpoint(targetPort: 25665, port: 25665, name: "minecraft", scheme: "tcp")
    .WithEndpoint(targetPort: 25675, port: 25675, name: "rcon", scheme: "tcp")
    .WithBindMount("./minecraft-data", "/data");

var bridgeApi = builder.AddProject<Projects.Bridge_Api>("bridge-api")
    .WithReference(postgres)
    .WithReference(redis)
    .WaitFor(postgres)
    .WaitFor(redis);

var discordBotToken = builder.AddParameter("discord-bot-token", secret: true);

var discordBot = builder.AddProject<Projects.DiscordBot_Service>("discord-bot")
    .WithReference(redis)
    .WithReference(bridgeApi)
    .WithEnvironment("Discord__BotToken", discordBotToken)
    .WaitFor(redis)
    .WaitFor(bridgeApi);

var worldGen = builder.AddProject<Projects.WorldGen_Worker>("worldgen-worker")
    .WithReference(redis)
    .WithReference(postgres)
    .WithEnvironment("Rcon__Host", minecraft.GetEndpoint("rcon"))
    .WithEnvironment("Rcon__Port", "25675")
    .WithEnvironment("Rcon__Password", rconPassword)
    .WaitFor(redis)
    .WaitFor(minecraft);

builder.Build().Run();
