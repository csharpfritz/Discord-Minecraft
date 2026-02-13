# Decision: Player Welcome & Pressure Plate Walkthrough

**By:** Oracle
**Date:** 2026-02-13

## What

- Golden pressure plate at Crossroads spawn (0, -59, 8) triggers a 5-step title walkthrough for new players
- Lectern info kiosk at (8, -59, 0) with written book world guide
- Player join shows title overlay with configurable guild name + actionbar hint
- `/map` slash command now supports `village-name` option for coordinate-based BlueMap deep-linking

## Why

First-time players need orientation. The pressure plate is physical (you walk over it), the walkthrough uses title commands (high-visibility, no chat spam), and the lectern provides a persistent reference. The `/map village-name` option complements the existing channel deep-link for players/Discord users who think in terms of village (category) names rather than individual channels.

## Impact

- **Batgirl**: Pressure plate is at fixed coords (0, -59, 8) — avoid building anything there
- **Lucius**: VillageResponse record added to DiscordBotWorker — if /api/villages response shape changes, the record needs updating
- **All**: `guild-name` in plugin config.yml defaults to "Discord World" — server operators should set this to their Discord guild name
