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
            await SyncGuildsAsync();
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
            .AddOption("village-name", ApplicationCommandOptionType.String,
                "Deep-link to a specific village by name on the map", isRequired: false)
            .Build();

        var crossroadsCommand = new SlashCommandBuilder()
            .WithName("crossroads")
            .WithDescription("Show info about the Crossroads hub — the central meeting point")
            .Build();

        var unlinkCommand = new SlashCommandBuilder()
            .WithName("unlink")
            .WithDescription("Remove your Discord-Minecraft account link")
            .Build();

        try
        {
            await client.CreateGlobalApplicationCommandAsync(pingCommand);
            await client.CreateGlobalApplicationCommandAsync(statusCommand);
            await client.CreateGlobalApplicationCommandAsync(navigateCommand);
            await client.CreateGlobalApplicationCommandAsync(mapCommand);
            await client.CreateGlobalApplicationCommandAsync(crossroadsCommand);
            await client.CreateGlobalApplicationCommandAsync(unlinkCommand);
            logger.LogInformation("Registered slash commands: /ping, /status, /navigate, /map, /crossroads, /unlink");
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
            case "crossroads":
                await HandleCrossroadsCommandAsync(command);
                break;
            case "unlink":
                await HandleUnlinkCommandAsync(command);
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
        var villageOption = command.Data.Options.FirstOrDefault(o => o.Name == "village-name");

        if (channelOption?.Value is IChannel targetChannel)
        {
            // Deep-link to a specific building marker using the channel ID
            var deepLinkUrl = $"{blueMapBaseUrl}#discord-buildings:{targetChannel.Id}";
            await command.RespondAsync(
                $"🗺️ **BlueMap** — Building for #{targetChannel.Name}:\n{deepLinkUrl}");
        }
        else if (villageOption?.Value is string villageName && !string.IsNullOrWhiteSpace(villageName))
        {
            await command.DeferAsync();
            try
            {
                var httpClient = httpClientFactory.CreateClient("BridgeApi");
                var response = await httpClient.GetAsync("/api/villages");

                if (!response.IsSuccessStatusCode)
                {
                    await command.FollowupAsync("⚠️ Could not retrieve village data.");
                    return;
                }

                var villages = await response.Content.ReadFromJsonAsync<List<VillageResponse>>();
                var match = villages?.FirstOrDefault(v =>
                    v.Name.Contains(villageName, StringComparison.OrdinalIgnoreCase));

                if (match is null)
                {
                    await command.FollowupAsync($"📭 No village found matching \"{villageName}\".");
                    return;
                }

                var deepLinkUrl = $"{blueMapBaseUrl}/#world:{match.CenterX}:{match.CenterZ}:0:100:0:0:0:0:flat";
                await command.FollowupAsync(
                    $"🗺️ **BlueMap** — Village **{match.Name}**:\n{deepLinkUrl}");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to execute /map with village-name");
                await command.FollowupAsync("⚠️ An error occurred while looking up the village.");
            }
        }
        else
        {
            await command.RespondAsync(
                $"🗺️ **BlueMap** — Interactive world map:\n{blueMapBaseUrl}");
        }
    }

    private record VillageResponse(int Id, string Name, string DiscordId, int CenterX, int CenterZ, int BuildingCount);

    private async Task HandleCrossroadsCommandAsync(SocketSlashCommand command)
    {
        await command.DeferAsync();

        try
        {
            var httpClient = httpClientFactory.CreateClient("BridgeApi");
            var response = await httpClient.GetAsync("/api/crossroads");

            if (!response.IsSuccessStatusCode)
            {
                await command.FollowupAsync("⚠️ Could not retrieve Crossroads info. The Bridge API may be unavailable.");
                return;
            }

            var crossroads = await response.Content.ReadFromJsonAsync<CrossroadsResponse>();
            if (crossroads is null)
            {
                await command.FollowupAsync("⚠️ Received an unexpected response from the Bridge API.");
                return;
            }

            var statusText = crossroads.Ready ? "✅ Generated" : "🔄 Pending generation";
            var blueMapBaseUrl = configuration["BlueMap:BaseUrl"] ?? "http://localhost:8200";

            var embed = new EmbedBuilder()
                .WithTitle($"⭐ {crossroads.Name}")
                .WithDescription(crossroads.Description)
                .WithColor(Color.Gold)
                .AddField("📍 Coordinates", $"X: {crossroads.X}  Y: {crossroads.Y}  Z: {crossroads.Z}", inline: true)
                .AddField("Status", statusText, inline: true)
                .AddField("🚂 Getting There", "`/goto crossroads` or take a minecart from any village station", inline: false)
                .AddField("🗺️ BlueMap", $"[View on Map]({crossroads.BlueMapUrl})", inline: false)
                .WithFooter("Discord ↔ Minecraft Bridge")
                .WithCurrentTimestamp()
                .Build();

            await command.FollowupAsync(embed: embed);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to execute /crossroads command");
            await command.FollowupAsync("⚠️ An error occurred while fetching Crossroads info.");
        }
    }

    private record CrossroadsResponse(
        string Name,
        int X,
        int Z,
        int Y,
        bool Ready,
        string? GeneratedAt,
        string Description,
        string BlueMapUrl);

    private async Task HandleUnlinkCommandAsync(SocketSlashCommand command)
    {
        // Account linking was deferred — stub response per Sprint 3 decisions
        await command.RespondAsync(
            "🔗 Account linking is not yet available. This feature is coming in a future update!",
            ephemeral: true);
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

    private async Task SyncGuildsAsync()
    {
        try
        {
            var httpClient = httpClientFactory.CreateClient("BridgeApi");
            var totalCategories = 0;
            var totalChannels = 0;

            foreach (var guild in client.Guilds)
            {
                var channelGroups = new List<SyncChannelGroup>();
                var everyoneRole = guild.EveryoneRole;

                foreach (var category in guild.CategoryChannels)
                {
                    // Skip categories where @everyone has ViewChannel explicitly denied
                    var everyoneOverwrite = category.GetPermissionOverwrite(everyoneRole);
                    if (everyoneOverwrite.HasValue &&
                        everyoneOverwrite.Value.ViewChannel == PermValue.Deny)
                        continue;

                    var channels = new List<SyncChannel>();

                    foreach (var channel in category.Channels.OfType<SocketTextChannel>())
                    {
                        // Skip text channels where @everyone has ViewChannel explicitly denied
                        var channelOverwrite = channel.GetPermissionOverwrite(everyoneRole);
                        if (channelOverwrite.HasValue &&
                            channelOverwrite.Value.ViewChannel == PermValue.Deny)
                            continue;

                        channels.Add(new SyncChannel(channel.Id.ToString(), channel.Name, channel.Position, channel.Users.Count));
                    }

                    channelGroups.Add(new SyncChannelGroup(
                        category.Id.ToString(), category.Name, category.Position, channels));
                }

                var syncRequest = new SyncRequest(guild.Id.ToString(), channelGroups);
                var response = await httpClient.PostAsJsonAsync("/api/mappings/sync", syncRequest);

                if (response.IsSuccessStatusCode)
                {
                    totalCategories += channelGroups.Count;
                    totalChannels += channelGroups.Sum(g => g.Channels.Count);
                    logger.LogInformation(
                        "Synced guild {GuildName} ({GuildId}): {Categories} categories, {Channels} channels",
                        guild.Name, guild.Id, channelGroups.Count,
                        channelGroups.Sum(g => g.Channels.Count));
                }
                else
                {
                    logger.LogWarning(
                        "Failed to sync guild {GuildName} ({GuildId}): {StatusCode}",
                        guild.Name, guild.Id, response.StatusCode);
                }
            }

            logger.LogInformation(
                "Guild sync complete: {TotalCategories} categories, {TotalChannels} channels across {GuildCount} guilds",
                totalCategories, totalChannels, client.Guilds.Count);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Guild sync failed — bot will continue running without initial sync");
        }
    }

    // DTOs for /api/mappings/sync endpoint
    private record SyncRequest(string GuildId, List<SyncChannelGroup> ChannelGroups);
    private record SyncChannelGroup(string DiscordId, string Name, int Position, List<SyncChannel> Channels);
    private record SyncChannel(string DiscordId, string Name, int Position = 0, int MemberCount = 0);

    private async Task PublishEventAsync(DiscordChannelEvent evt)
    {
        var json = evt.ToJson();
        await _subscriber!.PublishAsync(RedisChannel.Literal(RedisChannels.DiscordChannel), json);
    }
}
