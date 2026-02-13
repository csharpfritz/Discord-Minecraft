### 2026-02-13: Dynamic Building Sizing (S5-08)
**By:** Gordon
**What:** Buildings now scale with Discord channel member count. Three tiers:
- **Small** (<10 members): 15×15 footprint, 2 floors
- **Medium** (10-30 members): 21×21 footprint, 3 floors (default, backward compatible)
- **Large** (30+ members): 27×27 footprint, 4 floors

Implementation:
1. `BuildingSize` enum in `Bridge.Data` (Small/Medium/Large)
2. `MemberCount` optional field (default 0) added to `BuildingJobPayload` and `BuildingGenerationRequest`
3. `BuildingGenerator.DeriveSize(int memberCount)` static method for size derivation
4. `BuildingDimensions` private record holds computed footprint/floors/wallTop/roofY per building
5. All generation methods (walls, stairs, roof, windows, lighting, interiors) accept and use `BuildingDimensions`
6. `DiscordBotWorker` passes `channel.Users.Count` through sync payload
7. `Bridge.Api` sync endpoint passes `MemberCount` to `BuildingJobPayload`
8. `WorldGenJobProcessor` passes `MemberCount` to `BuildingGenerationRequest`

**Why:** Channels with more members deserve larger buildings. The tiered approach keeps the design predictable (only 3 sizes) while making villages visually reflect their Discord community. Optional defaults ensure full backward compatibility — existing buildings and any code paths that don't supply member count get Medium (the previous default behavior, now with 3 floors instead of 2).

**Trade-offs:**
- Medium now has 3 floors (was 2) — this is a deliberate upgrade. New medium buildings will be slightly taller than before.
- Building spacing (24 blocks) is unchanged — Large buildings (27×27) extend 3 blocks further in each direction but still fit within the grid because the spacing was already generous.
- Interior furnishing (Batgirl's code) uses relative coordinates from building edges, so it scales automatically. No hardcoded 21×21 assumptions.
- Arrow slit positions, window spacing, and torch placement scale proportionally with `dim.HalfFootprint`.
