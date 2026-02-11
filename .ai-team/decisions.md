# Decisions

> Shared decision log. All agents read this. Scribe merges new entries from the inbox.

### 2026-02-11: System architecture for Discord-Minecraft bridge
**By:** Gordon
**What:** Established the service decomposition (3 .NET services + Paper MC container), communication patterns (Redis pub/sub + job queue), state storage (PostgreSQL), and world generation strategy (superflat, 500-block grid villages, RCON + Bridge Plugin hybrid). Full details in `docs/architecture.md`.
**Why:** Need clear service boundaries and technology choices before any implementation begins. Event-driven architecture decouples Discord's event rate from Minecraft's world generation rate. Hybrid RCON + plugin approach balances simplicity (RCON for basic commands) with capability (plugin API for complex structures). Superflat world eliminates terrain complexity and gives predictable coordinates.

### 2026-02-11: Paper MC as the Minecraft server platform
**By:** Gordon
**What:** Using Paper MC (not Vanilla, Fabric, or Forge) running in the `itzg/minecraft-server` Docker container, orchestrated by Aspire.
**Why:** Paper has the best plugin API for server-side world manipulation, excellent performance, and the Docker image is battle-tested with env-var configuration. Aspire's container support makes this a first-class citizen in our service graph.

### 2026-02-11: Sprint plan for first 3 sprints
**By:** Gordon
**What:** Defined 3 sprints (Foundation, Core Features, Integration & Navigation) with 7 work items each, assigned to team members by expertise. Full details in `docs/sprints.md`.
**Why:** Sprint 1 establishes the infrastructure so all subsequent work has a running environment. Sprint 2 delivers the core Discord→Minecraft pipeline. Sprint 3 adds player-facing features (account linking, navigation). This ordering minimizes blocked work items.

### 2026-02-11: Channel deletion archives buildings, does not destroy them
**By:** Gordon
**What:** When a Discord channel is deleted, its corresponding Minecraft building is marked archived (signs updated, entrance blocked with barriers) but NOT demolished.
**Why:** Destroying structures while players might be inside is dangerous. Archived buildings preserve world continuity and can be repurposed if channels are recreated. This is the safe default — we can add a `/demolish` admin command later if needed.

### 2026-02-11: Account linking via one-time codes, not OAuth
**By:** Gordon
**What:** Players link Discord↔Minecraft accounts by generating a 6-char code via Discord `/link`, then typing `/link <code>` in-game within 5 minutes.
**Why:** Avoids OAuth complexity. Player proves ownership of both accounts by being present in both systems simultaneously. Redis TTL handles expiry automatically. Simple for players to understand.
