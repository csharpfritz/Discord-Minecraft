package com.discordminecraft.bridge;

import com.google.gson.Gson;
import com.google.gson.JsonArray;
import com.google.gson.JsonObject;
import com.google.gson.JsonParser;
import net.kyori.adventure.text.Component;
import net.kyori.adventure.text.event.ClickEvent;
import net.kyori.adventure.text.format.NamedTextColor;
import org.bukkit.Bukkit;
import org.bukkit.Location;
import org.bukkit.command.Command;
import org.bukkit.command.CommandExecutor;
import org.bukkit.command.CommandSender;
import org.bukkit.command.TabCompleter;
import org.bukkit.entity.Player;
import org.jetbrains.annotations.NotNull;
import org.jetbrains.annotations.Nullable;

import java.net.URI;
import java.net.URLEncoder;
import java.net.http.HttpClient;
import java.net.http.HttpRequest;
import java.net.http.HttpResponse;
import java.nio.charset.StandardCharsets;
import java.util.Collections;
import java.util.List;
import java.util.Map;
import java.util.UUID;
import java.util.concurrent.ConcurrentHashMap;
import java.util.concurrent.CopyOnWriteArrayList;
import java.util.logging.Level;
import java.util.logging.Logger;

/**
 * /goto command — teleports a player to the building matching a Discord channel name.
 * Queries the Bridge API for building search and spawn coordinates.
 */
public final class GotoCommand implements CommandExecutor, TabCompleter {

    private final BridgePlugin plugin;
    private final Logger logger;
    private final String bridgeApiUrl;
    private final HttpClient httpClient;

    // Cooldown tracking: player UUID → last use timestamp
    private final Map<UUID, Long> cooldowns = new ConcurrentHashMap<>();
    private static final long COOLDOWN_MS = 5000;

    // Tab completion cache
    private final CopyOnWriteArrayList<String> channelNameCache = new CopyOnWriteArrayList<>();
    private volatile long lastCacheRefresh = 0;
    private static final long CACHE_REFRESH_MS = 60_000;

    public GotoCommand(BridgePlugin plugin, String bridgeApiUrl, Logger logger) {
        this.plugin = plugin;
        this.logger = logger;
        this.bridgeApiUrl = bridgeApiUrl.endsWith("/")
                ? bridgeApiUrl.substring(0, bridgeApiUrl.length() - 1)
                : bridgeApiUrl;
        this.httpClient = HttpClient.newHttpClient();
    }

    @Override
    public boolean onCommand(@NotNull CommandSender sender, @NotNull Command command,
                             @NotNull String label, @NotNull String[] args) {
        if (!(sender instanceof Player player)) {
            sender.sendMessage(Component.text("This command can only be used by players.", NamedTextColor.RED));
            return true;
        }

        if (args.length == 0) {
            player.sendMessage(Component.text("Usage: /goto <channel-name>", NamedTextColor.YELLOW));
            return true;
        }

        // Cooldown check
        long now = System.currentTimeMillis();
        Long lastUse = cooldowns.get(player.getUniqueId());
        if (lastUse != null && (now - lastUse) < COOLDOWN_MS) {
            long remaining = (COOLDOWN_MS - (now - lastUse)) / 1000 + 1;
            player.sendMessage(Component.text("Please wait " + remaining + "s before using /goto again.",
                    NamedTextColor.RED));
            return true;
        }
        cooldowns.put(player.getUniqueId(), now);

        String query = String.join(" ", args);

        // Run HTTP calls async to avoid blocking the main thread
        Bukkit.getScheduler().runTaskAsynchronously(plugin, () -> {
            try {
                String encoded = URLEncoder.encode(query, StandardCharsets.UTF_8);
                HttpRequest searchReq = HttpRequest.newBuilder()
                        .uri(URI.create(bridgeApiUrl + "/api/buildings/search?q=" + encoded))
                        .GET()
                        .build();

                HttpResponse<String> searchResp = httpClient.send(searchReq,
                        HttpResponse.BodyHandlers.ofString());

                if (searchResp.statusCode() != 200) {
                    sendOnMainThread(player, Component.text("Failed to search buildings (HTTP "
                            + searchResp.statusCode() + ").", NamedTextColor.RED));
                    return;
                }

                JsonArray matches = JsonParser.parseString(searchResp.body()).getAsJsonArray();

                if (matches.isEmpty()) {
                    sendOnMainThread(player, Component.text("No buildings found matching \""
                            + query + "\".", NamedTextColor.RED));
                    return;
                }

                if (matches.size() == 1) {
                    teleportToBuilding(player, matches.get(0).getAsJsonObject());
                } else {
                    // Multiple matches — show list
                    sendOnMainThread(player, Component.text("Multiple buildings found:",
                            NamedTextColor.YELLOW));
                    for (int i = 0; i < matches.size(); i++) {
                        JsonObject m = matches.get(i).getAsJsonObject();
                        String name = m.get("name").getAsString();
                        String village = m.get("villageName").getAsString();
                        Component entry = Component.text("  " + (i + 1) + ". ", NamedTextColor.GRAY)
                                .append(Component.text(name, NamedTextColor.GREEN)
                                        .clickEvent(ClickEvent.runCommand("/goto " + name)))
                                .append(Component.text(" (" + village + ")", NamedTextColor.DARK_GRAY));
                        sendOnMainThread(player, entry);
                    }
                }
            } catch (Exception e) {
                logger.log(Level.WARNING, "Error executing /goto command", e);
                sendOnMainThread(player, Component.text("An error occurred while searching.",
                        NamedTextColor.RED));
            }
        });

        return true;
    }

    private void teleportToBuilding(Player player, JsonObject match) {
        try {
            int buildingId = match.get("id").getAsInt();
            String channelName = match.get("name").getAsString();

            HttpRequest spawnReq = HttpRequest.newBuilder()
                    .uri(URI.create(bridgeApiUrl + "/api/buildings/" + buildingId + "/spawn"))
                    .GET()
                    .build();

            HttpResponse<String> spawnResp = httpClient.send(spawnReq,
                    HttpResponse.BodyHandlers.ofString());

            if (spawnResp.statusCode() != 200) {
                sendOnMainThread(player, Component.text("Failed to get teleport location (HTTP "
                        + spawnResp.statusCode() + ").", NamedTextColor.RED));
                return;
            }

            JsonObject spawn = JsonParser.parseString(spawnResp.body()).getAsJsonObject();
            int x = spawn.get("x").getAsInt();
            int y = spawn.get("y").getAsInt();
            int z = spawn.get("z").getAsInt();
            String villageName = spawn.get("villageName").getAsString();

            // Teleport must happen on main thread
            Bukkit.getScheduler().runTask(plugin, () -> {
                Location loc = new Location(player.getWorld(), x + 0.5, y, z + 0.5);
                player.teleport(loc);
                player.sendMessage(Component.text("Teleported to #" + channelName
                        + " in " + villageName + "!", NamedTextColor.GREEN));
            });
        } catch (Exception e) {
            logger.log(Level.WARNING, "Error teleporting player", e);
            sendOnMainThread(player, Component.text("Failed to teleport.", NamedTextColor.RED));
        }
    }

    private void sendOnMainThread(Player player, Component message) {
        Bukkit.getScheduler().runTask(plugin, () -> player.sendMessage(message));
    }

    @Override
    public @Nullable List<String> onTabComplete(@NotNull CommandSender sender, @NotNull Command command,
                                                @NotNull String alias, @NotNull String[] args) {
        if (args.length != 1) {
            return Collections.emptyList();
        }

        refreshCacheIfNeeded();

        String prefix = args[0].toLowerCase();
        return channelNameCache.stream()
                .filter(name -> name.toLowerCase().startsWith(prefix))
                .limit(20)
                .toList();
    }

    private void refreshCacheIfNeeded() {
        long now = System.currentTimeMillis();
        if (now - lastCacheRefresh < CACHE_REFRESH_MS) {
            return;
        }
        lastCacheRefresh = now;

        Bukkit.getScheduler().runTaskAsynchronously(plugin, () -> {
            try {
                // Fetch all channel names with a broad search
                HttpRequest req = HttpRequest.newBuilder()
                        .uri(URI.create(bridgeApiUrl + "/api/buildings/search?q="))
                        .GET()
                        .build();

                HttpResponse<String> resp = httpClient.send(req,
                        HttpResponse.BodyHandlers.ofString());

                if (resp.statusCode() == 200) {
                    JsonArray results = JsonParser.parseString(resp.body()).getAsJsonArray();
                    channelNameCache.clear();
                    for (int i = 0; i < results.size(); i++) {
                        channelNameCache.add(results.get(i).getAsJsonObject()
                                .get("name").getAsString());
                    }
                }
            } catch (Exception e) {
                logger.log(Level.FINE, "Failed to refresh channel name cache", e);
            }
        });
    }
}
