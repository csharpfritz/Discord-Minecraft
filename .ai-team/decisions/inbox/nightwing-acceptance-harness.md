### 2026-02-12: Acceptance test harness architecture
**By:** Nightwing
**What:** Created `tests/Acceptance.Tests/` project using `Aspire.Hosting.Testing` to launch the full stack. The harness uses:
1. `FullStackFixture` (IAsyncLifetime + IClassFixture pattern) to manage Aspire app lifecycle
2. `BlueMapClient` to query BlueMap's static JSON marker files (no REST API exists)
3. `DiscordEventPublisher` to simulate Discord events via Redis pub/sub
4. Job completion detection via polling `queue:worldgen` Redis list length
5. Serial test execution (single Minecraft container) via xUnit collection + runsettings

**Why:** Need end-to-end verification that Discord events → WorldGen jobs → Minecraft structures → BlueMap markers. Unit and integration tests can't catch issues at the Minecraft/BlueMap boundary. Aspire.Hosting.Testing is the idiomatic way to test Aspire apps. BlueMap has no REST API, so we query its static JSON files instead.
