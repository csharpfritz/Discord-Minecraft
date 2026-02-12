# Work Routing

How to decide who handles what.

## Routing Table

| Work Type | Route To | Examples |
|-----------|----------|----------|
| Architecture & design | Gordon | System design, service boundaries, API contracts, tech decisions |
| Discord integration | Oracle | Discord bot, event handling, channel sync, Discord.NET |
| Minecraft protocol | Oracle | Minecraft server communication, player bridging |
| .NET services & Aspire | Lucius | Service scaffolding, Aspire orchestration, hosting, configuration |
| World generation | Batgirl | Village layouts, building generation, minecart tracks, creative mode |
| Database & state | Lucius | Persistence, caching, state management |
| Testing & QA | Nightwing | Unit tests, integration tests, edge cases, quality gates |
| Code review | Gordon | Review PRs, architecture compliance, suggest improvements |
| Scope & priorities | Gordon | What to build next, trade-offs, sprint planning |
| Session logging | Scribe | Automatic — never needs routing |

## Rules

1. **Eager by default** — spawn all agents who could usefully start work, including anticipatory downstream work.
2. **Scribe always runs** after substantial work, always as `mode: "background"`. Never blocks.
3. **Quick facts → coordinator answers directly.** Don't spawn an agent for "what port does the server run on?"
4. **When two agents could handle it**, pick the one whose domain is the primary concern.
5. **"Team, ..." → fan-out.** Spawn all relevant agents in parallel as `mode: "background"`.
6. **Anticipate downstream work.** If a feature is being built, spawn the tester to write test cases from requirements simultaneously.
