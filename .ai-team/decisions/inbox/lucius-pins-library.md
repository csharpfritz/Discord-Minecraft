### Discord Pins → Building Library (S5-03)
**By:** Lucius
**What:**
1. `POST /api/buildings/{id}/pin` endpoint accepts pin data and enqueues `UpdateBuilding` job via Redis.
2. `UpdateBuildingJobPayload` in Bridge.Data/Jobs/ carries pin content + building coordinates.
3. `PinDisplayService` places up to 6 preview signs on south interior wall (signX = bx-5, signZ = bz+HalfFootprint-1, Y = BaseY+2..+7) and a lectern with written book via Paper plugin HTTP API.
4. Paper plugin `POST /plugin/lectern` endpoint places a lectern with a written book using Bukkit API on the main thread.
5. Signs use `oak_wall_sign[facing=north]` at signX = bx-5 (away from existing floor signs at bx+3). Lectern at `(bx-5, BaseY+1, bz+HalfFootprint-3)`.
6. PinDisplayService registered via `AddHttpClient<PinDisplayService>` with Plugin:BaseUrl, injected into WorldGenJobProcessor.

**Why:** Pinned Discord messages need a physical representation inside buildings. Signs provide at-a-glance preview (6 signs × 60 chars = 360 chars max). Lectern with written book provides full content. Plugin HTTP API handles lectern because RCON `setblock` can't set book contents — Bukkit API with BookMeta is required. Sign positions chosen to avoid conflicts with existing interior furnishing (topic signs at bx-3, floor signs at bx+3).

**Affects:** Bridge.Api (new endpoint), Bridge.Data (new payload), WorldGen.Worker (processor + service + DI), Paper plugin (new endpoint). Oracle/DiscordBot will need to call this endpoint when a message is pinned.
