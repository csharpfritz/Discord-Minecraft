# Discord Bridge Plugin

Paper MC plugin providing the Minecraft-side integration for the Discord-Minecraft bridge.

## Features

- **HTTP API** on configurable port for receiving commands from .NET services
- **Redis pub/sub** for player join/leave event reporting  
- **Health endpoint** for service discovery and monitoring

## Building

Requires Java 21 and Gradle 8.12+.

```bash
# Generate Gradle wrapper (first time only)
gradle wrapper

# Build the plugin JAR
./gradlew build
```

The JAR is output to `build/libs/` and automatically copied to the Aspire-mounted `plugins/` directory.

## Configuration

Edit `plugins/DiscordBridge/config.yml` after first run:

```yaml
http-port: 8080
redis:
  host: localhost
  port: 6379
```

## HTTP API Endpoints

| Method | Path | Description |
|--------|------|-------------|
| GET | `/health` | Server health check |
| POST | `/api/command` | Execute a server command |
| GET | `/api/players` | List online players |

## Redis Events

Player events are published to `events:minecraft:player`:

```json
{
  "eventType": "PlayerJoined",
  "playerUuid": "...",
  "playerName": "Steve",
  "timestamp": "2026-02-12T00:00:00Z"
}
```
