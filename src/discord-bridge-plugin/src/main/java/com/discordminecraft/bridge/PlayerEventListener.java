package com.discordminecraft.bridge;

import com.google.gson.Gson;
import com.google.gson.GsonBuilder;
import org.bukkit.event.EventHandler;
import org.bukkit.event.Listener;
import org.bukkit.event.player.PlayerJoinEvent;
import org.bukkit.event.player.PlayerQuitEvent;

import java.time.Instant;
import java.util.HashMap;
import java.util.Map;
import java.util.UUID;

/**
 * Listens for player join/leave events and publishes them to Redis.
 * Event schema uses camelCase to match the .NET Bridge.Data conventions.
 */
public final class PlayerEventListener implements Listener {

    private final BridgePlugin plugin;
    private final RedisPublisher redisPublisher;
    private final Gson gson;

    public PlayerEventListener(BridgePlugin plugin, RedisPublisher redisPublisher) {
        this.plugin = plugin;
        this.redisPublisher = redisPublisher;
        this.gson = new GsonBuilder().create();
    }

    @EventHandler
    public void onPlayerJoin(PlayerJoinEvent event) {
        publishPlayerEvent("PlayerJoined", event.getPlayer().getUniqueId(), event.getPlayer().getName());
    }

    @EventHandler
    public void onPlayerQuit(PlayerQuitEvent event) {
        publishPlayerEvent("PlayerLeft", event.getPlayer().getUniqueId(), event.getPlayer().getName());
    }

    private void publishPlayerEvent(String eventType, UUID playerUuid, String playerName) {
        if (redisPublisher == null) {
            return;
        }

        Map<String, Object> payload = new HashMap<>();
        payload.put("eventType", eventType);
        payload.put("playerUuid", playerUuid.toString());
        payload.put("playerName", playerName);
        payload.put("timestamp", Instant.now().toString());

        String json = gson.toJson(payload);

        // Publish asynchronously to avoid blocking the main server thread
        plugin.getServer().getScheduler().runTaskAsynchronously(plugin, () ->
                redisPublisher.publish(RedisPublisher.CHANNEL_PLAYER_EVENT, json));
    }
}
