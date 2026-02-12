# SKILL: Aspire Secret Parameter Injection

**Confidence:** medium
**Source:** earned
**Tags:** aspire, secrets, configuration, .NET

## What

Pass secrets from Aspire AppHost to child projects using `AddParameter` + `WithEnvironment`, leveraging .NET's built-in env var → hierarchical config mapping.

## Pattern

```csharp
// In AppHost.cs — declare the secret parameter
var mySecret = builder.AddParameter("my-secret", secret: true);

// Wire it to the project via environment variable
var myService = builder.AddProject<Projects.MyService>("my-service")
    .WithEnvironment("Section__Key", mySecret);
```

```csharp
// In the consuming service — read via standard IConfiguration
var value = configuration["Section:Key"];
```

## Key Details

- `AddParameter("name", secret: true)` stores the value in user secrets during development and maps to proper secret stores in production.
- Aspire dashboard masks secret parameter values automatically.
- .NET configuration maps `__` (double underscore) in env var names to `:` (colon) in hierarchical config keys. No custom code needed.
- The consuming service doesn't need to know it's running under Aspire — it just reads `IConfiguration` normally.

## When to Use

- Any time a service needs a secret (API key, token, password) that the AppHost controls.
- Prefer this over hardcoding, plain env vars, or passing secrets through `appsettings.json`.

## When NOT to Use

- For non-secret configuration values — use `WithEnvironment` directly with string values.
- For connection strings managed by Aspire resources (e.g., `WithReference(postgres)` handles connection strings automatically).
