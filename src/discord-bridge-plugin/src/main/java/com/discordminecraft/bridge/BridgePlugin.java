package com.discordminecraft.bridge;

import org.bukkit.plugin.java.JavaPlugin;

import java.util.logging.Level;

/**
 * Paper plugin that bridges Discord-Minecraft integration.
 * Exposes an HTTP API for .NET services and publishes player events to Redis.
 */
public final class BridgePlugin extends JavaPlugin {

    private HttpApiServer httpServer;
    private RedisPublisher redisPublisher;
    private BlueMapIntegration blueMapIntegration;

    @Override
    public void onEnable() {
        saveDefaultConfig();

        int httpPort = getConfig().getInt("http-port", 8080);
        String redisHost = getConfig().getString("redis.host", "localhost");
        int redisPort = getConfig().getInt("redis.port", 6379);

        // Initialize Redis publisher
        try {
            redisPublisher = new RedisPublisher(redisHost, redisPort, getLogger());
            getLogger().info("Connected to Redis at " + redisHost + ":" + redisPort);
        } catch (Exception e) {
            getLogger().log(Level.SEVERE, "Failed to connect to Redis", e);
        }

        // Initialize BlueMap integration
        blueMapIntegration = new BlueMapIntegration(getLogger());
        blueMapIntegration.enable();
        getLogger().info("BlueMap integration initialized");


        // Register player event listener
        getServer().getPluginManager().registerEvents(
                new PlayerEventListener(this, redisPublisher), this);

        // Start HTTP API server
        try {
            httpServer = new HttpApiServer(httpPort, this, blueMapIntegration, getLogger());
            httpServer.start();
            getLogger().info("HTTP API started on port " + httpPort);
        } catch (Exception e) {
            getLogger().log(Level.SEVERE, "Failed to start HTTP API on port " + httpPort, e);
        }

        getLogger().info("DiscordBridge plugin enabled successfully");
    }

    @Override
    public void onDisable() {
        if (httpServer != null) {
            httpServer.stop();
            getLogger().info("HTTP API stopped");
        }

        if (blueMapIntegration != null) {
            blueMapIntegration.disable();
            getLogger().info("BlueMap integration disabled");
        }


        if (redisPublisher != null) {
            redisPublisher.close();
            getLogger().info("Redis connection closed");
        }

        getLogger().info("DiscordBridge plugin disabled");
    }
    public BlueMapIntegration getBlueMapIntegration() {
        return blueMapIntegration;
    }
}
