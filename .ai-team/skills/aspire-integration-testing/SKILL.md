---
name: "aspire-integration-testing"
description: "Patterns for integration testing Aspire-hosted .NET services with WebApplicationFactory, Testcontainers, and SQLite"
domain: "testing"
confidence: "medium"
source: "earned"
---

## Context
When writing integration tests for .NET Aspire services that use `AddNpgsqlDbContext` and `AddRedisClient` (or similar Aspire component methods). Applies to any service using Aspire 13.x with PostgreSQL + Redis.

## Patterns
- Use `WebApplicationFactory<Program>` to host the service under test — avoids Aspire AppHost orchestration in tests
- Aspire component methods (`AddNpgsqlDbContext`, `AddRedisClient`) validate connection strings at registration time — provide fake connection strings via `builder.UseSetting("ConnectionStrings:name", "...")` in `ConfigureWebHost`
- Swap Npgsql for SQLite in-memory by removing ALL EF Core and Npgsql service descriptors (match by `FullName` containing `"EntityFramework"`, `"Npgsql"`, `"DbContext"`) then re-registering with `AddDbContext<T>(o => o.UseSqlite(...))`
- SQLite in-memory with `Cache=Shared` requires a keep-alive `SqliteConnection` that stays open for the entire fixture lifetime
- Create the schema via `EnsureCreatedAsync()` during fixture initialization, before the host starts
- Use Testcontainers Redis for real pub/sub testing — mocking `ISubscriber.OnMessage` callbacks is brittle
- Implement `IAsyncLifetime` on the factory class for container lifecycle (note: xUnit `IAsyncLifetime` uses `Task`, not `ValueTask`)
- Use `IClassFixture<BridgeApiFactory>` to share the factory across tests in a class — avoids container churn

## Examples
```csharp
// Keep-alive connection pattern for SQLite in-memory
private SqliteConnection _keepAlive = null!;
_keepAlive = new SqliteConnection(connStr);
await _keepAlive.OpenAsync();
// ... in DisposeAsync: await _keepAlive.DisposeAsync();

// Removing Aspire-registered services
var toRemove = services.Where(d => {
    var st = d.ServiceType.FullName ?? "";
    return st.Contains("EntityFramework", OrdinalIgnoreCase)
        || st.Contains("Npgsql", OrdinalIgnoreCase)
        || st.Contains("DbContext", OrdinalIgnoreCase);
}).ToList();
foreach (var d in toRemove) services.Remove(d);

// Provider-agnostic Max query (works on both Npgsql and SQLite)
var maxIndex = await db.Items.Select(x => (int?)x.Index).MaxAsync();
var nextIndex = (maxIndex ?? -1) + 1;
```

## Anti-Patterns
- Don't use `DefaultIfEmpty(value).MaxAsync()` — SQLite provider can't translate it
- Don't forget to provide connection strings before Aspire components register — they validate eagerly
- Don't use `BuildServiceProvider()` inside `ConfigureServices` to call `EnsureCreated()` — it creates a separate DI container with stale registrations
- Don't close the SQLite keep-alive connection before the test host shuts down — the in-memory database disappears
- Don't try to partially remove Npgsql services — EF Core detects multiple providers if any Npgsql registrations remain
