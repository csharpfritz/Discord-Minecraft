package com.discordminecraft.bridge;

import com.google.gson.Gson;
import com.google.gson.GsonBuilder;
import com.google.gson.JsonArray;
import com.google.gson.JsonObject;
import com.google.gson.JsonParser;
import com.sun.net.httpserver.HttpExchange;
import com.sun.net.httpserver.HttpServer;

import org.bukkit.Bukkit;
import org.bukkit.Location;
import org.bukkit.Material;
import org.bukkit.block.Block;
import org.bukkit.block.BlockFace;
import org.bukkit.block.Lectern;
import org.bukkit.inventory.ItemStack;
import org.bukkit.inventory.meta.BookMeta;

import net.kyori.adventure.text.Component;

import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;
import java.net.InetSocketAddress;
import java.nio.charset.StandardCharsets;
import java.util.ArrayList;
import java.util.HashMap;
import java.util.List;
import java.util.Map;
import java.util.concurrent.CompletableFuture;
import java.util.concurrent.Executors;
import java.util.logging.Level;
import java.util.logging.Logger;

/**
 * Lightweight HTTP server exposing an API for .NET services to send commands
 * to the Minecraft server. Runs on a configurable port.
 */
public final class HttpApiServer {

    private final HttpServer server;
    private final BridgePlugin plugin;
    private final BlueMapIntegration blueMap;
    private final Logger logger;
    private final Gson gson;

    public HttpApiServer(int port, BridgePlugin plugin, BlueMapIntegration blueMap, Logger logger) throws IOException {
        this.plugin = plugin;
        this.blueMap = blueMap;
        this.logger = logger;
        this.gson = new GsonBuilder().create();

        server = HttpServer.create(new InetSocketAddress(port), 0);
        server.setExecutor(Executors.newFixedThreadPool(4));

        server.createContext("/health", this::handleHealth);
        server.createContext("/api/command", this::handleCommand);
        server.createContext("/api/players", this::handlePlayers);
        server.createContext("/api/markers/village", this::handleVillageMarker);
        server.createContext("/api/markers/building", this::handleBuildingMarker);
        server.createContext("/api/markers/building/archive", this::handleArchiveBuildingMarker);
        server.createContext("/api/markers/village/archive", this::handleArchiveVillageMarker);
        server.createContext("/plugin/lectern", this::handleLectern);
    }

    public void start() {
        server.start();
    }

    public void stop() {
        server.stop(1);
    }

    /**
     * GET /health -- Returns 200 with server status. Used by .NET services for health checks.
     */
    private void handleHealth(HttpExchange exchange) throws IOException {
        if (!"GET".equalsIgnoreCase(exchange.getRequestMethod())) {
            sendResponse(exchange, 405, errorJson("Method not allowed"));
            return;
        }

        Map<String, Object> status = new HashMap<>();
        status.put("status", "healthy");
        status.put("plugin", "DiscordBridge");
        status.put("serverVersion", Bukkit.getVersion());
        status.put("onlinePlayers", Bukkit.getOnlinePlayers().size());

        sendResponse(exchange, 200, gson.toJson(status));
    }

    /**
     * POST /api/command -- Executes a server command dispatched from .NET services.
     * Body: { "command": "say Hello World" }
     * Runs on the main server thread to ensure thread safety with Bukkit API.
     */
    private void handleCommand(HttpExchange exchange) throws IOException {
        if (!"POST".equalsIgnoreCase(exchange.getRequestMethod())) {
            sendResponse(exchange, 405, errorJson("Method not allowed"));
            return;
        }

        String body = readBody(exchange);
        if (body.isEmpty()) {
            sendResponse(exchange, 400, errorJson("Request body is required"));
            return;
        }

        try {
            JsonObject json = JsonParser.parseString(body).getAsJsonObject();
            String command = json.get("command").getAsString();

            if (command == null || command.isBlank()) {
                sendResponse(exchange, 400, errorJson("'command' field is required"));
                return;
            }

            // Execute on main thread -- Bukkit API is not thread-safe
            Bukkit.getScheduler().runTask(plugin, () ->
                    Bukkit.dispatchCommand(Bukkit.getConsoleSender(), command));

            Map<String, Object> result = new HashMap<>();
            result.put("success", true);
            result.put("command", command);
            sendResponse(exchange, 200, gson.toJson(result));

        } catch (Exception e) {
            logger.log(Level.WARNING, "Failed to execute command", e);
            sendResponse(exchange, 400, errorJson("Invalid request: " + e.getMessage()));
        }
    }

    /**
     * GET /api/players -- Returns list of online players.
     */
    private void handlePlayers(HttpExchange exchange) throws IOException {
        if (!"GET".equalsIgnoreCase(exchange.getRequestMethod())) {
            sendResponse(exchange, 405, errorJson("Method not allowed"));
            return;
        }

        var players = Bukkit.getOnlinePlayers().stream()
                .map(p -> {
                    Map<String, Object> info = new HashMap<>();
                    info.put("uuid", p.getUniqueId().toString());
                    info.put("name", p.getName());
                    info.put("x", p.getLocation().getBlockX());
                    info.put("y", p.getLocation().getBlockY());
                    info.put("z", p.getLocation().getBlockZ());
                    return info;
                })
                .toList();

        Map<String, Object> result = new HashMap<>();
        result.put("players", players);
        result.put("count", players.size());
        sendResponse(exchange, 200, gson.toJson(result));
    }

    /**
     * POST /api/markers/village -- Create/update a village marker on BlueMap.
     * Body: { "id": "...", "label": "...", "x": 0, "z": 0 }
     */
    private void handleVillageMarker(HttpExchange exchange) throws IOException {
        if (!"POST".equalsIgnoreCase(exchange.getRequestMethod())) {
            sendResponse(exchange, 405, errorJson("Method not allowed"));
            return;
        }

        String body = readBody(exchange);
        try {
            JsonObject json = JsonParser.parseString(body).getAsJsonObject();
            String id = json.get("id").getAsString();
            String label = json.get("label").getAsString();
            int x = json.get("x").getAsInt();
            int z = json.get("z").getAsInt();

            blueMap.setVillageMarker(id, label, x, z);

            Map<String, Object> result = new HashMap<>();
            result.put("success", true);
            result.put("markerId", id);
            sendResponse(exchange, 200, gson.toJson(result));
        } catch (Exception e) {
            logger.log(Level.WARNING, "Failed to set village marker", e);
            sendResponse(exchange, 400, errorJson("Invalid request: " + e.getMessage()));
        }
    }

    /**
     * POST /api/markers/building -- Create/update a building marker on BlueMap.
     * Body: { "id": "...", "label": "...", "x": 0, "z": 0 }
     */
    private void handleBuildingMarker(HttpExchange exchange) throws IOException {
        if (!"POST".equalsIgnoreCase(exchange.getRequestMethod())) {
            sendResponse(exchange, 405, errorJson("Method not allowed"));
            return;
        }

        String body = readBody(exchange);
        try {
            JsonObject json = JsonParser.parseString(body).getAsJsonObject();
            String id = json.get("id").getAsString();
            String label = json.get("label").getAsString();
            int x = json.get("x").getAsInt();
            int z = json.get("z").getAsInt();

            blueMap.setBuildingMarker(id, label, x, z);

            Map<String, Object> result = new HashMap<>();
            result.put("success", true);
            result.put("markerId", id);
            sendResponse(exchange, 200, gson.toJson(result));
        } catch (Exception e) {
            logger.log(Level.WARNING, "Failed to set building marker", e);
            sendResponse(exchange, 400, errorJson("Invalid request: " + e.getMessage()));
        }
    }

    /**
     * POST /api/markers/building/archive -- Mark a building marker as archived.
     * Body: { "id": "..." }
     */
    private void handleArchiveBuildingMarker(HttpExchange exchange) throws IOException {
        if (!"POST".equalsIgnoreCase(exchange.getRequestMethod())) {
            sendResponse(exchange, 405, errorJson("Method not allowed"));
            return;
        }

        String body = readBody(exchange);
        try {
            JsonObject json = JsonParser.parseString(body).getAsJsonObject();
            String id = json.get("id").getAsString();

            blueMap.archiveBuildingMarker(id);

            Map<String, Object> result = new HashMap<>();
            result.put("success", true);
            result.put("markerId", id);
            sendResponse(exchange, 200, gson.toJson(result));
        } catch (Exception e) {
            logger.log(Level.WARNING, "Failed to archive building marker", e);
            sendResponse(exchange, 400, errorJson("Invalid request: " + e.getMessage()));
        }
    }

    /**
     * POST /api/markers/village/archive -- Mark a village marker as archived.
     * Body: { "id": "..." }
     */
    private void handleArchiveVillageMarker(HttpExchange exchange) throws IOException {
        if (!"POST".equalsIgnoreCase(exchange.getRequestMethod())) {
            sendResponse(exchange, 405, errorJson("Method not allowed"));
            return;
        }

        String body = readBody(exchange);
        try {
            JsonObject json = JsonParser.parseString(body).getAsJsonObject();
            String id = json.get("id").getAsString();

            blueMap.archiveVillageMarker(id);

            Map<String, Object> result = new HashMap<>();
            result.put("success", true);
            result.put("markerId", id);
            sendResponse(exchange, 200, gson.toJson(result));
        } catch (Exception e) {
            logger.log(Level.WARNING, "Failed to archive village marker", e);
            sendResponse(exchange, 400, errorJson("Invalid request: " + e.getMessage()));
        }
    }

    /**
     * POST /plugin/lectern -- Places a lectern with a written book at the specified coordinates.
     * Body: { "x": int, "y": int, "z": int, "title": "...", "author": "...", "pages": ["..."] }
     * Must run on the main server thread for Bukkit API thread safety.
     */
    private void handleLectern(HttpExchange exchange) throws IOException {
        if (!"POST".equalsIgnoreCase(exchange.getRequestMethod())) {
            sendResponse(exchange, 405, errorJson("Method not allowed"));
            return;
        }

        String body = readBody(exchange);
        if (body.isEmpty()) {
            sendResponse(exchange, 400, errorJson("Request body is required"));
            return;
        }

        try {
            JsonObject json = JsonParser.parseString(body).getAsJsonObject();
            int x = json.get("x").getAsInt();
            int y = json.get("y").getAsInt();
            int z = json.get("z").getAsInt();
            String title = json.get("title").getAsString();
            String author = json.get("author").getAsString();
            JsonArray pagesArray = json.getAsJsonArray("pages");

            List<String> pages = new ArrayList<>();
            for (int i = 0; i < pagesArray.size(); i++) {
                pages.add(pagesArray.get(i).getAsString());
            }

            // Execute on main thread for Bukkit API safety
            CompletableFuture<Boolean> future = new CompletableFuture<>();
            Bukkit.getScheduler().runTask(plugin, () -> {
                try {
                    var world = Bukkit.getWorlds().get(0);
                    Location loc = new Location(world, x, y, z);
                    Block block = loc.getBlock();

                    block.setType(Material.LECTERN);

                    Lectern lectern = (Lectern) block.getState();
                    ItemStack book = new ItemStack(Material.WRITTEN_BOOK);
                    BookMeta meta = (BookMeta) book.getItemMeta();
                    meta.setTitle(title.length() > 32 ? title.substring(0, 32) : title);
                    meta.setAuthor(author.length() > 16 ? author.substring(0, 16) : author);
                    List<Component> bookPages = new ArrayList<>();
                    for (String page : pages) {
                        bookPages.add(Component.text(page));
                    }
                    meta.pages(bookPages);
                    book.setItemMeta(meta);
                    lectern.getInventory().setItem(0, book);
                    lectern.update();

                    future.complete(true);
                } catch (Exception e) {
                    logger.log(Level.WARNING, "Failed to place lectern", e);
                    future.complete(false);
                }
            });

            boolean success = future.get(5, java.util.concurrent.TimeUnit.SECONDS);

            Map<String, Object> result = new HashMap<>();
            result.put("success", success);
            result.put("x", x);
            result.put("y", y);
            result.put("z", z);
            sendResponse(exchange, success ? 200 : 500, gson.toJson(result));

        } catch (Exception e) {
            logger.log(Level.WARNING, "Failed to handle lectern request", e);
            sendResponse(exchange, 400, errorJson("Invalid request: " + e.getMessage()));
        }
    }

    private void sendResponse(HttpExchange exchange, int statusCode, String body) throws IOException {
        byte[] bytes = body.getBytes(StandardCharsets.UTF_8);
        exchange.getResponseHeaders().set("Content-Type", "application/json");
        exchange.sendResponseHeaders(statusCode, bytes.length);
        try (OutputStream os = exchange.getResponseBody()) {
            os.write(bytes);
        }
    }

    private String readBody(HttpExchange exchange) throws IOException {
        try (InputStream is = exchange.getRequestBody()) {
            return new String(is.readAllBytes(), StandardCharsets.UTF_8);
        }
    }

    private String errorJson(String message) {
        Map<String, Object> error = new HashMap<>();
        error.put("error", message);
        return gson.toJson(error);
    }
}
