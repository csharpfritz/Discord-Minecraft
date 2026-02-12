### 2026-02-12: /status and /navigate slash commands with Bridge API endpoints
**By:** Oracle
**What:**
1. **Two new slash commands** — `/status` (village count, building count) and `/navigate <channel>` (village name, building index, XYZ coordinates). Registered globally in the `Ready` event alongside existing `/ping` and `/map` commands.
2. **Two new Bridge API endpoints** — `GET /api/status` returns `{ villageCount, buildingCount }` (non-archived only). `GET /api/navigate/{discordChannelId}` returns full mapping including coordinates, archived status, and village center. Returns 404 for unmapped channels.
3. **HttpClient via Aspire service discovery** — Named `HttpClient("BridgeApi")` with `https+http://bridge-api` base address registered in `DiscordBot.Service/Program.cs`. Leverages Aspire's existing `WithReference(bridgeApi)` and `AddServiceDefaults()` service discovery.
4. **Defer/Followup pattern** — Both commands use `DeferAsync()` + `FollowupAsync()` since they make HTTP calls to Bridge API. This avoids Discord's 3-second interaction timeout.
5. **Edge cases handled** — Unmapped channel shows clear message mentioning public-text-channel-in-category requirement. API unavailability shows warning. Invalid channel option handled gracefully.
**Why:** Slash commands are the primary Discord user interface for world discovery. Using Aspire service discovery avoids hardcoded URLs and works across development/production environments. The Defer pattern is mandatory for any command making external HTTP calls due to Discord's strict response deadline. The `/api/navigate` endpoint was designed to return enough data for a rich embed without requiring multiple API calls.
