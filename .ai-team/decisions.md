# Decisions

> Shared decision log. All agents read this. Scribe merges new entries from the inbox.

### 2026-02-11: System architecture for Discord-Minecraft bridge
**By:** Gordon
**What:** Established the service decomposition (3 .NET services + Paper MC container), communication patterns (Redis pub/sub + job queue), state storage (PostgreSQL), and world generation strategy (superflat, 500-block grid villages, RCON + Bridge Plugin hybrid). Full details in `docs/architecture.md`.
**Why:** Need clear service boundaries and technology choices before any implementation begins. Event-driven architecture decouples Discord's event rate from Minecraft's world generation rate. Hybrid RCON + plugin approach balances simplicity (RCON for basic commands) with capability (plugin API for complex structures). Superflat world eliminates terrain complexity and gives predictable coordinates.

### 2026-02-11: Paper MC as the Minecraft server platform
**By:** Gordon
**What:** Using Paper MC (not Vanilla, Fabric, or Forge) running in the `itzg/minecraft-server` Docker container, orchestrated by Aspire.
**Why:** Paper has the best plugin API for server-side world manipulation, excellent performance, and the Docker image is battle-tested with env-var configuration. Aspire's container support makes this a first-class citizen in our service graph.

### 2026-02-11: Sprint plan for first 3 sprints
**By:** Gordon
**What:** Defined 3 sprints (Foundation, Core Features, Integration & Navigation) with 7 work items each, assigned to team members by expertise. Full details in `docs/sprints.md`.
**Why:** Sprint 1 establishes the infrastructure so all subsequent work has a running environment. Sprint 2 delivers the core Discord→Minecraft pipeline. Sprint 3 adds player-facing features (account linking, navigation). This ordering minimizes blocked work items.

### 2026-02-11: Channel deletion archives buildings, does not destroy them
**By:** Gordon
**What:** When a Discord channel is deleted, its corresponding Minecraft building is marked archived (signs updated, entrance blocked with barriers) but NOT demolished.
**Why:** Destroying structures while players might be inside is dangerous. Archived buildings preserve world continuity and can be repurposed if channels are recreated. This is the safe default — we can add a `/demolish` admin command later if needed.

### 2026-02-11: Account linking via one-time codes, not OAuth
**By:** Gordon
**What:** Players link Discord↔Minecraft accounts by generating a 6-char code via Discord `/link`, then typing `/link <code>` in-game within 5 minutes.
**Why:** Avoids OAuth complexity. Player proves ownership of both accounts by being present in both systems simultaneously. Redis TTL handles expiry automatically. Simple for players to understand.

### 2026-02-11: Discord bot uses singleton DiscordSocketClient with BackgroundService pattern
**By:** Oracle
**What:** The Discord bot registers `DiscordSocketClient` as a singleton in DI and injects it into a `DiscordBotWorker : BackgroundService`. The client stays alive via `Task.Delay(Timeout.Infinite, stoppingToken)` and shuts down gracefully on host cancellation. Slash commands are registered globally in the `Ready` event and handled via `SlashCommandExecuted`.
**Why:** Discord.NET's `DiscordSocketClient` manages its own WebSocket connection and must be a singleton — multiple instances would create duplicate gateway connections. The `BackgroundService` pattern integrates cleanly with .NET's generic host and Aspire's lifecycle management. Global command registration (vs. guild-specific) ensures commands work across all guilds without per-guild setup, though they take up to an hour to propagate on first registration.

### 2026-02-11: Test project structure and CI pipeline conventions
**By:** Nightwing
**What:** Established test project convention: test projects live under `tests/{ProjectName}.Tests/`, added to solution under `/tests/` folder. CI workflow at `.github/workflows/ci.yml` runs restore → build → test on ubuntu-latest with .NET 10. The test project `Bridge.Api.Tests` uses xUnit and is the first test project in the solution.
**Why:** Consistent test project placement makes it easy to add more test projects later (e.g., `DiscordBot.Service.Tests`, `WorldGen.Worker.Tests`). Running CI at the solution root means new projects are automatically included in CI when added to the `.slnx`. Using xUnit aligns with Aspire ecosystem conventions.

### 2026-02-11: Snake_case PostgreSQL table names with PascalCase C# entities
**By:** Lucius
**What:** Database tables use snake_case naming (`channel_groups`, `generation_jobs`) while C# entity classes use PascalCase (`ChannelGroup`, `GenerationJob`). Column names remain PascalCase (EF Core default for Npgsql).
**Why:** Snake_case tables follow PostgreSQL conventions and make raw SQL queries natural. PascalCase C# follows .NET conventions. The Fluent API `.ToTable()` call makes the mapping explicit without requiring a global naming convention.

### 2026-02-11: RCON password as Aspire secret parameter
**By:** Lucius
**What:** RCON password for the Minecraft container is provisioned via `builder.AddParameter("rcon-password", secret: true)` rather than a hardcoded string or environment variable.
**Why:** Aspire secret parameters integrate with user secrets in development and can be mapped to proper secret stores in production. No secrets in source control, no magic strings, and the Aspire dashboard masks the value.

### 2026-02-11: EF Core enum-to-string conversion for GenerationJobStatus
**By:** Lucius
**What:** The `GenerationJobStatus` enum is stored as a string column in PostgreSQL (via `HasConversion<string>()`) rather than as an integer.
**Why:** String storage makes the database human-readable when querying directly, and it's resilient to enum reordering. The performance difference is negligible for a job audit table.

### 2026-02-11: Discord bot token as Aspire secret parameter
**By:** Lucius
**What:** Discord bot token is provisioned via `builder.AddParameter("discord-bot-token", secret: true)` in AppHost.cs and passed to the discord-bot project via `.WithEnvironment("Discord__BotToken", discordBotToken)`. The worker reads it from `configuration["Discord:BotToken"]` — .NET config maps double-underscore env vars to hierarchical keys automatically.
**Why:** Follows the same pattern established for RCON password. Aspire secret parameters integrate with user secrets in dev and proper secret stores in production. The env var naming convention (`__` → `:`) is a built-in .NET feature, so no custom mapping code is needed. Keeps secrets out of source control and masked in the Aspire dashboard.
