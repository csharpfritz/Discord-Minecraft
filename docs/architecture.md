# Discord-Minecraft Bridge — System Architecture

> **Author:** Gordon (Lead / Architect)  
> **Date:** 2026-02-11  
> **Status:** Draft — pending team review  
> **Stack:** .NET 10, Aspire 13.1, C#, Discord.NET, Paper MC, CoreRCON

---

## 1. System Overview

```
┌─────────────────────────────────────────────────────────────────────────┐
│                        .NET Aspire 13.1 AppHost                        │
│                     (Orchestration & Service Discovery)                 │
│                                                                         │
│  ┌──────────────┐   ┌──────────────┐   ┌──────────────┐               │
│  │  Discord Bot  │   │  Bridge API  │   │  World Gen   │               │
│  │   Service     │──▶│   Service    │──▶│   Worker     │               │
│  │ (Discord.NET) │   │  (ASP.NET)   │   │ (Background) │               │
│  └──────┬───────┘   └──────┬───────┘   └──────┬───────┘               │
│         │                  │                   │                        │
│         │           ┌──────┴───────┐           │                        │
│         │           │  PostgreSQL  │           │                        │
│         │           │  (State DB)  │           │                        │
│         │           └──────────────┘           │                        │
│         │                                      │                        │
│         │           ┌──────────────┐           │                        │
│         └──────────▶│    Redis     │◀──────────┘                        │
│                     │ (Event Bus)  │                                    │
│                     └──────────────┘                                    │
│                                                                         │
│                     ┌──────────────────────────┐                        │
│                     │  Paper MC Server         │                        │
│                     │  (Container)             │                        │
│                     │  ┌────────────────────┐  │                        │
│                     │  │ Bridge Plugin      │  │                        │
│                     │  │ (Java/Kotlin)      │  │                        │
│                     │  └────────────────────┘  │                        │
│                     │  RCON :25575              │                        │
│                     │  MC   :25565              │                        │
│                     └──────────────────────────┘                        │
│                                                                         │
└─────────────────────────────────────────────────────────────────────────┘
```

---

## 2. Service Decomposition

### 2.1 Discord Bot Service (`DiscordBot.Service`)
- **Runtime:** .NET 10 Worker Service  
- **Library:** Discord.NET  
- **Responsibilities:**
  - Connect to Discord gateway, authenticate with bot token
  - Subscribe to guild events: channel create/delete/rename, category changes, member join/leave
  - Expose slash commands: `/link` (link Minecraft account), `/status` (world status), `/navigate <channel>`
  - Publish domain events to Redis pub/sub when Discord state changes
- **Does NOT:** Directly talk to Minecraft. All Minecraft interaction goes through the Bridge API.

### 2.2 Bridge API Service (`Bridge.Api`)
- **Runtime:** ASP.NET Core Minimal API  
- **Responsibilities:**
  - Central orchestration point between Discord and Minecraft
  - REST endpoints for player identity management (Discord↔Minecraft account linking)
  - Consumes Discord events from Redis, translates to world commands
  - Queues world generation/modification jobs for the World Gen Worker
  - Serves channel→village mapping data
  - Health checks and status for the Aspire dashboard
- **Database:** PostgreSQL for persistent state

### 2.3 World Generation Worker (`WorldGen.Worker`)
- **Runtime:** .NET 10 Worker Service (BackgroundService)  
- **Responsibilities:**
  - Consumes world generation jobs from a Redis queue
  - Translates high-level commands ("create village for channel group X") into Minecraft commands
  - Sends commands to Paper MC via RCON (CoreRCON library) and/or the Bridge Plugin's REST API
  - Handles spatial planning: village placement coordinates, building dimensions, track routing
  - Idempotent operations — safe to retry on failure
- **Key constraint:** World modifications are serialized through this worker to prevent conflicts

### 2.4 Paper MC Server (Container)
- **Runtime:** Paper MC (Java) in a Docker container, managed by Aspire
- **Configuration:** Creative mode, Peaceful difficulty, flat world type (superflat)
- **Bridge Plugin (Java/Kotlin):**
  - Custom Paper plugin that listens for REST/WebSocket commands from the .NET services
  - Exposes a lightweight HTTP API for complex world operations that exceed RCON's capabilities
  - Handles: structure placement, rail/minecart systems, player teleportation, sign updates
  - Reports server events back to Redis (player join/leave, player location)

### 2.5 Infrastructure Resources (Aspire-managed)
- **PostgreSQL:** Channel mappings, player identities, village coordinates, world state
- **Redis:** Event bus (pub/sub), job queue for world generation, caching

---

## 3. Aspire 13.1 Orchestration

```csharp
// AppHost/Program.cs — conceptual
var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres")
    .AddDatabase("bridgedb");

var redis = builder.AddRedis("redis");

var minecraft = builder.AddContainer("minecraft", "itzg/minecraft-server")
    .WithEnvironment("TYPE", "PAPER")
    .WithEnvironment("EULA", "TRUE")
    .WithEnvironment("MODE", "creative")
    .WithEnvironment("DIFFICULTY", "peaceful")
    .WithEnvironment("LEVEL_TYPE", "FLAT")
    .WithEnvironment("ENABLE_RCON", "true")
    .WithEnvironment("RCON_PASSWORD", "{rcon-password}")
    .WithEndpoint(25565, 25565, name: "minecraft")
    .WithEndpoint(25575, 25575, name: "rcon")
    .WithBindMount("./minecraft-data", "/data");

var bridgeApi = builder.AddProject<Projects.Bridge_Api>("bridge-api")
    .WithReference(postgres)
    .WithReference(redis)
    .WaitFor(postgres)
    .WaitFor(redis);

var discordBot = builder.AddProject<Projects.DiscordBot_Service>("discord-bot")
    .WithReference(redis)
    .WithReference(bridgeApi)
    .WaitFor(redis)
    .WaitFor(bridgeApi);

var worldGen = builder.AddProject<Projects.WorldGen_Worker>("worldgen-worker")
    .WithReference(redis)
    .WithReference(postgres)
    .WaitFor(redis)
    .WaitFor(minecraft);

builder.Build().Run();
```

Aspire gives us:
- **Dashboard:** Real-time visibility into all services, logs, traces
- **Service discovery:** Automatic endpoint resolution between services
- **Health checks:** Built-in health monitoring
- **Configuration:** Centralized secrets and connection strings
- **Container management:** Paper MC server lifecycle

---

## 4. Data Flow

### 4.1 Discord Channel Event → Minecraft World Change

```
Discord Server                .NET Services                    Minecraft
─────────────                ──────────────                    ─────────
                                                               
Channel Created  ──▶  Discord Bot Service                      
                      │ Publishes event to Redis               
                      ▼                                        
                 Redis Pub/Sub                                 
                      │                                        
                      ▼                                        
                 Bridge API                                    
                      │ Looks up channel group                 
                      │ Determines village assignment           
                      │ Creates world gen job                  
                      ▼                                        
                 Redis Job Queue                               
                      │                                        
                      ▼                                        
                 WorldGen Worker                               
                      │ Calculates building placement          
                      │ Sends RCON commands / Plugin API       
                      ▼                                        
                                                    Paper MC Server
                                                    │ Places structure
                                                    │ Updates signs
                                                    │ Lays rail tracks
```

### 4.2 Player Links Discord → Minecraft Account

```
User types /link in Discord
  ▼
Discord Bot generates a unique link code
  ▼
User joins Minecraft, types /link <code> in-game
  ▼
Bridge Plugin sends code to Bridge API
  ▼
Bridge API validates code, creates player mapping in PostgreSQL
  ▼
Player is teleported to their channel group's village
```

### 4.3 Player Navigation via Minecarts

```
Player boards minecart at Village A station
  ▼
Bridge Plugin detects minecart ride start, reports to Bridge API
  ▼
Player arrives at Village B station
  ▼
Bridge Plugin reports arrival
  ▼
Bridge API optionally updates Discord status/presence
```

---

## 5. State Management

### 5.1 PostgreSQL Schema (Core Tables)

| Table | Purpose |
|-------|---------|
| `channel_groups` | Discord categories → village mappings (name, position, coordinates) |
| `channels` | Discord channels → building mappings (name, village_id, floor_count, coordinates) |
| `players` | Discord user ↔ Minecraft UUID linking, last known location |
| `world_state` | Village coordinates, building dimensions, track endpoints |
| `generation_jobs` | Job queue audit trail — what was generated, when, status |

### 5.2 Redis Usage

| Key Pattern | Purpose |
|-------------|---------|
| `events:discord:*` | Pub/sub channels for Discord events |
| `events:minecraft:*` | Pub/sub channels for Minecraft events |
| `queue:worldgen` | Job queue for world generation tasks |
| `cache:mapping:*` | Cached channel→village lookups |

---

## 6. World Generation Strategy

### 6.1 World Layout

- **World type:** Superflat (flat terrain, no obstacles)
- **Village spacing:** 500 blocks between village centers (generous — feels like distinct neighborhoods)
- **Village layout:** Circular/grid arrangement around a central plaza with a sign naming the channel group
- **Building dimensions:** 21×21 blocks footprint, 4+ floors, 4 blocks per floor (generous ceiling height)
  - Ground floor: open lobby with signs/information
  - Upper floors: open space for congregation (~10 players comfortably)
  - Roof: observation deck
- **Coordinate system:** Villages placed on a grid, origin at (0, 64, 0)
  - Village 0: (0, 64, 0)
  - Village 1: (500, 64, 0)
  - Village 2: (0, 64, 500)
  - etc.

### 6.2 Building Generation

Buildings represent individual Discord channels within a channel group's village.

- Generated using RCON `/fill` and `/setblock` commands for basic structure
- Bridge Plugin handles complex structure placement via its API (doors, signs, stairs, lighting)
- Buildings are placed around the village plaza in a ring pattern
- Each building has a sign at the entrance with the channel name
- New channels → new buildings added to the village ring
- Deleted channels → building marked as "archived" (not destroyed — players might be inside)

### 6.3 Minecart Track System

Tracks connect village plazas to each other:

- **Station:** Each village has a central station with named departure platforms
- **Tracks:** Powered rails every 8 blocks for sustained speed
- **Routing:** Direct tracks between all village pairs (star topology for simplicity in early versions)
- **Signs:** Platform signs indicate destination village (channel group)
- **Minecart dispensers:** Button-activated minecart spawning at each platform

### 6.4 Update Strategy

- **Real-time for high-priority events:** Channel create/delete triggers immediate world gen job
- **Batch for bulk changes:** Server reorganizations queue jobs that execute in sequence
- **Idempotent operations:** Every world gen command checks current state before modifying
- **No destructive deletes:** Removed channels archive buildings rather than destroying them

---

## 7. Key Technical Decisions

### Decision 1: Paper MC Server (self-hosted in container)
**Choice:** Run Paper MC in a Docker container orchestrated by Aspire  
**Why:** 
- Paper is the most widely-used performant fork with excellent plugin API
- Container gives us full control over configuration, plugins, and world data
- Aspire can manage the container lifecycle alongside our .NET services
- `itzg/minecraft-server` Docker image is battle-tested and configurable via env vars
- **Rejected:** Vanilla (no plugin API), Fabric (mod-focused, less plugin ecosystem), cloud-hosted (less control, more latency)

### Decision 2: Hybrid RCON + Plugin for World Control
**Choice:** CoreRCON for simple commands + custom Paper plugin with HTTP API for complex operations  
**Why:**
- RCON handles simple commands well (`/fill`, `/setblock`, `/tp`, `/gamerule`)
- Complex structure generation (multi-block buildings, rail systems) needs plugin-side logic
- Plugin exposes a lightweight HTTP API that the WorldGen Worker calls
- This avoids trying to squeeze complex spatial logic through RCON's command-line interface
- **Rejected:** Pure RCON (too limited), pure NBT manipulation (requires server restart), data packs only (no runtime changes)

### Decision 3: Event-Driven Architecture via Redis
**Choice:** Redis pub/sub for events, Redis lists for job queues  
**Why:**
- Aspire has first-class Redis support
- Decouples Discord events from world generation (different rates, different failure modes)
- WorldGen Worker can process jobs at its own pace without blocking Discord event handling
- Simple, proven pattern — no need for RabbitMQ/Kafka complexity at this scale
- **Rejected:** Direct service-to-service calls (tight coupling), message broker (over-engineered for this scale)

### Decision 4: PostgreSQL for State
**Choice:** PostgreSQL for all persistent state  
**Why:**
- Aspire has first-class PostgreSQL support
- Relational model fits our domain well (channels belong to groups, players link to identities)
- EF Core provides migrations and strong typing
- **Rejected:** SQLite (not suitable for multi-service access), NoSQL (relational model is a better fit)

### Decision 5: Superflat World
**Choice:** Superflat world type with custom generation  
**Why:**
- Clean canvas — no terrain to work around when placing villages
- Predictable Y coordinate for all structures (y=64)
- Faster world generation — no terrain noise to compute
- Better player experience — easy navigation, clear sightlines between buildings

### Decision 6: Account Linking via Codes
**Choice:** One-time code exchange between Discord `/link` command and in-game `/link` command  
**Why:**
- No OAuth complexity
- Player proves ownership of both accounts by being present in both
- Code expires after 5 minutes for security
- Simple to implement, easy to understand for players

---

## 8. Technology Choices Summary

| Component | Technology | Version | NuGet/Source |
|-----------|-----------|---------|--------------|
| Orchestration | .NET Aspire | 13.1 | `Aspire.Hosting` |
| Discord Bot | Discord.NET | latest | `Discord.Net` |
| RCON Client | CoreRCON | 5.4.x | `CoreRCON` |
| ORM | EF Core | 10.x | `Microsoft.EntityFrameworkCore` |
| Database | PostgreSQL | 17 | `Aspire.Hosting.PostgreSQL` |
| Cache/Events | Redis | 7.x | `Aspire.Hosting.Redis` |
| MC Server | Paper MC | 1.21.x | `itzg/minecraft-server` Docker image |
| MC Plugin | Custom | n/a | Java/Kotlin, built separately |
| Serialization | System.Text.Json | built-in | — |
| Logging | OpenTelemetry | via Aspire | Aspire defaults |

---

## 9. Non-Functional Requirements

- **Observability:** Aspire dashboard provides logs, traces, metrics out of the box
- **Resilience:** WorldGen Worker retries failed jobs with exponential backoff
- **Security:** Discord bot token and RCON password stored in .NET user secrets (dev) / environment variables (prod)
- **Performance:** World generation is async — Discord bot responds immediately, world updates happen in background
- **Scalability:** Single instance is fine for one Discord server. Architecture supports horizontal scaling of workers if needed later.

---

## 10. Out of Scope (For Now)

- Multiple Discord server support
- Bedrock Edition support  
- Web dashboard for administration
- Voice channel integration
- Real-time player position sync to Discord
- Automated backups (manual for now)
