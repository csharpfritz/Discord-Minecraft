# Session: 2026-02-11-discord-token-config

**Requested by:** Jeffrey T. Fritz

## Summary

Lucius wired the Discord bot token as an Aspire secret parameter in AppHost.cs, following the same pattern as the RCON password. The token is provisioned via `builder.AddParameter("discord-bot-token", secret: true)` and passed to the discord-bot project via `.WithEnvironment("Discord__BotToken", discordBotToken)`. The worker reads it from `configuration["Discord:BotToken"]` — .NET config maps `__` env vars to `:` hierarchical keys automatically.

## Decisions

- Discord bot token added as Aspire secret parameter (merged to decisions.md)
