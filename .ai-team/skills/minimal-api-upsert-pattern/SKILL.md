# SKILL: Minimal API Upsert Pattern with EF Core

**Confidence:** medium
**Source:** earned
**Tags:** aspire, ef-core, minimal-api, upsert, .NET

## What

Implement Discord→DB sync endpoints using Minimal API + EF Core upsert pattern: query by Discord ID, create-or-update, compute dependent values (indices, coordinates) at insert time.

## Pattern

```csharp
app.MapPost("/api/mappings/sync", async (SyncRequest request, BridgeDbContext db) =>
{
    int created = 0, updated = 0;

    foreach (var dto in request.Items)
    {
        var entity = await db.Entities
            .FirstOrDefaultAsync(e => e.DiscordId == dto.DiscordId);

        if (entity is null)
        {
            var index = await db.Entities.CountAsync();
            entity = new Entity
            {
                DiscordId = dto.DiscordId,
                Name = dto.Name,
                Index = index,
                // Compute derived values from index
            };
            db.Entities.Add(entity);
            created++;
        }
        else
        {
            entity.Name = dto.Name;
            updated++;
        }

        await db.SaveChangesAsync();
    }

    return Results.Ok(new { created, updated });
});
```

## Key Details

- Query by `DiscordId` (unique business key), not by primary key — Discord IDs are the source of truth.
- `SaveChangesAsync()` after each parent entity to ensure the auto-generated `Id` is available for child entities.
- Compute ordinal indices (VillageIndex, BuildingIndex) from existing row counts at creation time.
- Use `MAX(Index) + 1` for child indices to handle gaps from archived records.
- Request DTOs as records at file bottom keep Program.cs self-contained.

## When to Use

- Any Discord-to-database sync where Discord snowflake IDs are the natural key.
- Upsert patterns where creation requires computing dependent/sequential values.

## When NOT to Use

- High-concurrency writes — use serializable transactions or database sequences instead.
- Bulk imports where EF Core `ExecuteUpdate`/`ExecuteDelete` would be more efficient.
