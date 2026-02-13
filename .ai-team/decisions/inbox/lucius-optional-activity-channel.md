# Decision: Make discord-activity-channel-id an optional config value

**Author:** Lucius (Backend Dev)
**Date:** 2025-07-15
**Status:** Implemented

## Context

The `discord-activity-channel-id` Aspire parameter on line 40 of `src/AppHost/AppHost.cs` was declared via `builder.AddParameter("discord-activity-channel-id")`. Aspire's parameter resolution is strict — if the value is missing from configuration, the entire host fails to start, blocking all services (Discord bot, WorldGen worker, Bridge API).

The downstream consumer (`WorldActivityFeedService`) already handles a missing/empty value gracefully: it logs a warning and disables itself. The problem was exclusively at the Aspire orchestration layer.

## Decision

Replaced `builder.AddParameter("discord-activity-channel-id")` with a direct configuration read:

```csharp
var discordActivityChannelId = builder.Configuration["Parameters:discord-activity-channel-id"] ?? "";
```

This bypasses Aspire's strict parameter validation while preserving the same configuration key path. When the value is present, it flows through as before. When absent, an empty string is passed, and `WorldActivityFeedService` handles it by disabling the activity feed.

## Alternatives Considered

- **`AddParameter` with `defaultValue`**: Not supported in Aspire 9 / .NET 10 — the API only accepts a `secret` bool, no default value parameter.
- **Try/catch around `AddParameter`**: Anti-pattern; masks real configuration errors.
- **Conditional `WithEnvironment`**: More complex, would require null-checking and conditional builder chaining for no additional benefit since the service already handles empty values.

## Impact

- `AppHost` no longer crashes when `discord-activity-channel-id` is missing from config.
- All services start normally regardless of whether the activity channel is configured.
- Zero behavior change when the parameter IS provided — same env var, same value.
