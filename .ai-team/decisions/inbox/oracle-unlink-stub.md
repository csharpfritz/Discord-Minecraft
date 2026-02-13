# /unlink Command Stubbed for Deferred Account Linking

**Date:** 2026-02-12  
**Author:** Oracle  
**Sprint Item:** S3-06

## Decision

The `/unlink` Discord slash command is implemented as a stub that responds with "Account linking is not yet available" (ephemeral message). This aligns with the team decision to defer account linking from Sprint 3.

## Rationale

Per the Sprint 3 decisions:
> Account linking deferred from Sprint 3 — S3-02 closed, /link removed from S3-01 (Paper Bridge Plugin), /unlink removed from S3-06 (Discord slash commands)

The command is registered and handled, but simply informs the user the feature is coming. This provides a better UX than an unrecognized command error, and the handler is ready to be filled in when account linking is implemented.

## Implementation

- Command registered in `RegisterSlashCommandsAsync()` with description "Remove your Discord-Minecraft account link"
- Handler `HandleUnlinkCommandAsync()` responds with ephemeral message (only visible to the user who invoked)
- No API calls or Redis interactions — pure stub

## Future Work

When account linking is implemented:
1. Add `/link` command to initiate linking
2. Update `/unlink` handler to call `DELETE /api/players/link/{discordUserId}` (or similar)
3. Remove the "not yet available" stub response
