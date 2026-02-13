# Decision: Acceptance Test Expansion Strategy

**Author:** Nightwing (Tester / QA)
**Date:** 2026-02-12
**Status:** Implemented

## Context

After establishing the acceptance test harness with `FullStackFixture`, `BlueMapClient`, and `DiscordEventPublisher`, the test suite needed expansion to cover the full range of system behaviors.

## Decision

Organized acceptance tests into **6 distinct test classes** by category:

| Test Class | Coverage | Test Count |
|------------|----------|------------|
| `SmokeTests` | Basic health checks | 6 |
| `VillageCreationTests` | Village/building generation | 6 |
| `TrackRoutingTests` | Minecart tracks between villages | 5 |
| `ArchivalTests` | Channel/village deletion behavior | 9 |
| `EdgeCaseTests` | Out-of-order events, duplicates, unicode | 8 |
| `NegativeTests` | Invalid inputs, error resilience | 13 |
| `ConcurrencyTests` | Parallel operations | 7 |

**Total: 54 acceptance tests**

## Key Test Categories

### Track Routing
- First village creates no tracks (no destinations)
- Second village triggers track to first
- Archived villages excluded from new track connections
- Distant villages (multiple grid positions) connected correctly

### Edge Cases
- Channel created before category exists (auto-create group)
- Simultaneous channel creation → distinct positions
- Duplicate events are idempotent
- Unicode/long names handled gracefully
- BlueMap marker polling with timeout

### Negative Tests
- Malformed JSON events ignored (no crash)
- Missing EventType rejected gracefully
- Null/empty fields handled
- API returns 404 for non-existent resources
- Unknown event types ignored

### Concurrency
- Multiple villages created simultaneously
- Parallel channels in same village get distinct ring positions
- High-volume event bursts (10+ channels) processed without loss

## Rationale

- **Separate test classes by concern** → easier to run subsets, clearer failure diagnostics
- **xUnit traits** → `[Trait("Category", "Acceptance")]` + subcategory enables filtered test runs
- **Edge case coverage** → documents expected behavior for unusual scenarios
- **Negative tests** → proves system resilience (crucial for Discord's unpredictable event ordering)

## Consequences

- CI may need extended timeout for full acceptance suite (57 tests × 5min = potentially long)
- Recommend running acceptance tests in separate CI job or nightly build
- Tests are SLOW by design — they launch the full Aspire stack with Minecraft

## Related

- `tests/Acceptance.Tests/` — all test files
- Team history: `📌 Track routing tests verify...`
