# Project Context

- **Owner:** Jeffrey T. Fritz (csharpfritz@users.noreply.github.com)
- **Project:** Discord-to-Minecraft bridge — maps Discord channels to Minecraft villages/buildings with minecart navigation between channel groups. Creative/peaceful mode, .NET 10/Aspire 13.1/C#.
- **Stack:** .NET 10, Aspire 13.1, C#, Discord.NET, Minecraft protocol
- **Created:** 2026-02-11

## Learnings

<!-- Append new learnings below. Each entry is something lasting about the project. -->

📌 Team update (2026-02-11): System architecture established — 3 .NET services (Discord Bot, Bridge API, WorldGen Worker) + Paper MC + PostgreSQL + Redis, orchestrated by Aspire 13.1 — decided by Gordon
📌 Team update (2026-02-11): Paper MC chosen as Minecraft server platform (itzg/minecraft-server Docker container, orchestrated by Aspire) — decided by Gordon
📌 Team update (2026-02-11): Sprint plan defined — 3 sprints: Foundation, Core Features, Integration & Navigation — decided by Gordon
📌 Team update (2026-02-11): Channel deletion archives buildings (does not destroy them) — decided by Gordon
📌 Team update (2026-02-11): Account linking via one-time 6-char codes with 5-min Redis TTL (no OAuth) — decided by Gordon

- **CoreRCON 5.4.2** — `RCON(IPAddress, ushort port, string password)` constructor, then `ConnectAsync()`, then `SendCommandAsync(string)`. Requires `System.Net.Dns.GetHostAddressesAsync` to resolve hostnames to IPAddress. Implements `IDisposable`.
- **Discord.Net 3.18.0** — `DiscordSocketClient` is the gateway client. Config via `DiscordSocketConfig` (set `GatewayIntents`, `LogLevel`). Register as singleton, inject into `BackgroundService`. Login with `LoginAsync(TokenType.Bot, token)` + `StartAsync()`.
- Discord.NET logging bridge: map `LogSeverity` enum to `Microsoft.Extensions.Logging.LogLevel` via switch expression. Wire `client.Log` event.
- Slash commands: use `SlashCommandBuilder` → `client.CreateGlobalApplicationCommandAsync()` in the `Ready` event. Handle via `client.SlashCommandExecuted` event with `SocketSlashCommand` parameter.
- Bot token config key: `Discord:BotToken` (reads from user secrets or env var `Discord__BotToken`)
- Gateway intents: `GatewayIntents.Guilds` is minimum for slash commands. Future S2-01 work will need `GatewayIntents.GuildMessages` etc.
- Graceful shutdown pattern: `Task.Delay(Timeout.Infinite, stoppingToken)` in a try/catch for `OperationCanceledException`, then `client.StopAsync()`.
- **File paths:**
  - `tools/RconTest/Program.cs` — RCON connectivity PoC (S1-05). Usage: `dotnet run -- <host> <port> <password>`
  - `src/DiscordBot.Service/Program.cs` — DI setup, registers `DiscordSocketClient` and `DiscordBotWorker`
  - `src/DiscordBot.Service/DiscordBotWorker.cs` — `BackgroundService` with gateway connection, logging, `/ping` command
- Solution file is `DiscordMinecraft.slnx` (XML-based .slnx format, not classic .sln)
- RconTest added to solution under `/tools/` folder

📌 Team update (2026-02-11): Test projects under tests/{ProjectName}.Tests/, CI at .github/workflows/ci.yml with .NET 10 — decided by Nightwing
📌 Team update (2026-02-11): Snake_case PostgreSQL table names with PascalCase C# entities — decided by Lucius
📌 Team update (2026-02-11): RCON password as Aspire secret parameter via builder.AddParameter("rcon-password", secret: true) — decided by Lucius
📌 Team update (2026-02-11): EF Core enum-to-string conversion for GenerationJobStatus — decided by Lucius
📌 Team update (2026-02-11): Discord bot token as Aspire secret parameter — passed via env var Discord__BotToken, reads as Discord:BotToken in .NET config — decided by Lucius
