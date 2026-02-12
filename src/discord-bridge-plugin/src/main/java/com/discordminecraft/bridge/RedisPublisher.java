package com.discordminecraft.bridge;

import redis.clients.jedis.JedisPool;
import redis.clients.jedis.JedisPoolConfig;
import redis.clients.jedis.Jedis;

import java.util.logging.Level;
import java.util.logging.Logger;

/**
 * Publishes Minecraft server events to Redis pub/sub channels.
 * Channel names match the constants defined in Bridge.Data/Events/RedisChannels.cs.
 */
public final class RedisPublisher {

    /** Must match RedisChannels.cs on the .NET side */
    public static final String CHANNEL_PLAYER_EVENT = "events:minecraft:player";

    private final JedisPool jedisPool;
    private final Logger logger;

    public RedisPublisher(String host, int port, Logger logger) {
        this.logger = logger;
        JedisPoolConfig poolConfig = new JedisPoolConfig();
        poolConfig.setMaxTotal(4);
        poolConfig.setMaxIdle(2);
        this.jedisPool = new JedisPool(poolConfig, host, port);
    }

    /**
     * Publishes a JSON message to the specified Redis channel.
     */
    public void publish(String channel, String jsonMessage) {
        try (Jedis jedis = jedisPool.getResource()) {
            jedis.publish(channel, jsonMessage);
        } catch (Exception e) {
            logger.log(Level.WARNING, "Failed to publish to Redis channel: " + channel, e);
        }
    }

    public void close() {
        if (jedisPool != null && !jedisPool.isClosed()) {
            jedisPool.close();
        }
    }
}
