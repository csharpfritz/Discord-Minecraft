using Bridge.Data.Events;
using Discord;
using Discord.WebSocket;
using StackExchange.Redis;
using System.Net.Http.Json;

namespace DiscordBot.Service;

public class DiscordBotWorker(
    DiscordSocketClient client,
    IConnectionMultiplexer redis,
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    ILogger<DiscordBotWorker> logger) : BackgroundService
{
    private ISubscriber? _subscriber;
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var token = configuration["Discord:BotToken"]
            ?? throw new InvalidOperationException(
                "Discord bot token not configured. Set 'Discord:BotToken' via user secrets or environment variable 'Discord__BotToken'.");

        client.Log += msg =>
        {
            logger.Log(msg.Severity switch
            {
                LogSeverity.Critical => LogLevel.Critical,
                LogSeverity.Error => LogLevel.Error,
                LogSeverity.Warning => LogLevel.Warning,
                LogSeverity.Info => LogLevel.Information,
                LogSeverity.Debug => LogLevel.Debug,
                _ => LogLevel.Trace
            }, msg.Exception, "Discord: {Message}", msg.Message);
            return Task.CompletedTask;
        };

        client.Connected += () =>
        {
            logger.LogInformation("Connected to Discord gateway");
            return Task.CompletedTask;
        };

        client.Disconnected += ex =>
        {
            logger.LogWarning(ex, "Disconnected from Discord gateway");
            return Task.CompletedTask;
        };

        client.Ready += async () =>
        {
            logger.LogInformation("Discord bot is ready. Guilds: {GuildCount}", client.Guilds.Count);
            await RegisterSlashCommandsAsync();
        };

        client.SlashCommandExecuted += HandleSlashCommandAsync;

        client.ChannelCreated += HandleChannelCreatedAsync;
        client.ChannelDestroyed += HandleChannelDestroyedAsync;
        client.ChannelUpdated += HandleChannelUpdatedAsync;

        _subscriber = redis.GetSubscriber();

        await client.LoginAsync(TokenType.Bot, token);
        await client.StartAsync();

        // Keep alive until shutdown is requested
        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Graceful shutdown
        }

        logger.LogInformation("Shutting down Discord bot...");
        await client.StopAsync();
    }

    private async Task RegisterSlashCommandsAsync()
    {
        var pingCommand = new SlashCommandBuilder()
            .WithName("ping")
            .WithDescription("Check if the bot is alive")
            .Build();

        var statusCommand = new SlashCommandBuilder()
            .WithName("status")
            .WithDescription("Show world stats — village count and building count")
            .Build();

        var navigateCommand = new SlashCommandBuilder()
            .WithName("navigate")
            .WithDescription("Show which village and building a channel maps to, with coordinates")
            .AddOption("channel", ApplicationCommandOptionType.Channel,
                "The channel to look up", isRequired: true)
            .Build();

        var mapCommand = new SlashCommandBuilder()
            .WithName("map")
            .WithDescription("Get a link to the interactive BlueMap web map")
            .AddOption("channel", ApplicationCommandOptionType.Channel,
                "Deep-link to a specific channel's building on the map", isRequired: false)
            .Build();

        try
        {
            await client.CreateGlobalApplicationCommandAsync(pingCommand);
            await client.CreateGlobalApplicationCommandAsync(statusCommand);
            await client.CreateGlobalApplicationCommandAsync(navigateCommand);
            await client.CreateGlobalApplicationCommandAsync(mapCommand);
            logger.LogInformation("Registered slash commands: /ping, /status, /navigate, /map");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to register slash commands");
        }
    }

    private async Task HandleSlashCommandAsync(SocketSlashCommand command)
    {
        switch (command.Data.Name)
        {
            case "ping":
                await command.RespondAsync("Pong! 🏓");
                break;
            case "status":
                await HandleStatusCommandAsync(command);
                break;
            case "navigate":
                await HandleNavigateCommandAsync(command);
                break;
            case "map":
                await HandleMapCommandAsync(command);
                break;
        }
    }

    private async Task HandleStatusCommandAsync(SocketSlashCommand command)
    {
        await command.DeferAsync();

        try
        {
            var httpClient = httpClientFactory.CreateClient("BridgeApi");
            var response = await httpClient.GetAsync("/api/status");

            if (!response.IsSuccessStatusCode)
            {
                await command.FollowupAsync("⚠️ Could not retrieve world status. The Bridge API may be unavailable.");
                return;
            }

            var status = await response.Content.ReadFromJsonAsync<StatusResponse>();

            var embed = new EmbedBuilder()
                .WithTitle("🌍 World Status")
                .WithColor(Color.Green)
                .AddField("🏘️ Villages", status?.VillageCount.ToString() ?? "0", inline: true)
                .AddField("🏠 Buildings", status?.BuildingCount.ToString() ?? "0", inline: true)
                .WithFooter("Discord ↔ Minecraft Bridge")
                .WithCurrentTimestamp()
                .Build();

            await command.FollowupAsync(embed: embed);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to execute /status command");
            await command.FollowupAsync("⚠️ An error occurred while fetching world status.");
        }
    }

    private async Task HandleNavigateCommandAsync(SocketSlashCommand command)
    {
        await command.DeferAsync();

        try
        {
            var channelOption = command.Data.Options.FirstOrDefault(o => o.Name == "channel");
            if (channelOption?.Value is not IChannel targetChannel)
            {
                await command.FollowupAsync("⚠️ Please specify a valid channel.", ephemeral: true);
                return;
            }

            var httpClient = httpClientFactory.CreateClient("BridgeApi");
            var response = await httpClient.GetAsync($"/api/navigate/{targetChannel.Id}");

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                await command.FollowupAsync(
                    $"📭 <#{targetChannel.Id}> has no village mapping. " +
                    "Only public text channels within a category are mapped to Minecraft buildings.");
                return;
            }

            if (!response.IsSuccessStatusCode)
            {
                await command.FollowupAsync("⚠️ Could not retrieve navigation data. The Bridge API may be unavailable.");
                return;
            }

            var nav = await response.Content.ReadFromJsonAsync<NavigateResponse>();
            if (nav is null)
            {
                await command.FollowupAsync("⚠️ Received an unexpected response from the Bridge API.");
                return;
            }

            var statusLabel = nav.IsArchived ? "🔒 Archived" : "✅ Active";

            var embed = new EmbedBuilder()
                .WithTitle($"🧭 Navigation — #{nav.ChannelName}")
                .WithColor(nav.IsArchived ? Color.DarkGrey : Color.Blue)
                .AddField("🏘️ Village", nav.VillageName, inline: true)
                .AddField("🏠 Building", $"#{nav.BuildingIndex}", inline: true)
                .AddField("📍 Coordinates", $"X: {nav.CoordinateX}  Y: {nav.CoordinateY}  Z: {nav.CoordinateZ}", inline: false)
                .AddField("🏘️ Village Center", $"X: {nav.VillageCenterX}  Z: {nav.VillageCenterZ}", inline: false)
                .AddField("Status", statusLabel, inline: true)
                .WithFooter("Discord ↔ Minecraft Bridge")
                .WithCurrentTimestamp()
                .Build();

            await command.FollowupAsync(embed: embed);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to execute /navigate command");
            await command.FollowupAsync("⚠️ An error occurred while fetching navigation data.");
        }
    }

    private record StatusResponse(int VillageCount, int BuildingCount);

    private record NavigateResponse(
        string ChannelName,
        string VillageName,
        int BuildingIndex,
        int CoordinateX,
        int CoordinateY,
        int CoordinateZ,
        bool IsArchived,
        int VillageCenterX,
        int VillageCenterZ);

    private async Task HandleMapCommandAsync(SocketSlashCommand command)
    {
        var blueMapBaseUrl = configuration["BlueMap:BaseUrl"] ?? "http://localhost:8200";

        var channelOption = command.Data.Options.FirstOrDefault(o => o.Name == "channel");
        if (channelOption?.Value is IChannel targetChannel)
        {
            // Deep-link to a specific building marker using the channel ID
            var deepLinkUrl = $"{blueMapBaseUrl}#discord-buildings:{targetChannel.Id}";
            await command.RespondAsync(
                $"🗺️ **BlueMap** — Building for #{targetChannel.Name}:\n{deepLinkUrl}");
        }
        else
        {
            await command.RespondAsync(
                $"🗺️ **BlueMap** — Interactive world map:\n{blueMapBaseUrl}");
        }
    }

    private async Task HandleChannelCreatedAsync(SocketChannel channel)
    {
        if (channel is SocketGuildChannel guildChannel)
        {
            var isCategory = channel is ICategoryChannel;
            var evt = new DiscordChannelEvent
            {
                EventType = isCategory
                    ? DiscordChannelEventType.ChannelGroupCreated
                    : DiscordChannelEventType.ChannelCreated,
                Timestamp = DateTimeOffset.UtcNow,
                GuildId = guildChannel.Guild.Id.ToString(),
                ChannelId = isCategory ? null : guildChannel.Id.ToString(),
                ChannelGroupId = isCategory
                    ? guildChannel.Id.ToString()
                    : (guildChannel as SocketTextChannel)?.Category?.Id.ToString(),
                Name = isCategory ? null : guildChannel.Name,
                ChannelGroupName = isCategory ? guildChannel.Name : null,
                Position = guildChannel.Position
            };

            await PublishEventAsync(evt);
            logger.LogInformation(
                "Channel created: {EventType} {Name} in guild {GuildId}",
                evt.EventType, guildChannel.Name, evt.GuildId);
        }
    }

    private async Task HandleChannelDestroyedAsync(SocketChannel channel)
    {
        if (channel is SocketGuildChannel guildChannel)
        {
            var isCategory = channel is ICategoryChannel;
            var evt = new DiscordChannelEvent
            {
                EventType = isCategory
                    ? DiscordChannelEventType.ChannelGroupDeleted
                    : DiscordChannelEventType.ChannelDeleted,
                Timestamp = DateTimeOffset.UtcNow,
                GuildId = guildChannel.Guild.Id.ToString(),
                ChannelId = isCategory ? null : guildChannel.Id.ToString(),
                ChannelGroupId = isCategory
                    ? guildChannel.Id.ToString()
                    : (guildChannel as SocketTextChannel)?.Category?.Id.ToString(),
                Name = isCategory ? null : guildChannel.Name,
                ChannelGroupName = isCategory ? guildChannel.Name : null
            };

            await PublishEventAsync(evt);
            logger.LogInformation(
                "Channel destroyed: {EventType} {Name} in guild {GuildId}",
                evt.EventType, guildChannel.Name, evt.GuildId);
        }
    }

    private async Task HandleChannelUpdatedAsync(SocketChannel oldChannel, SocketChannel newChannel)
    {
        if (newChannel is SocketGuildChannel newGuildChannel &&
            oldChannel is SocketGuildChannel oldGuildChannel)
        {
            // Only publish if something meaningful changed
            if (oldGuildChannel.Name == newGuildChannel.Name &&
                oldGuildChannel.Position == newGuildChannel.Position)
                return;

            var isCategory = newChannel is ICategoryChannel;
            var evt = new DiscordChannelEvent
            {
                EventType = DiscordChannelEventType.ChannelUpdated,
                Timestamp = DateTimeOffset.UtcNow,
                GuildId = newGuildChannel.Guild.Id.ToString(),
                ChannelId = isCategory ? null : newGuildChannel.Id.ToString(),
                ChannelGroupId = isCategory
                    ? newGuildChannel.Id.ToString()
                    : (newGuildChannel as SocketTextChannel)?.Category?.Id.ToString(),
                OldName = oldGuildChannel.Name,
                NewName = newGuildChannel.Name,
                Position = newGuildChannel.Position
            };

            await PublishEventAsync(evt);
            logger.LogInformation(
                "Channel updated: {OldName} → {NewName} in guild {GuildId}",
                evt.OldName, evt.NewName, evt.GuildId);
        }
    }

    private async Task PublishEventAsync(DiscordChannelEvent evt)
    {
        var json = evt.ToJson();
        await _subscriber!.PublishAsync(RedisChannel.Literal(RedisChannels.DiscordChannel), json);
    }
}
