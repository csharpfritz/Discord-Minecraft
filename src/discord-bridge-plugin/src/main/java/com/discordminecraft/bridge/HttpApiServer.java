package com.discordminecraft.bridge;

import com.google.gson.Gson;
import com.google.gson.GsonBuilder;
import com.google.gson.JsonObject;
import com.google.gson.JsonParser;
import com.sun.net.httpserver.HttpExchange;
import com.sun.net.httpserver.HttpServer;

import org.bukkit.Bukkit;

import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;
import java.net.InetSocketAddress;
import java.nio.charset.StandardCharsets;
import java.util.HashMap;
import java.util.Map;
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
    private final Logger logger;
    private final Gson gson;

    public HttpApiServer(int port, BridgePlugin plugin, Logger logger) throws IOException {
        this.plugin = plugin;
        this.logger = logger;
        this.gson = new GsonBuilder().create();

        server = HttpServer.create(new InetSocketAddress(port), 0);
        server.setExecutor(Executors.newFixedThreadPool(4));

        server.createContext("/health", this::handleHealth);
        server.createContext("/api/command", this::handleCommand);
        server.createContext("/api/players", this::handlePlayers);
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
