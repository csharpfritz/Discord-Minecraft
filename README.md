# 🏰 Discord-Minecraft

> **A .NET service that maps your Discord server into a living Minecraft world.**

Discord-Minecraft bridges your Discord community into a custom Minecraft experience. Each Discord **channel category** becomes a **Minecraft village**, each **channel** becomes a **building** within that village, and **minecart rail networks** connect everything together. Players link their Discord and Minecraft accounts and explore a world that mirrors their community's structure in real time.

Built with **.NET 10**, **Aspire 13.1**, **Discord.NET**, and **Paper MC** — fully orchestrated, containerized, and observable from the Aspire dashboard.

---

## ✨ How It Works

1. **Discord bot** monitors channel events (create, update, delete) via Discord.NET
2. Events flow through **Redis pub/sub** to the **Bridge API**, which persists mappings in **PostgreSQL**
3. World generation **jobs** are queued in Redis and picked up by the **WorldGen Worker**
4. The worker builds structures in a **Paper MC** server via RCON commands and a custom Bridge Plugin
5. Players link accounts with a one-time code and ride **minecart tracks** between villages

---

## 🏗️ Architecture

The system is composed of three .NET services, a shared data library, and containerized infrastructure:

| Component | Type | Description |
|-----------|------|-------------|
| **DiscordBot.Service** | Worker Service | Discord.NET gateway bot — monitors channel events, publishes to Redis |
| **Bridge.Api** | ASP.NET Minimal API | Coordination hub — REST endpoints, event consumer, job enqueuing |
| **WorldGen.Worker** | BackgroundService | Dequeues generation jobs, builds villages/buildings via RCON |
| **Bridge.Data** | Class Library | Shared EF Core entities, DTOs, constants |
| **PostgreSQL** | Container | Persistent state — channel→village mappings, player links, job audit trail |
| **Redis** | Container | Pub/sub event bus + list-based job queue |
| **Paper MC** | Container (`itzg/minecraft-server`) | Minecraft server — superflat, creative, peaceful |

📐 For full details, see [docs/architecture.md](docs/architecture.md).

---

## 🚀 Getting Started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Docker Desktop](https://www.docker.com/products/docker-desktop/) (for PostgreSQL, Redis, and Paper MC containers)
- A Discord bot token ([Discord Developer Portal](https://discord.com/developers/applications))

### Setup

1. **Clone the repository**

   ```bash
   git clone https://github.com/csharpfritz/Discord-Minecraft.git
   cd Discord-Minecraft
   ```

2. **Configure secrets** — the Discord bot token and RCON password are managed via .NET user secrets:

   ```bash
   cd src/AppHost
   dotnet user-secrets set "Parameters:discord-bot-token" "YOUR_DISCORD_BOT_TOKEN"
   dotnet user-secrets set "Parameters:rcon-password" "YOUR_RCON_PASSWORD"
   ```

3. **Run the application**

   ```bash
   dotnet run --project src/AppHost
   ```

   This starts all three .NET services plus PostgreSQL, Redis, and Paper MC containers. Open the **Aspire dashboard** (URL shown in console output) to see everything running.

---

## 🦇 The Squad

This project is built by an AI development team, each member with a specialized role:

| | Member | Role | Focus |
|---|--------|------|-------|
| ![squad:gordon](https://img.shields.io/badge/🏗️_Gordon-Lead_/_Architect-0052CC?style=flat-square&labelColor=0052CC&color=0052CC) | **Gordon** | Lead / Architect | Architecture, API contracts, sprint planning |
| ![squad:oracle](https://img.shields.io/badge/⚛️_Oracle-Integration_Dev-7B61FF?style=flat-square&labelColor=7B61FF&color=7B61FF) | **Oracle** | Integration Dev | Discord.NET, Minecraft protocol, Bridge Plugin |
| ![squad:lucius](https://img.shields.io/badge/🔧_Lucius-Backend_Dev-0E8A16?style=flat-square&labelColor=0E8A16&color=0E8A16) | **Lucius** | Backend Dev | .NET services, Aspire, EF Core, PostgreSQL |
| ![squad:batgirl](https://img.shields.io/badge/🌍_Batgirl-World_Builder-D93F0B?style=flat-square&labelColor=D93F0B&color=D93F0B) | **Batgirl** | World Builder | Village/building generation, rail networks |
| ![squad:nightwing](https://img.shields.io/badge/🧪_Nightwing-Tester_/_QA-006B75?style=flat-square&labelColor=006B75&color=006B75) | **Nightwing** | Tester / QA | Integration tests, CI pipeline, E2E smoke tests |
| 📋 | **Scribe** | Session Logger | Decision logging, session history |

**Project Owner:** [Jeffrey T. Fritz](https://github.com/csharpfritz)

---

## 📊 Project Status

We are heading into **Sprint 3: Integration & Navigation** — account linking, minecart track generation, channel deletion handling, and end-to-end smoke tests.

Track progress on the [Sprint 3 milestone](https://github.com/csharpfritz/Discord-Minecraft/milestone/1).

| Sprint | Focus | Status |
|--------|-------|--------|
| Sprint 1 | Foundation — Aspire scaffolding, containers, bot shell | ✅ Complete |
| Sprint 2 | Core Features — event pipeline, world generation | ✅ Complete |
| Sprint 3 | Integration & Navigation — linking, tracks, polish | 🔄 In Progress |

---

## 📄 License

This project is maintained by Jeffrey T. Fritz.
