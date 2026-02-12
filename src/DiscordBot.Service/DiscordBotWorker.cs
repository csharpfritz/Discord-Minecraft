using Bridge.Data.Events;
using Discord;
using Discord.WebSocket;
using StackExchange.Redis;

namespace DiscordBot.Service;

public class DiscordBotWorker(
    DiscordSocketClient client,
    IConnectionMultiplexer redis,
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

        try
        {
            await client.CreateGlobalApplicationCommandAsync(pingCommand);
            logger.LogInformation("Registered /ping slash command");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to register slash commands");
        }
    }

    private async Task HandleSlashCommandAsync(SocketSlashCommand command)
    {
        if (command.Data.Name == "ping")
        {
            await command.RespondAsync("Pong! 🏓");
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
