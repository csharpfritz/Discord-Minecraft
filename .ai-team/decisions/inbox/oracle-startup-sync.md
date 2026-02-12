# Decision: Startup guild sync in DiscordBotWorker

**By:** Oracle
**Date:** 2026-02-12
**What:** Added `SyncGuildsAsync()` to the Discord bot's `Ready` event handler. On startup, the bot iterates all guilds, collects publicly accessible categories and their text channels (filtering out channels where @everyone has ViewChannel explicitly denied), and POSTs a `SyncRequest` to `/api/mappings/sync` for each guild. This populates the Bridge API database with the current Discord channel structure so villages/buildings appear immediately.

**Key details:**
1. Called AFTER `RegisterSlashCommandsAsync` in the Ready handler
2. Uses `GetPermissionOverwrite(guild.EveryoneRole)` to check `ViewChannel` — only explicit `Deny` is filtered; inherit/allow pass through
3. Only `SocketTextChannel` types within categories are included (no voice, forum, or uncategorized channels)
4. Wrapped in try/catch — sync failure logs an error but does NOT prevent the bot from running
5. Sync DTOs (`SyncRequest`, `SyncChannelGroup`, `SyncChannel`) are private records in `DiscordBotWorker`, matching the Bridge API's contract
6. No changes needed to `Program.cs` — `GatewayIntents.Guilds` already configured, which is sufficient

**Why:** The `/api/mappings/sync` endpoint existed but was never called. Without an initial sync, zero villages/buildings would appear until individual channel events fired. This startup sync ensures the Minecraft world reflects the current Discord server structure immediately on bot ready.
