# Decision: World Activity Feed architecture — DiscordBot.Service, not Bridge.Api

**By:** Lucius  
**Date:** 2026-02-13  
**Task:** S5-07 (GitHub #33)

## What

World activity events flow: WorldGen Worker → Redis pub/sub (`events:world:activity`) → DiscordBot.Service → Discord embed in `#world-activity` channel.

The subscriber/poster lives in **DiscordBot.Service** (as `WorldActivityFeedService`), NOT in Bridge.Api. The task description suggested Bridge.Api, but Bridge.Api has no Discord.NET dependency and no `DiscordSocketClient` — it's a pure HTTP API + Redis event consumer for DB mutations.

## Why

- DiscordBot.Service already has `DiscordSocketClient` as a singleton and `Discord.Net` NuGet reference.
- Adding Discord.NET to Bridge.Api would create an unnecessary dependency and a second Discord gateway connection.
- The existing pattern: DiscordBot.Service handles all Discord interactions; Bridge.Api handles all HTTP API + DB operations.

## Configuration

- `Discord:ActivityChannelId` config key (env var `Discord__ActivityChannelId`) — the numeric Discord channel ID to post embeds to.
- Aspire parameter `discord-activity-channel-id` in AppHost.cs feeds this env var.
- If not configured, the service logs a warning and disables itself gracefully.

## Rate Limiting

- Max 1 embed per 5 seconds via `ConcurrentQueue<WorldActivityEvent>` + timer drain loop.
- Events that arrive during the cooldown are queued and posted sequentially.
