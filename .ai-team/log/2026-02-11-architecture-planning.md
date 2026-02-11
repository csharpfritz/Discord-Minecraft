# Session: 2026-02-11 — Architecture Planning

**Requested by:** Jeffrey T. Fritz

## Team Creation

Team was created with the following agents:

| Agent     | Role              |
|-----------|-------------------|
| Gordon    | Lead              |
| Oracle    | Integration       |
| Lucius    | Backend           |
| Batgirl   | World Builder     |
| Nightwing | QA                |
| Scribe    | Logger / Memory   |

## Architecture

Gordon designed the system architecture and planned 3 sprints.

**Service decomposition:**
- 3 .NET services: Discord Bot, Bridge API, WorldGen Worker
- Paper MC server (via `itzg/minecraft-server` Docker container)
- PostgreSQL for state storage
- Redis for pub/sub + job queue
- All orchestrated by .NET Aspire 13.1

## Sprint Plan

### Sprint 1 — Foundation
Scaffolding, containers, database, RCON proof-of-concept, Discord bot shell, CI pipeline.

### Sprint 2 — Core Features
Discord event handling, Bridge API, village/building generation, job processing.

### Sprint 3 — Integration
Paper plugin, account linking, minecart tracks, slash commands, end-to-end tests.

## Artifacts

- `docs/architecture.md` — full architecture documentation
- `docs/sprints.md` — detailed sprint plan

## Decisions Made

- System architecture: 3 .NET services + Paper MC + PostgreSQL + Redis, orchestrated by Aspire 13.1
- Paper MC chosen over Vanilla/Fabric/Forge for plugin API and Docker support
- Sprint plan: 3 sprints (Foundation → Core Features → Integration)
- Channel deletion archives buildings (does not destroy them)
- Account linking via one-time codes (not OAuth)
