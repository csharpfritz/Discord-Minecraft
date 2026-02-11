---
name: "aspire-apphost-wiring"
description: "How to wire up .NET Aspire 13.1 AppHost with containers, databases, and service projects"
domain: "infrastructure"
confidence: "low"
source: "earned"
---

## Context
When scaffolding a .NET Aspire 13.1 solution with multiple services, containers, and infrastructure resources. Applies to any project using Aspire for orchestration.

## Patterns

### Template selection
- `aspire-apphost` for the orchestrator (generates `AppHost.cs`, not `Program.cs`)
- `aspire-servicedefaults` for shared defaults (OTel, health checks, service discovery)
- Standard `webapi`, `worker`, `classlib` templates for service projects — Aspire doesn't need special service templates

### Container endpoints for non-HTTP services
Non-HTTP containers (Minecraft, game servers, TCP services) need explicit `scheme: "tcp"`:
```csharp
.WithEndpoint(targetPort: 25565, port: 25565, name: "minecraft", scheme: "tcp")
```

### Secret parameters
Use `builder.AddParameter("name", secret: true)` for secrets that containers need:
```csharp
.WithEnvironment("RCON_PASSWORD", builder.AddParameter("rcon-password", secret: true))
```
This integrates with user secrets in dev and proper stores in production.

### Service wiring pattern
Services call `builder.AddServiceDefaults()` plus their specific Aspire client integrations:
```csharp
builder.AddNpgsqlDbContext<MyDbContext>("connectionName");
builder.AddRedisClient("redis");
```
The connection name must match what the AppHost defines (e.g., `.AddDatabase("bridgedb")` → `"bridgedb"`).

### EF Core Design package placement
`Microsoft.EntityFrameworkCore.Design` must be in BOTH the data class library AND the startup project for `dotnet ef migrations` to work.

## Examples
See `src/AppHost/AppHost.cs` in the Discord-Minecraft project for a complete multi-service, multi-container orchestration.

## Anti-Patterns
- Don't hardcode connection strings — let Aspire inject them via service discovery
- Don't use `aspire-starter` if you need custom project layouts — scaffold individual projects instead
- Don't forget `WaitFor()` chains — services that depend on infrastructure should wait for it
- Don't mix EF Core package versions between the Aspire integration and your data library — pin explicitly if needed
