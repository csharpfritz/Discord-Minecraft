# Session: 2026-02-12 — Sprint 3 Scope Update

**Requested by:** Jeffrey T. Fritz

## Summary

Sprint 3 scope changes applied per user directives:

- **Closed Issue #2** (Account linking flow) — deferred to a future sprint
- **Updated Issue #1** — removed `/link` command from Paper Bridge Plugin scope
- **Updated Issue #6** — removed `/unlink` command from Discord slash commands scope
- **Added public-channels-only constraint** comments to Issues #4 and #5

## Directives Captured

1. **Account linking deferred** — Account linking (Issue #2) is removed from Sprint 3. The `/link` command is removed from the Paper Bridge Plugin (Issue #1) and `/unlink` is removed from Discord slash commands (Issue #6). Feature will be revisited in a future sprint.
2. **Public channels only** — Only publicly accessible Discord channels should be mapped to Minecraft village buildings. Private, restricted, and channels not visible to @everyone are excluded.

## Decisions Merged

- `copilot-directive-2026-02-12T1622-account-linking.md` → decisions.md
- `copilot-directive-2026-02-12T1622-public-channels-only.md` → decisions.md

## Cross-Agent Updates

- **Oracle** — notified: account linking removed (Issues #1, #2, #6)
- **Lucius** — notified: public channels only (Issue #5)
- **Batgirl** — notified: public channels only (Issue #4)
