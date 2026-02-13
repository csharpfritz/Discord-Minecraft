# Decision: /goto command uses Bridge API for building lookup

**By:** Oracle  
**Date:** 2026-02-13  
**Sprint:** S4-03  

## What
The `/goto <channel-name>` in-game command queries the Bridge API (`/api/buildings/search` + `/api/buildings/{id}/spawn`) to find and teleport players to Discord channel buildings. No account linking required. Bridge API URL is configurable in the plugin's `config.yml` (`bridge-api-url`, default `http://localhost:5169`).

## Why
Keeps coordinate calculation logic server-side in the .NET API (single source of truth matching BuildingGenerator layout). The plugin stays thin — just HTTP calls + teleport. Uses `java.net.http.HttpClient` to avoid adding external HTTP dependencies to the Gradle build. 5-second cooldown prevents API spam.

## Impact
- Bridge API has two new public endpoints other agents may use
- Plugin config.yml has a new `bridge-api-url` key — Aspire/AppHost may need to wire the correct URL when running in containers
