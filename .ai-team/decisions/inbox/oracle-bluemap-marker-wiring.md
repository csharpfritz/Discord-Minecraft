# Decision: Plugin HTTP port 8180 exposed via Aspire for marker wiring

**By:** Oracle
**Date:** 2026-02-13
**Context:** S4-06 BlueMap Marker Wiring

## What
- Bridge Plugin's HTTP API (port 8180) is now exposed as an Aspire endpoint (`plugin-http`) on the minecraft container.
- WorldGen.Worker receives `Plugin__BaseUrl` env var pointing to this endpoint, read as `Plugin:BaseUrl` in .NET config.
- `MarkerService` registered via `AddHttpClient<MarkerService>` with this base URL.
- All marker calls are best-effort (catch + log) — BlueMap markers are nice-to-have, never critical path.

## Why
- The WorldGen.Worker needs to call the plugin's marker endpoints after generation completes.
- Using Aspire's `GetEndpoint()` + `WithEnvironment()` pattern keeps service discovery consistent with the rest of the system (same as `BlueMap__BaseUrl` for the Discord bot).
- Fire-and-forget pattern ensures generation jobs never fail due to marker issues (BlueMap may not be available during development or if the plugin hasn't started yet).

## Impact
- **Batgirl, Lucius:** If you add new generation job types, consider adding marker calls in `SetMarkersForJobAsync`.
- **All:** Plugin HTTP API is now reachable from WorldGen.Worker — could be used for future cross-service calls beyond markers.
