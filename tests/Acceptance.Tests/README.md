# Acceptance Test Harness

> Full-stack acceptance tests for the Discord-Minecraft bridge.

## Overview

These tests launch the **entire Aspire stack** and verify end-to-end functionality:
- PostgreSQL + Redis infrastructure
- Bridge.Api service
- WorldGen.Worker (RCON commands)
- Minecraft container with Paper MC
- BlueMap web server and markers

## Architecture

```
┌─────────────────────────────────────────────────────────────────────┐
│                      Acceptance Test Harness                        │
│                                                                     │
│  ┌──────────────────┐    ┌──────────────────┐                      │
│  │ DiscordEvent     │───▶│ Redis Pub/Sub    │                      │
│  │ Publisher        │    │ events:discord:* │                      │
│  └──────────────────┘    └────────┬─────────┘                      │
│                                   │                                 │
│                                   ▼                                 │
│  ┌──────────────────────────────────────────────────────────┐      │
│  │              Full Aspire Stack                           │      │
│  │  ┌─────────┐  ┌───────────┐  ┌──────────────┐           │      │
│  │  │Bridge   │──│ WorldGen  │──│  Minecraft   │           │      │
│  │  │API      │  │ Worker    │  │  (Paper MC)  │           │      │
│  │  └─────────┘  └───────────┘  └──────┬───────┘           │      │
│  │                                     │ BlueMap           │      │
│  │                                     │ port 8200         │      │
│  └─────────────────────────────────────┼────────────────────┘      │
│                                        │                           │
│  ┌──────────────────┐                  │                           │
│  │ BlueMapClient    │◀─────────────────┘                           │
│  │ (HTTP queries)   │                                              │
│  └──────────────────┘                                              │
└─────────────────────────────────────────────────────────────────────┘
```

## Test Categories

### Smoke Tests (`SmokeTests.cs`)
Fast health checks that verify:
- Bridge.Api is running
- Redis is connected
- BlueMap web server is accessible
- WorldGen queue exists

### Village Creation Tests (`VillageCreationTests.cs`)
Full end-to-end tests that:
1. Publish Discord events via Redis
2. Wait for WorldGen jobs to complete
3. Query BlueMap markers to verify structures

### Archival Tests (`ArchivalTests.cs`)
Tests for channel/channel-group deletion:
- Building markers are archived (not removed)
- Village markers persist after deletion

## Running Tests

### Prerequisites
- Docker Desktop running (for Minecraft container)
- .NET 10 SDK
- Sufficient memory (~4GB for Minecraft)

### Run all acceptance tests
```bash
dotnet test tests/Acceptance.Tests --settings tests/Acceptance.Tests/acceptance.runsettings
```

### Run only smoke tests (faster)
```bash
dotnet test tests/Acceptance.Tests --filter "Subcategory=Smoke"
```

### Run with verbose output
```bash
dotnet test tests/Acceptance.Tests --logger "console;verbosity=detailed"
```

### Skip acceptance tests in regular CI
The project uses a custom runsettings file with extended timeouts. 
In CI, you can skip these with:
```bash
dotnet test --filter "Category!=Acceptance"
```

## Timeouts

| Component | Timeout |
|-----------|---------|
| Full stack startup | 5 minutes |
| BlueMap ready check | 3 minutes |
| WorldGen job completion | 5 minutes per test |
| Test session overall | 10 minutes |

Minecraft container startup is the bottleneck — first start downloads Paper and plugins.

## BlueMap Integration

BlueMap doesn't expose a REST API. Instead, it serves static JSON files:

| Path | Content |
|------|---------|
| `/` | Web app HTML |
| `/maps/{mapId}/markers.json` | Marker sets and markers |
| `/data/markers.json` | Global markers (fallback) |
| `/maps.json` | Available map IDs |

The `BlueMapClient` class queries these files to verify:
- Village markers exist in `discord-villages` marker set
- Building markers exist in `discord-buildings` marker set
- Marker positions match expected grid/ring formulas

## Troubleshooting

### Tests timeout waiting for BlueMap
BlueMap needs time to render after Minecraft starts. The test harness waits for the web server, not full render completion. If markers are missing, increase `Task.Delay` before assertions.

### Minecraft container fails to start
Check Docker Desktop memory limits. Paper MC needs ~2GB.

### WorldGen jobs never complete
Check `queue:worldgen` in Redis. Jobs might be failing (see GenerationJobs table).

### Markers not appearing in BlueMap
The Java BlueMapIntegration plugin must be loaded. Check Paper server logs for BlueMap errors.

## Extending Tests

### Add a new acceptance test
1. Create a test class with `[Collection("FullStack")]` and `[Trait("Category", "Acceptance")]`
2. Inject `FullStackFixture` via constructor
3. Use `DiscordEventPublisher` to simulate Discord events
4. Use `BlueMapClient` to verify results
5. Call `_fixture.WaitForJobsToCompleteAsync()` between event and assertion
