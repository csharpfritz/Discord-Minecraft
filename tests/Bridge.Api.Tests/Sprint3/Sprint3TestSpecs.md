# Sprint 3 Test Specifications

> **Author:** Nightwing (QA)
> **Date:** 2026-02-12
> **Sprint:** 3 — Integration & Navigation
> **Status:** Specs written; tests pending implementation landing

---

## Overview

This document covers test specifications, expected behavior, and edge cases for all
Sprint 3 features. Tests are organized by GitHub issue number.

**Test infrastructure:** WebApplicationFactory + Testcontainers Redis + SQLite in-memory
(established in Sprint 2 via `BridgeApiFactory`).

**Key constraint:** Only publicly accessible Discord channels get villages. Private
channels are excluded from mapping.

**Deferred:** Account linking (`/link`, `/unlink`) is out of scope — no tests written.

---

## S3-01: Paper Bridge Plugin (#1)

### What to Test

The Paper Bridge Plugin exposes an HTTP API from the Java side. From the .NET test
perspective, we verify that Bridge.Api can call the plugin's endpoints and handle
responses/failures.

| ID | Test Case | Expected Behavior |
|----|-----------|-------------------|
| P-01 | Plugin HTTP API health check | GET `/api/health` returns 200 |
| P-02 | Plugin receives structure command | POST to plugin with building payload returns 200 |
| P-03 | Plugin reports player join event | Player join published to Redis, consumer processes it |
| P-04 | Plugin reports player leave event | Player leave published to Redis, consumer updates last location |
| P-05 | Plugin HTTP API unreachable | WorldGen Worker retries up to 3 times, then marks job Failed |
| P-06 | Plugin returns 500 | Job marked as failed after retries exhausted |
| P-07 | Plugin returns malformed JSON | Error logged, job not stuck in InProgress |

### Edge Cases

- Plugin starts after WorldGen Worker — queued jobs should be retried when plugin becomes available
- Plugin restarts mid-command — idempotent operations should be safe to re-execute
- Concurrent requests to plugin — semaphore in RconService should serialize
- Plugin port conflict — clear error message if configured port is already in use

### Test Approach

Stub the plugin HTTP client with NSubstitute or a custom `DelegatingHandler`.
Real container tests deferred to E2E suite.

---

## S3-03: Minecart Track Generation (#3)

### What to Test

Track generation creates rail paths between village station platforms with powered rails
every 8 blocks.

| ID | Test Case | Expected Behavior |
|----|-----------|-------------------|
| T-01 | Track between two villages | Powered rails placed every 8 blocks on y=65 trackbed |
| T-02 | Station structure at each village | Departure platform with destination signs and minecart dispensers |
| T-03 | Track path calculation | Direct line between village centers, correct coordinate interpolation |
| T-04 | Multiple villages — star topology | N villages produce N*(N-1)/2 unique tracks |
| T-05 | Track with 500-block spacing | Powered rail count = ~62 per track segment (500/8) |
| T-06 | Station sign content | Sign text matches destination village name |
| T-07 | Minecart dispenser activation | Button at platform triggers dispenser |

### Edge Cases

- **Two villages at same X or Z** — track runs straight along one axis
- **Villages at diagonal** — track path needs L-shaped or diagonal routing strategy
- **Single village** — no tracks generated (nothing to connect to)
- **16+ buildings in a village** — station must not overlap with building ring at radius=60
- **Village name with special characters** — signs handle UTF-8 or truncate gracefully
- **Track crosses existing structure** — coordinate validation should prevent overlap
- **Very large grid (100+ villages)** — star topology produces O(n^2) tracks

### Test Approach

Unit tests for track path calculation (pure math). Integration tests mock RconService.
Actual rail placement verified in E2E only.

---

## S3-04: Track Routing on Village Creation (#4)

### What to Test

When a new village is created, tracks to all existing villages are automatically generated.

| ID | Test Case | Expected Behavior |
|----|-----------|-------------------|
| R-01 | First village created | No track jobs enqueued (nothing to connect to) |
| R-02 | Second village created | One track job: village1 to village2 |
| R-03 | Third village created | Two track jobs: village3 to village1, village3 to village2 |
| R-04 | Track job queued after village job | Track generation after village creation completes |
| R-05 | Existing station signs updated | All existing villages get new departure platform |
| R-06 | Archived village excluded | Tracks NOT generated to archived villages |

### Edge Cases

- Village created while track generation in progress — job queue handles concurrent jobs
- Village creation fails — no track jobs should be enqueued
- Rapid village creation (bulk sync) — track jobs accumulate in queue; order matters
- WorldGen Worker crash during track generation — job stays InProgress; needs recovery

### Test Approach

Data-layer tests verify correct number of track jobs enqueued per village count.

---

## S3-05: Channel Deletion Handling (#5)

### What to Test

Channel deletion archives buildings (signs, barriers) without destruction. Category
deletion archives all buildings in the village.

| ID | Test Case | Expected Behavior |
|----|-----------|-------------------|
| D-01 | Single channel deleted | Channel.IsArchived = true, building NOT destroyed |
| D-02 | Channel group deleted | Group + ALL child channels archived |
| D-03 | Delete unknown channel | Handled gracefully, no exception |
| D-04 | Delete unknown channel group | Handled gracefully, no exception |
| D-05 | Delete already-archived channel | Idempotent, remains archived |
| D-06 | Delete already-archived group | Idempotent, remains archived |
| D-07 | Archive job enqueued on channel delete | UpdateBuilding job for signs and barriers |
| D-08 | Archive jobs enqueued on group delete | UpdateBuilding jobs for ALL channels |
| D-09 | GET /api/villages building count | Excludes archived buildings from count |
| D-10 | GET buildings shows archived status | isArchived=true in response |
| D-11 | Building index continuity | New channels skip archived index slots |
| D-12 | Re-sync after deletion | Sync doesn't resurrect archived channels |
| D-13 | Delete last channel in group | Group stays active (not auto-archived) |
| D-14 | Multiple rapid deletions | All channels archived, no race conditions |
| D-15 | Deletion with pending gen job | Archive takes precedence |

### Edge Cases

- Channel deleted before building generated (null BuildingX/BuildingZ)
- Group deleted with 0 channels — no iteration errors
- Concurrent delete events — sequential processing, no DbContext leakage
- Channel re-created with same Discord ID after deletion — upsert behavior
- Category deleted then channel created for that category

### Test Approach

**Primary test class: `ChannelDeletionTests.cs`** — 14 concrete xUnit integration tests.

---

## S3-06: Discord Slash Commands (#6)

### What to Test

| ID | Test Case | Expected Behavior |
|----|-----------|-------------------|
| C-01 | /status village count | Count of non-archived villages |
| C-02 | /status building count | Count of non-archived buildings |
| C-03 | /status player count | Count of linked players (0 if none) |
| C-04 | /navigate valid channel | Village name, building name, coordinates |
| C-05 | /navigate archived channel | Indicates channel is archived |
| C-06 | /navigate unknown channel | Indicates channel not found |
| C-07 | /navigate no argument | Discord validation error |
| C-08 | /status in DM | Guild-specific stats or error |

### Edge Cases

- Guild with 0 villages — /status returns zeros
- Coordinates not yet assigned — "building is being constructed"

---

## S3-07: E2E Smoke Tests (#7)

### Integration Test Scenarios

| ID | Scenario | Flow |
|----|----------|------|
| E-01 | Full channel sync | 3 groups, 6 channels, all DB records + jobs |
| E-02 | Channel create flow | Redis event, DB record, generation job |
| E-03 | Channel delete flow | Redis event, IsArchived=true |
| E-04 | Category delete flow | Group + all children archived |
| E-05 | Channel update flow | Name updated, UpdateBuilding job |
| E-06 | Out-of-order events | ChannelCreated before GroupCreated |
| E-07 | Duplicate events | Upsert, no duplicates |
| E-08 | Mixed create and delete | 2 active, 1 archived |

---

## Coverage Targets

| Area | Target |
|------|--------|
| Channel deletion (data layer) | 95%+ |
| API endpoints | 90%+ |
| Event consumer | 85%+ |
| Track generation (math) | 90%+ |
| Slash commands (handler logic) | 80%+ |
| E2E flows | 70%+ |

---

## Dependencies

- Track generation tests (#3, #4) depend on Batgirl's `ITrackGenerator` interface
- Slash command tests (#6) depend on Oracle's command handler implementation
- Plugin tests (#1) depend on Oracle's Paper plugin HTTP API contract
- Channel deletion tests (#5) can proceed NOW
