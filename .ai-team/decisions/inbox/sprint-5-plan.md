### Sprint 5 Plan: Immersion & Onboarding
**By:** Gordon
**What:** Planned Sprint 5 (8 items, 2-week cadence) themed "Immersion & Onboarding." Also retroactively documented Sprint 4 in sprints.md with full work item table and post-sprint RCON optimization notes.

**Sprint 5 items:**
1. **S5-01: Building Interior Furnishing** (Batgirl, L) — Style-specific furnished interiors: throne rooms, hearths, observation decks. Buildings are currently hollow shells — this is the single biggest immersion gap.
2. **S5-02: Player Welcome & Orientation** (Oracle, L) — Title screen, pressure plate tour trigger, Crossroads info kiosk with written_book. Players currently spawn with zero guidance.
3. **S5-03: Discord Chat → Minecraft Signs** (Lucius, M) — Pinned messages become wall signs inside buildings. Implements the `UpdateBuilding` job type (currently a stub). First real Discord→Minecraft content sync beyond structure.
4. **S5-04: Village Ambient Life** (Batgirl, M) — Villager NPCs, animals, crop farms, flower gardens, ambient lighting. Villages feel empty and lifeless.
5. **S5-05: E2E Test Scenarios** (Nightwing, L) — Carry-forward #7. 5 real test scenarios using S4-07 infrastructure. Test infra exists but no scenarios are written.
6. **S5-06: BlueMap Full Setup** (Oracle, M) — Carry-forward #10. Complete JAR setup, port exposure, map rendering verification, village deep-links, README docs.
7. **S5-07: World Activity Feed in Discord** (Lucius, M) — `#world-activity` channel with color-coded embeds for builds, archival, player events. Makes the Discord community aware of world changes.
8. **S5-08: Dynamic Building Sizing** (Gordon, M) — Scale building footprint by channel member count (<10→Small 15×15, 10-30→Medium 21×21, 30+→Large 27×27). Adds visual variety and channel-appropriate scale.

**Why this theme:** After 4 sprints of infrastructure, topology, and mechanics, the biggest gap is *feel*. A player joining today spawns at Crossroads with no context, enters hollow buildings, walks through empty villages, and has no sense that their Discord community lives here. Sprint 5 directly addresses: (1) first-impression onboarding, (2) interior polish, (3) ambient world life, and (4) deeper Discord↔MC content synchronization. The carry-forwards (#7, #10) are refined to their remaining scope — test scenarios and BlueMap JAR wiring respectively.

**Capacity check:**
- Batgirl: 1L + 1M = ~5-6 days ✅
- Oracle: 1L + 1M = ~5-6 days ✅
- Lucius: 2M = ~3-4 days ✅
- Nightwing: 1L = ~3-4 days ✅
- Gordon: 1M + reviews = ~3-4 days ✅

**What was NOT included and why:**
- Account linking: Deferred per Jeff's explicit decision. Not ready yet.
- Multi-tenant / monitoring (#11): XL scope, not appropriate for a 2-week sprint.
- Voice→proximity chat: Requires significant Paper plugin work; better as Sprint 6 candidate after immersion is solid.
- Web admin dashboard: Nice-to-have, but players don't see it. Player experience first.
