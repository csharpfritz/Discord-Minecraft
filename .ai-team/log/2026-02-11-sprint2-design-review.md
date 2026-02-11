# Sprint 2 Design Review — Ceremony Summary

> **Facilitator:** Gordon (Lead / Architect)  
> **Date:** 2026-02-11  
> **Participants:** Oracle (Integration), Lucius (Backend), Batgirl (World Builder), Nightwing (QA)  
> **Sprint:** Sprint 2 — Core Features

---

## 1. Agenda Covered

1. Sprint 2 task review and dependency chain
2. Interface contracts between all four agents
3. Integration risks and edge cases
4. Action items per agent

---

## 2. Dependency Chain

```
S2-01 (Oracle: Discord events → Redis)
  ↓
S2-02 (Lucius: Bridge API endpoints)
  ↓
S2-03 (Lucius: Event consumer + job queue)  ← depends on S2-01 + S2-02
  
S2-04 (Batgirl: Village generation)
  ↓
S2-05 (Batgirl: Building generation)  ← depends on S2-04
  ↓
S2-06 (Lucius: WorldGen Worker job processor)  ← depends on S2-04 + S2-05
  ↓
S2-07 (Nightwing: Integration tests)  ← depends on S2-03 + S2-06
```

**Critical path:** S2-01 and S2-04 are unblocked. S2-02 is unblocked. Everything else chains off these three.

---

## 3. Contract Definitions

### 3.1 Redis Event Schema (Oracle → Lucius)

**Channel:** `events:discord:channel`  
**Format:** JSON, published via Redis pub/sub  
**Serialization:** `System.Text.Json` with `JsonSerializerDefaults.Web` (camelCase)

#### ChannelGroupCreated
```json
{
  "eventType": "ChannelGroupCreated",
  "timestamp": "2026-02-11T14:30:00Z",
  "guildId": "123456789012345678",
  "channelGroupId": "234567890123456789",
  "name": "general-discussion",
  "position": 2
}
```

#### ChannelGroupDeleted
```json
{
  "eventType": "ChannelGroupDeleted",
  "timestamp": "2026-02-11T14:30:00Z",
  "guildId": "123456789012345678",
  "channelGroupId": "234567890123456789",
  "name": "general-discussion"
}
```

#### ChannelCreated
```json
{
  "eventType": "ChannelCreated",
  "timestamp": "2026-02-11T14:30:00Z",
  "guildId": "123456789012345678",
  "channelId": "345678901234567890",
  "channelGroupId": "234567890123456789",
  "name": "welcome",
  "position": 0
}
```

#### ChannelDeleted
```json
{
  "eventType": "ChannelDeleted",
  "timestamp": "2026-02-11T14:30:00Z",
  "guildId": "123456789012345678",
  "channelId": "345678901234567890",
  "channelGroupId": "234567890123456789",
  "name": "welcome"
}
```

#### ChannelUpdated
```json
{
  "eventType": "ChannelUpdated",
  "timestamp": "2026-02-11T14:30:00Z",
  "guildId": "123456789012345678",
  "channelId": "345678901234567890",
  "channelGroupId": "234567890123456789",
  "oldName": "welcome",
  "newName": "welcome-new-members"
}
```

**Envelope rule:** Every event MUST include `eventType`, `timestamp`, and `guildId`. The consumer uses `eventType` to deserialize the rest.

**Redis channel rationale:** Single channel `events:discord:channel` rather than per-event-type channels. The volume is low (Discord channel ops are infrequent), and one subscriber simplifies Lucius's consumer. If volume ever becomes an issue, we split later.

---

### 3.2 Job Queue Format (Lucius → Batgirl via Redis)

**Queue key:** `queue:worldgen`  
**Mechanism:** Redis `LPUSH` to enqueue, `BRPOP` to dequeue (FIFO)  
**Format:** JSON

#### CreateVillage Job
```json
{
  "jobType": "CreateVillage",
  "jobId": 42,
  "channelGroupId": 7,
  "name": "general-discussion",
  "villageIndex": 3,
  "centerX": 1500,
  "centerZ": 0
}
```

#### CreateBuilding Job
```json
{
  "jobType": "CreateBuilding",
  "jobId": 43,
  "channelGroupId": 7,
  "channelId": 15,
  "villageCenterX": 1500,
  "villageCenterZ": 0,
  "buildingIndex": 2,
  "name": "welcome"
}
```

**Contract rules:**
- `jobId` matches `GenerationJob.Id` in PostgreSQL — the worker updates this row's status.
- `villageIndex` is the ordinal of the village (0-based), used by Batgirl to compute coordinates. Lucius computes this from the count of existing `ChannelGroup` rows.
- `centerX`/`centerZ` are pre-computed by Lucius using the formula: `villageIndex * 500` on alternating axes. Batgirl trusts these values.
- `buildingIndex` is the ordinal of the building within the village, computed by Lucius from the count of non-archived `Channel` rows in the group.

**Village coordinate formula (Lucius computes, Batgirl consumes):**
```
row = villageIndex / columns  (integer division)
col = villageIndex % columns
centerX = col * 500
centerZ = row * 500
columns = 10  (configurable constant, yields a 10-wide grid)
```

**Building placement formula (Batgirl owns):**
```
Buildings placed in a ring around village center.
Radius = 60 blocks from center.
Angle = buildingIndex * (360 / maxBuildingsPerVillage)
maxBuildingsPerVillage = 16 (initial capacity)
buildingX = centerX + (int)(radius * cos(angle_radians))
buildingZ = centerZ + (int)(radius * sin(angle_radians))
```

---

### 3.3 Bridge API Endpoints (Lucius owns)

#### `GET /api/villages`
Returns all channel groups with their village coordinates.
```json
[
  {
    "id": 7,
    "name": "general-discussion",
    "discordId": "234567890123456789",
    "centerX": 1500,
    "centerZ": 0,
    "buildingCount": 3,
    "createdAt": "2026-02-11T14:30:00Z"
  }
]
```

#### `GET /api/villages/{id}/buildings`
Returns all channels (buildings) in a village.
```json
[
  {
    "id": 15,
    "name": "welcome",
    "discordId": "345678901234567890",
    "buildingIndex": 2,
    "coordinateX": 1560,
    "coordinateZ": 0,
    "isArchived": false,
    "createdAt": "2026-02-11T14:30:00Z"
  }
]
```

#### `POST /api/mappings/sync`
Full sync of a Discord guild's channel structure. Body:
```json
{
  "guildId": "123456789012345678",
  "channelGroups": [
    {
      "discordId": "234567890123456789",
      "name": "general-discussion",
      "position": 2,
      "channels": [
        {
          "discordId": "345678901234567890",
          "name": "welcome",
          "position": 0
        }
      ]
    }
  ]
}
```
Returns: `200 OK` with sync result summary. Creates/updates records and enqueues generation jobs for new groups/channels.

#### `POST /api/players/link`
Initiates account linking (generates code, stores in Redis with 5-min TTL).  
_Sprint 3 scope — endpoint stub only in Sprint 2._

---

### 3.4 WorldGen Worker Interface (Lucius → Batgirl)

The WorldGen Worker (`Worker.cs`) is Lucius's responsibility for the outer loop (S2-06): dequeue, dispatch, status tracking, retries.

Batgirl implements the generation logic (S2-04, S2-05) as services that the worker calls.

**Interface contract:**

```csharp
// Batgirl implements these in WorldGen.Worker project
public interface IVillageGenerator
{
    Task GenerateAsync(VillageGenerationRequest request, CancellationToken ct);
}

public interface IBuildingGenerator
{
    Task GenerateAsync(BuildingGenerationRequest request, CancellationToken ct);
}

// Shared request models (place in WorldGen.Worker/Models/)
public record VillageGenerationRequest(
    int JobId,
    int ChannelGroupId,
    string Name,
    int VillageIndex,
    int CenterX,
    int CenterZ
);

public record BuildingGenerationRequest(
    int JobId,
    int ChannelGroupId,
    int ChannelId,
    int VillageCenterX,
    int VillageCenterZ,
    int BuildingIndex,
    string Name
);
```

**RCON interface (Batgirl uses):**  
Batgirl's generators take a dependency on `CoreRCON.RCON` (already proven in `tools/RconTest`). The RCON connection details come from configuration. Batgirl owns all RCON command composition — Lucius does not need to know what `/fill` commands are sent.

**Error contract:** Generators throw exceptions on failure. The worker catches them, records `ErrorMessage` on the `GenerationJob`, and retries up to 3 times with exponential backoff (1s, 4s, 16s).

---

### 3.5 GenerationJob Status Lifecycle

```
Pending → InProgress → Completed
                    ↘ Failed (after 3 retries)
```

- Lucius creates jobs as `Pending` in PostgreSQL, then pushes to Redis queue.
- Worker sets `InProgress` + `StartedAt` when dequeued.
- On success: `Completed` + `CompletedAt`.
- On failure after retries exhausted: `Failed` + `ErrorMessage`.
- `RetryCount` incremented on each attempt.

---

### 3.6 Shared Constants

These values are used across services. Define in `Bridge.Data` as a static class:

```csharp
namespace Bridge.Data;

public static class WorldConstants
{
    public const int VillageSpacing = 500;
    public const int GridColumns = 10;
    public const int BuildingFootprint = 21;
    public const int BuildingFloors = 4;
    public const int FloorHeight = 4;
    public const int VillagePlazaRadius = 60;
    public const int MaxBuildingsPerVillage = 16;
    public const int BaseY = 64;
}
```

---

## 4. Integration Risks

| # | Risk | Impact | Owner | Mitigation |
|---|------|--------|-------|------------|
| 1 | **Redis serialization mismatch** — Oracle publishes with one JSON shape, Lucius expects another | Events silently dropped or deserialization errors | Oracle + Lucius | Shared C# event model in `Bridge.Data`. Both reference the same types. Oracle serializes, Lucius deserializes. |
| 2 | **Job queue race condition** — Worker dequeues a job but crashes before updating PostgreSQL status | Job stuck as `Pending` in DB, already consumed from Redis | Lucius | Worker sets `InProgress` in DB *before* calling generator. On crash, a startup reconciliation query finds `InProgress` jobs older than 5 minutes and resets them to `Pending`. |
| 3 | **Village index collision** — Two `ChannelGroupCreated` events arrive near-simultaneously, both compute the same `villageIndex` | Two villages at same coordinates | Lucius | Use DB row count under a serializable transaction or a `UNIQUE` constraint on `(CenterX, CenterZ)` in `channel_groups`. |
| 4 | **RCON connection pool exhaustion** — Batgirl's generators hold RCON connections too long during large builds | WorldGen Worker blocks on RCON | Batgirl | Use a single RCON connection, serialize commands. Building generation is already serialized through the single worker. |
| 5 | **Building index gaps** — If channels are deleted and re-created, `buildingIndex` may have holes or collisions | Buildings overlap in the village ring | Lucius + Batgirl | `buildingIndex` is the next available slot. Lucius computes it by finding `MAX(BuildingIndex) + 1` for non-archived channels in the group. Archived buildings keep their index (the slot is "reserved"). |
| 6 | **Discord event ordering** — `ChannelCreated` arrives before `ChannelGroupCreated` for a new category with channels | Channel references a non-existent group | Oracle + Lucius | Lucius's consumer handles `ChannelCreated` for an unknown group by creating the group on-the-fly (upsert pattern), or by buffering with a short retry. Discord.NET fires category events first in practice, but we don't rely on it. |
| 7 | **Test isolation** — Nightwing's integration tests need Redis and PostgreSQL but shouldn't conflict with live services | Flaky tests, data pollution | Nightwing | Use Aspire's `DistributedApplicationTestingBuilder` with isolated containers per test run. Mock RCON via an in-memory fake implementing the generator interfaces. |

---

## 5. Shared Event Models — Location Decision

Place shared event DTOs in `Bridge.Data` under a new `Events/` folder:

```
Bridge.Data/
  Events/
    DiscordEvent.cs        ← base record with EventType, Timestamp, GuildId
    ChannelGroupCreated.cs
    ChannelGroupDeleted.cs
    ChannelCreated.cs
    ChannelDeleted.cs
    ChannelUpdated.cs
  Entities/                ← existing
  Jobs/
    WorldGenJob.cs         ← base record with JobType, JobId
    CreateVillageJob.cs
    CreateBuildingJob.cs
```

**Rationale:** `Bridge.Data` is already referenced by all three services. Placing event and job DTOs there avoids circular references and gives everyone a single source of truth.

---

## 6. Action Items

| # | Agent | Task | Sprint Item | Blocked By |
|---|-------|------|-------------|------------|
| 1 | Oracle | Implement Discord event handlers (`ChannelCreated`, `ChannelDestroyed`, `ChannelUpdated`, `CategoryCreated`) and publish to Redis channel `events:discord:channel` using shared event DTOs from `Bridge.Data/Events/` | S2-01 | — |
| 2 | Lucius | Create shared event DTOs in `Bridge.Data/Events/` and job DTOs in `Bridge.Data/Jobs/` per the schemas above | S2-02 (prerequisite) | — |
| 3 | Lucius | Implement Bridge API endpoints: `GET /api/villages`, `GET /api/villages/{id}/buildings`, `POST /api/mappings/sync`, `POST /api/players/link` (stub) | S2-02 | — |
| 4 | Lucius | Implement Redis event consumer in Bridge.Api as a `BackgroundService`. Subscribe to `events:discord:channel`, deserialize events, upsert DB records, enqueue jobs to `queue:worldgen` | S2-03 | S2-01, S2-02 |
| 5 | Lucius | Add `WorldConstants` static class to `Bridge.Data` | S2-02 (prerequisite) | — |
| 6 | Batgirl | Implement `IVillageGenerator` — plaza generation with stone bricks, signs, lighting via RCON `/fill` commands | S2-04 | — |
| 7 | Batgirl | Implement `IBuildingGenerator` — 21×21 footprint, 4-floor building with stairs, doors, signs via RCON | S2-05 | S2-04 |
| 8 | Lucius | Implement WorldGen Worker job processor — `BRPOP` from `queue:worldgen`, deserialize job, dispatch to `IVillageGenerator` or `IBuildingGenerator`, update `GenerationJob` status, retry logic | S2-06 | S2-04, S2-05 |
| 9 | Nightwing | Write integration tests using Aspire testing support — simulate event flow from Redis pub → Bridge API consumer → job queue → worker. Mock RCON via fake generator implementations | S2-07 | S2-03, S2-06 |
| 10 | Lucius | Add `UNIQUE` constraint on `(CenterX, CenterZ)` in `channel_groups` table (migration) | S2-02 | — |

---

## 7. Key Decisions Made

1. **Single Redis channel** `events:discord:channel` for all Discord events (not per-event-type)
2. **Shared DTOs in `Bridge.Data`** — event models under `Events/`, job models under `Jobs/`
3. **`WorldConstants` in `Bridge.Data`** — single source of truth for spacing, dimensions, grid layout
4. **Village coordinates computed by Lucius** (Bridge API), consumed by Batgirl (generators). Batgirl does NOT compute coordinates — she trusts the job payload.
5. **Building coordinates computed by Batgirl** using the ring-placement formula. The computed coordinates are persisted back to the `Channel` entity by the worker after successful generation.
6. **BRPOP for job dequeue** — blocking pop gives us natural work distribution without polling
7. **Generator interfaces** (`IVillageGenerator`, `IBuildingGenerator`) decouple Batgirl's algorithms from Lucius's worker loop
8. **Upsert pattern** for Discord events — handles out-of-order delivery gracefully
9. **`camelCase` JSON** via `JsonSerializerDefaults.Web` for all Redis payloads
10. **Unique constraint on village coordinates** prevents spatial collisions

---

## 8. Next Steps

- Lucius creates the shared DTOs and `WorldConstants` first (unblocks Oracle and Batgirl)
- Oracle and Batgirl can start in parallel on S2-01 and S2-04 respectively
- Nightwing prepares test scaffolding while waiting for S2-03 and S2-06
- Gordon reviews all PRs for structural changes before merge

---

*Ceremony closed. All agents proceed to implementation.*
