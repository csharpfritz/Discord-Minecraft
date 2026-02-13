using System.Collections.Concurrent;
using System.Text.Json;
using Bridge.Data.Events;
using Discord;
using Discord.WebSocket;
using StackExchange.Redis;

namespace DiscordBot.Service;

/// <summary>
/// Subscribes to world activity events on Redis and posts Discord embeds
/// to a configurable #world-activity channel with rate limiting.
/// </summary>
public sealed class WorldActivityFeedService(
    IConnectionMultiplexer redis,
    DiscordSocketClient discordClient,
    IConfiguration configuration,
    ILogger<WorldActivityFeedService> logger) : BackgroundService
{
    private readonly ConcurrentQueue<WorldActivityEvent> _queue = new();
    private static readonly TimeSpan RateLimit = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var channelIdStr = configuration["Discord:ActivityChannelId"];
        if (string.IsNullOrWhiteSpace(channelIdStr) || !ulong.TryParse(channelIdStr, out var channelId))
        {
            logger.LogWarning("Discord:ActivityChannelId not configured — world activity feed disabled");
            return;
        }

        var subscriber = redis.GetSubscriber();
        var redisChannel = await subscriber.SubscribeAsync(RedisChannel.Literal(RedisChannels.WorldActivity));

        redisChannel.OnMessage(msg =>
        {
            try
            {
                var evt = WorldActivityEvent.FromJson(msg.Message.ToString());
                if (evt is not null)
                {
                    _queue.Enqueue(evt);
                    logger.LogDebug("Queued world activity event: {Type} — {Name}", evt.Type, evt.Name);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to deserialize world activity event");
            }
        });

        logger.LogInformation("World activity feed started, posting to channel {ChannelId}", channelId);

        // Drain queue with rate limiting
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (_queue.TryDequeue(out var evt))
                {
                    await PostEmbedAsync(channelId, evt);
                    await Task.Delay(RateLimit, stoppingToken);
                }
                else
                {
                    await Task.Delay(500, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error in world activity feed loop");
                await Task.Delay(5000, stoppingToken);
            }
        }

        await subscriber.UnsubscribeAsync(RedisChannel.Literal(RedisChannels.WorldActivity));
        logger.LogInformation("World activity feed stopped");
    }

    private async Task PostEmbedAsync(ulong channelId, WorldActivityEvent evt)
    {
        try
        {
            if (discordClient.GetChannel(channelId) is not IMessageChannel channel)
            {
                logger.LogWarning("Activity channel {ChannelId} not found or not a text channel", channelId);
                return;
            }

            var isArchived = evt.Type.Contains("archived", StringComparison.OrdinalIgnoreCase);
            var color = isArchived ? Color.Red : Color.Green;

            var title = $"{FormatType(evt.Type)}: {evt.Name}";
            var description = $"📍 Coordinates: X: {evt.X}, Z: {evt.Z}\n🕐 {evt.Timestamp:yyyy-MM-dd HH:mm:ss} UTC";

            var embed = new EmbedBuilder()
                .WithTitle(title)
                .WithDescription(description)
                .WithColor(color)
                .WithFooter("Discord-Minecraft World")
                .WithTimestamp(evt.Timestamp)
                .Build();

            await channel.SendMessageAsync(embed: embed);
            logger.LogDebug("Posted activity embed: {Title}", title);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to post activity embed for {Type}: {Name}", evt.Type, evt.Name);
        }
    }

    private static string FormatType(string type) => type switch
    {
        "village_built" => "🏘️ Village Built",
        "building_built" => "🏠 Building Built",
        "track_built" => "🚂 Track Built",
        "building_archived" => "🔒 Building Archived",
        _ => type
    };
}
