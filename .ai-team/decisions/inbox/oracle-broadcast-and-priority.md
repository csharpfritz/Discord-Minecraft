# Decision: World broadcast messages and spawn-proximity build priority

**By:** Oracle
**Date:** 2026-02-13
**Requested by:** Jeffrey T. Fritz

## What
1. **RCON `tellraw @a` broadcasts** during world generation — players in-game see colorful progress messages when structures start/finish building (villages, buildings, tracks, Crossroads hub).
2. **Spawn-proximity job priority** — the WorldGen job queue now prioritizes structures closer to spawn (0,0) instead of strict FIFO. Uses Euclidean distance scoring with Redis LSET+LREM sentinel pattern for atomic removal.

## Why
- Players connecting to the server should see nearby content being built first, creating a better first impression.
- Broadcast messages give real-time feedback about world generation progress without requiring the Discord bot or external dashboards.
- Both features are best-effort and never fail actual generation jobs.

## Impact
- `WorldGenJobProcessor.cs` — new constructor dependency on `RconService`, `PopClosestJobAsync` replaces `ListRightPopAsync`, broadcast calls wrap `DispatchJobAsync`.
- `CrossroadsInitializationService.cs` — new constructor dependency on `RconService`, broadcast after hub generation.
- No new packages or DI registrations needed (RconService was already a singleton).
