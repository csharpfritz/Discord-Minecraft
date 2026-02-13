---
name: "aspire-acceptance-testing"
description: "Full-stack acceptance testing with Aspire.Hosting.Testing — launching entire AppHost including containers"
domain: "testing"
confidence: "low"
source: "earned"
---

## Context
When you need to test the entire Aspire-orchestrated application — all services, containers, and infrastructure — rather than individual services in isolation. Useful for end-to-end acceptance tests that verify cross-service behavior.

## Patterns

### 1. Use DistributedApplicationTestingBuilder
```csharp
var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.AppHost>();
_app = await builder.BuildAsync();
await _app.StartAsync();
```

This launches the full AppHost including all `AddProject`, `AddContainer`, `AddPostgres`, `AddRedis` etc.

### 2. Get Service Endpoints
```csharp
var apiEndpoint = _app.GetEndpoint("bridge-api", "http");
var httpClient = new HttpClient { BaseAddress = new Uri(apiEndpoint.ToString()) };
```

### 3. Get Connection Strings
```csharp
var redisConnStr = await _app.GetConnectionStringAsync("redis");
var redis = await ConnectionMultiplexer.ConnectAsync(redisConnStr);
```

### 4. xUnit Collection Pattern for Serial Execution
```csharp
[CollectionDefinition("FullStack")]
public sealed class FullStackCollection : ICollectionFixture<FullStackFixture> { }

[Collection("FullStack")]
public class MyTests : IClassFixture<FullStackFixture> { }
```
All tests in the same collection share one fixture instance and run serially.

### 5. Container Readiness Polling
```csharp
var retryPolicy = Policy
    .Handle<HttpRequestException>()
    .WaitAndRetryAsync(36, _ => TimeSpan.FromSeconds(5));

await retryPolicy.ExecuteAsync(async () => {
    var response = await httpClient.GetAsync("/");
    response.EnsureSuccessStatusCode();
});
```

### 6. Job Queue Completion Detection
```csharp
while (!cts.IsCancellationRequested)
{
    var length = await db.ListLengthAsync("queue:worldgen");
    if (length == 0) {
        await Task.Delay(2000, cts.Token); // Let in-progress job finish
        if (await db.ListLengthAsync("queue:worldgen") == 0) return;
    }
    await Task.Delay(3000, cts.Token);
}
```

## Required Packages
```xml
<PackageReference Include="Aspire.Hosting.Testing" Version="13.1.0" />
<PackageReference Include="Polly" Version="8.5.2" />
```

## Runsettings for Long Tests
```xml
<RunSettings>
  <RunConfiguration>
    <TestSessionTimeout>600000</TestSessionTimeout>
  </RunConfiguration>
  <xUnit>
    <MaxParallelThreads>1</MaxParallelThreads>
    <ParallelizeTestCollections>false</ParallelizeTestCollections>
  </xUnit>
</RunSettings>
```

## Anti-Patterns
- Don't run acceptance tests in parallel with a single container — use serial execution
- Don't skip readiness checks — containers take time to start
- Don't assume instant job completion — world generation is async
- Don't forget to dispose the DistributedApplication — it stops all containers

## When to Use
- End-to-end acceptance tests spanning multiple services
- Verifying container integration (Minecraft, databases, etc.)
- Testing event-driven pipelines that cross service boundaries
- Validating external service markers/outputs (BlueMap, etc.)

## When NOT to Use
- Unit tests (too slow, use mocks)
- Integration tests for a single service (use WebApplicationFactory)
- CI on every commit (too slow, run nightly or on-demand)
