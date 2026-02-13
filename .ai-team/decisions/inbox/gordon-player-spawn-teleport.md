# Player Spawn and Teleport Feature

**Author:** Gordon (Lead/Architect)  
**Date:** 2026-02-12  
**Status:** Design Complete — Ready for Implementation

---

## Feature Summary

Enable players to spawn at a central hub location when joining the server and teleport to Discord channel buildings using an in-game `/goto` command.

| Capability | Description |
|------------|-------------|
| **Default Spawn** | Players spawn at a central Hub Plaza at world origin (0, -59, 0) |
| **Teleport Command** | `/goto <channel-name>` teleports player to the building entrance for that channel |

---

## Design Decisions

### 1. Spawn Location: Central Hub (World Origin)

**Choice:** Hub Plaza at (0, -60, 0) — the first village in the grid (index 0).

**Rationale:**
- First village is always at origin per the grid formula (`col * VillageSpacing, row * VillageSpacing` where index 0 → col=0, row=0)
- Deterministic, always exists after first guild sync
- Players enter at the "front door" of the Discord world
- No additional world generation required — reuses existing village plaza
- Server spawn point set via RCON: `/setworldspawn 0 -59 0`

**Alternatives Considered:**
- *Separate hub structure*: Adds complexity, requires additional world gen work
- *Random village*: Confusing for players, no consistency
- *Individual beds*: Requires linking, overhead for new players

### 2. Teleport Command: `/goto <channel-name>`

**Choice:** Plugin-registered `/goto` command with fuzzy channel name matching.

**Command Syntax:**
```
/goto <channel-name>     # Teleport to building for channel "channel-name"
/goto general            # Teleport to #general's building
/goto "voice chat"       # Quoted names with spaces supported
```

**Behavior:**
1. Player types `/goto general`
2. Plugin HTTP endpoint receives teleport request
3. Queries Bridge API for channel coordinates
4. Teleports player to building entrance (south-facing, inside door)
5. Shows confirmation message: "Teleported to #general (Category Name)"

**Alternatives Considered:**
- *Discord-side `/goto`*: Players must be in Minecraft to teleport — command must be in-game
- *RCON-based*: No way to bind RCON to player input; plugin is correct
- *Command block teleporters*: Doesn't scale with dynamic channel list

---

## Component Changes

### Bridge.Api (ASP.NET Minimal API)

| Change | Details |
|--------|---------|
| **New Endpoint** | `GET /api/buildings/search?name={name}&limit=5` — Fuzzy search buildings by name |
| **New Endpoint** | `GET /api/buildings/{discordChannelId}/spawn` — Returns teleport coordinates for entrance |
| **Modify** | Add `EntranceX`, `EntranceY`, `EntranceZ` to `/api/navigate/{discordChannelId}` response |

**Building entrance coordinates:**
- Y: `WorldConstants.BaseY + 1` (-59, one block above floor)
- X/Z: Building center X, building center Z + `BuildingFootprint/2` (south entrance, inside door)

### Bridge.Data (Shared Library)

| Change | Details |
|--------|---------|
| **Constants** | Add `DefaultSpawnX = 0`, `DefaultSpawnY = -59`, `DefaultSpawnZ = 0` to `WorldConstants` |

### discord-bridge-plugin (Paper Plugin)

| Change | Details |
|--------|---------|
| **New Command** | `/goto <channel-name>` — Registered with Paper's command API |
| **New HTTP Endpoint** | `POST /api/teleport` — Teleports a player to coordinates (called from command handler) |
| **Startup Hook** | Set world spawn point on plugin enable via Bukkit API |
| **New HTTP Call** | Command handler calls Bridge API `/api/navigate/{discordChannelId}` to get coordinates |

**Command Registration:**
```java
// In BridgePlugin.java onEnable()
getCommand("goto").setExecutor(new GotoCommand(this, bridgeApiBaseUrl));

// plugin.yml
commands:
  goto:
    description: Teleport to a Discord channel's building
    usage: /goto <channel-name>
    permission: discordbridge.goto
```

### WorldGen.Worker

| Change | Details |
|--------|---------|
| **VillageGenerator** | Set world spawn at origin after first village generation |
| **RCON Command** | `/setworldspawn 0 -59 0` — Executed once, tracked in WorldState |

### DiscordBot.Service (Optional Enhancement)

| Change | Details |
|--------|---------|
| **New Slash Command** | `/tp <channel>` — Sends in-game teleport command for linked players (requires account linking) |

> **Note:** This is a future enhancement. Without account linking, we cannot map Discord user → Minecraft player. The in-game `/goto` command works without linking.

---

## API Contracts

### GET /api/buildings/search

**Purpose:** Fuzzy search buildings by name for `/goto` autocomplete.

**Request:**
```
GET /api/buildings/search?name=general&limit=5
```

**Response:**
```json
{
  "buildings": [
    {
      "discordChannelId": "123456789",
      "name": "general",
      "villageName": "Community",
      "entranceX": 10,
      "entranceY": -59,
      "entranceZ": 21,
      "isArchived": false
    }
  ]
}
```

**Notes:**
- Case-insensitive contains match
- Excludes archived buildings
- Returns top N matches sorted by name length (shortest first)

### GET /api/buildings/{discordChannelId}/spawn

**Purpose:** Get teleport destination for a specific building.

**Request:**
```
GET /api/buildings/123456789/spawn
```

**Response (200):**
```json
{
  "channelName": "general",
  "villageName": "Community",
  "x": 10,
  "y": -59,
  "z": 21
}
```

**Response (404):**
```json
{
  "message": "Channel not found or building not yet generated."
}
```

### POST /api/teleport (Plugin HTTP API)

**Purpose:** Teleport a player to coordinates (internal plugin endpoint).

**Request:**
```json
{
  "playerUuid": "550e8400-e29b-41d4-a716-446655440000",
  "x": 10,
  "y": -59,
  "z": 21,
  "message": "Teleported to #general"
}
```

**Response:**
```json
{
  "success": true,
  "playerName": "Steve"
}
```

---

## Data Flow Diagrams

### Default Spawn (First Village Creation)

```
┌──────────────────┐     ┌─────────────────┐     ┌────────────────┐
│ WorldGen.Worker  │────▶│    RCON         │────▶│   Minecraft    │
│                  │     │                 │     │    Server      │
│ After CreateVillage    │ setworldspawn   │     │                │
│ for index 0      │     │ 0 -59 0         │     │ Spawn set      │
└──────────────────┘     └─────────────────┘     └────────────────┘
```

### Player Teleport via /goto

```
┌─────────┐      ┌───────────────────┐      ┌─────────────┐      ┌─────────────┐
│ Player  │─────▶│  Bridge Plugin    │─────▶│ Bridge API  │◀─────│  PostgreSQL │
│         │      │                   │      │             │      │             │
│ /goto   │      │ GotoCommand       │ HTTP │ /buildings/ │      │ Channels    │
│ general │      │ .execute()        │ GET  │ search      │      │ table       │
└─────────┘      └───────────────────┘      └─────────────┘      └─────────────┘
     ▲                    │
     │                    ▼
     │           ┌───────────────────┐
     │           │  Bukkit API       │
     │           │                   │
     └───────────│ player.teleport() │
                 │ player.sendMessage│
                 └───────────────────┘
```

### Sequence: /goto command

```
Player          Plugin           Bridge API      Database
  │                │                 │               │
  │ /goto general  │                 │               │
  │───────────────▶│                 │               │
  │                │ GET /buildings/search?name=general
  │                │────────────────▶│               │
  │                │                 │  SELECT ...   │
  │                │                 │──────────────▶│
  │                │                 │◀──────────────│
  │                │ 200 { buildings: [...] }        │
  │                │◀────────────────│               │
  │                │                 │               │
  │                │ [Pick best match, teleport]     │
  │                │                 │               │
  │ TP + message   │                 │               │
  │◀───────────────│                 │               │
```

---

## Implementation Work Items

| ID | Title | Owner | Size | Dependencies |
|----|-------|-------|------|--------------|
| **TP-01** | Add `/api/buildings/search` endpoint | Lucius | S | — |
| **TP-02** | Add `/api/buildings/{id}/spawn` endpoint | Lucius | S | — |
| **TP-03** | Register `/goto` command in Paper plugin | Oracle | M | TP-01, TP-02 |
| **TP-04** | Add `POST /api/teleport` to plugin HTTP API | Oracle | S | TP-03 |
| **TP-05** | Set world spawn on first village creation | Batgirl | S | — |
| **TP-06** | Integration tests for building search API | Nightwing | S | TP-01, TP-02 |
| **TP-07** | E2E test: /goto command teleports player | Nightwing | M | TP-03, TP-05 |

**Size Legend:** S = ~2 hours, M = ~4 hours, L = ~8 hours

### Task Details

**TP-01: Add `/api/buildings/search` endpoint**
- Fuzzy name search (ILIKE `%{name}%` in PostgreSQL)
- Exclude archived channels
- Include entrance coordinates (calculated: building center X, center Z + 10)
- Return village name for context

**TP-02: Add `/api/buildings/{id}/spawn` endpoint**
- Accept Discord channel ID
- Return calculated spawn point (entrance, y=-59)
- 404 if channel not found or `BuildingX` is null (not yet generated)

**TP-03: Register `/goto` command in Paper plugin**
- Add to `plugin.yml`
- Create `GotoCommand` class implementing `CommandExecutor`
- HTTP client to Bridge API (configurable base URL in `config.yml`)
- Tab completion for channel names (async fetch from API)
- Teleport player on main thread via `Bukkit.getScheduler().runTask()`

**TP-04: Add `POST /api/teleport` to plugin HTTP API**
- Accept playerUuid, coordinates, optional message
- Lookup player by UUID
- Teleport on main thread
- Return success/failure

**TP-05: Set world spawn on first village creation**
- In `VillageGenerator.GenerateAsync`, check if `request.VillageIndex == 0`
- If so, execute `/setworldspawn 0 -59 0` via RCON
- Log: "World spawn set to origin (first village hub)"

**TP-06 & TP-07: Test coverage**
- Unit tests for search query logic
- Integration tests for API responses
- E2E test using Testcontainers (plugin mock or RCON verification)

---

## Configuration

### Paper Plugin (config.yml)

```yaml
# Existing
http-port: 8180
redis:
  host: localhost
  port: 6379

# New
bridge-api:
  base-url: http://bridge-api:8080
```

### Aspire (AppHost.cs)

No changes required — Bridge API is already discoverable by the plugin via Docker networking.

---

## Security Considerations

1. **Permission Node:** `/goto` command requires `discordbridge.goto` permission. Default to all players (ops can restrict if needed).
2. **Rate Limiting:** Consider adding cooldown (5 seconds) to prevent teleport spam.
3. **Archived Buildings:** Do not allow teleporting to archived buildings — return error message.

---

## Future Enhancements

1. **Discord `/tp` command:** Once account linking is implemented, allow Discord users to teleport their linked Minecraft account via Discord slash command.
2. **Teleport History:** Track recent teleports per player for a `/back` command.
3. **Favorite Locations:** Allow players to bookmark buildings.
4. **Teleport Confirmation:** For cross-village teleports, show distance before teleporting.

---

## Summary

This design adds player spawn and teleport functionality with minimal changes to existing infrastructure:

- **Spawn:** Reuses first village at origin (0, -59, 0) — one RCON command in VillageGenerator
- **Teleport:** New `/goto` command in Paper plugin, backed by two new Bridge API endpoints
- **No new services:** Plugin calls Bridge API directly; no additional Redis channels or job queues
- **Incremental:** Account linking not required; feature works standalone

**Total estimated effort:** ~20 hours across 4 squad members
