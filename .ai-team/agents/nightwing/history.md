# Project Context

- **Owner:** Jeffrey T. Fritz (csharpfritz@users.noreply.github.com)
- **Project:** Discord-to-Minecraft bridge — maps Discord channels to Minecraft villages/buildings with minecart navigation between channel groups. Creative/peaceful mode, .NET 10/Aspire 13.1/C#.
- **Stack:** .NET 10, Aspire 13.1, C#, Discord.NET, Minecraft protocol
- **Created:** 2026-02-11

## Learnings

<!-- Append new learnings below. Each entry is something lasting about the project. -->

### Sprint 1–3 Test Infrastructure Summary (2026-02-11 → 2026-02-12)

**CI & Structure:** `.github/workflows/ci.yml` (push to main + PRs, .NET 10 SDK). Test projects under `tests/`, solution folder `/tests/` in `DiscordMinecraft.slnx` (XML `.slnx` format). Global `<Using Include="Xunit" />`.

**Bridge.Api.Tests:** WebApplicationFactory + Testcontainers Redis + SQLite in-memory (`BridgeApiFactory.cs`). Must remove ALL EF Core + Npgsql descriptors before re-registering SQLite. `Cache=Shared` requires keep-alive `SqliteConnection`. Nullable cast for `MaxAsync()` (SQLite compat). 20 integration tests (Sprint 2) + Sprint 3 subdirectory: 14 channel deletion tests, 6+2 E2E smoke tests. Channel deletion sets `IsArchived` (not removed from DB).

**Acceptance.Tests:** Aspire.Hosting.Testing full stack (`FullStackFixture`). `DiscordEventPublisher` simulates Redis events. `BlueMapClient` queries static JSON markers. 6 test classes, xUnit traits (`Category`/`Subcategory`). Serial execution (shared MC container). Timeouts: 5min startup, 3min BlueMap, 5min/job, 10min session.

📌 Team update (2026-02-12): Sprint work items are now GitHub Issues with milestones and squad-colored labels — decided by Jeff and Gordon

📌 Team update (2026-02-13): Village amenities — walkways, scalable fountains, interior sign fix — decided by Batgirl
📌 Team update (2026-02-13): Crossroads hub + spawn + teleport consolidated — central hub at origin (0,0), hub-and-spoke track topology, /goto command, new acceptance tests needed — decided by Jeff, Gordon
📌 Team update (2026-02-13): Sprint 4 plan — 8 work items: Crossroads hub, hub-and-spoke tracks, player teleport, building variety, station relocation, BlueMap markers, E2E tests, Crossroads integration. Account linking deferred again — decided by Gordon
📌 Team update (2026-02-13): Plugin HTTP port 8180 exposed via Aspire for marker wiring — marker calls are best-effort (catch + log) — decided by Oracle
📌 Team update (2026-02-13): /goto command uses Bridge API (/api/buildings/search + /api/buildings/{id}/spawn) for building lookup and teleport — decided by Oracle
📌 Team update (2026-02-13): Hub-and-Spoke track topology — each village gets one track to Crossroads, O(n) instead of O(n²), radial slot positioning at Crossroads — decided by Batgirl
📌 Team update (2026-02-13): Village station relocation to plaza edge — VillageStationOffset=17, shared constant in WorldConstants — decided by Batgirl
📌 Team update (2026-02-13): Crossroads API and BlueMap URL configuration — Bridge.Api has BlueMap:WebUrl config key, /api/crossroads endpoint, /crossroads slash command — decided by Oracle

 Team update (2026-02-13): RconService batch API + fill consolidation across all generators completed  test coverage needed for SendBatchAsync, SendFillBatchAsync, SendSetBlockBatchAsync, and adaptive delay behavior  decided by Gordon
- E2E test scenarios (S5-05, #27) added in `tests/Acceptance.Tests/EndToEndScenarioTests.cs` — 5 tests covering full guild sync, channel create/delete events, track job topology verification, and status endpoint count accuracy
- E2E tests exercise the .NET service layer (Bridge.Api, Redis event consumer, WorldGen job queue) without depending on Minecraft RCON commands — uses API endpoints and Redis events as test drivers
- Building style (MedievalCastle/TimberCottage/StoneWatchtower via ChannelId % 3) is a WorldGen-layer concept not persisted in the DB — E2E tests verify building index assignment and associations instead
- `/api/status` endpoint returns `villageCount` and `buildingCount` (excludes archived) but does NOT include `playerCount` — status test validates count arithmetic including archival
- E2E tests use `[Trait("Subcategory", "E2E")]` for filtering alongside existing Smoke/Tracks/Archival subcategories
- Event processing delay (5 seconds) sufficient for DiscordEventConsumer pub/sub → DB writes; no WorldGen/RCON dependency in E2E tests
- Track job verification is timing-sensitive — WorldGenJobProcessor enqueues CreateTrack after CreateVillage completes, but the job may be processed before we can observe it in the queue. Test validates Crossroads hub topology via API as fallback

 Team update (2026-02-13): Generic villager NPCs removed from villages (SummonVillagersAsync deleted)  future: Discord bots as entities  decided by Jeff via Batgirl

 Team update (2026-02-13): Discord Pins  Building Library (S5-03)  new endpoint POST /api/buildings/{id}/pin + UpdateBuilding job processing  tests may be needed  decided by Lucius
 Team update (2026-02-13): Dynamic building sizing (S5-08)  BuildingSize enum + DeriveSize + BuildingDimensions + MemberCount pipeline  tests may be needed  decided by Gordon
