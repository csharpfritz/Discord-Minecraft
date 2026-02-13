# Decision: Building Interior Furnishing Pattern

**Date:** 2026-02-14
**Author:** Batgirl
**Status:** Implemented
**Scope:** S5-01, S5-04

## Decision

Building interiors are furnished per-style with distinct floor purposes. Interior methods are called LAST in each style's generation chain (after signs), not after GenerateFloorsAsync as originally considered, because we need wall blocks and lighting to already exist for furniture placement.

### Interior Layout Convention
- Ground floor: functional/social purpose (throne room, kitchen, planning room)
- 2nd floor: specialized purpose (armory, study, brewing lab)
- Channel topic sign on ground floor south interior wall when set

### Village Ambient Life Convention
- Villager NPCs use PersistenceRequired:1b NBT to prevent despawning
- Crop farms at ±50 blocks from center (inside fence at 75, outside building rows at ±20)
- Flower gardens at ±20 X, ±8 Z (between plaza and building rows)
- Walkway lanterns every 6 blocks along perimeter walkway outer edge

### Data Pipeline
- ChannelTopic flows as optional field through the entire pipeline
- Uses default parameter values to avoid breaking existing serialized jobs in Redis

## Why

Buildings were hollow shells and villages felt lifeless. These additions give each building character that reflects its architectural style, and make villages feel inhabited. The optional ChannelTopic pattern ensures backward compatibility with jobs already in the queue.

## Impact

- BuildingGenerator: ~250 new lines (3 interior methods + topic sign helper)
- VillageGenerator: ~166 new lines (5 ambient methods)
- Bridge.Data: Channel.Topic, DiscordChannelEvent.Topic, BuildingJobPayload.ChannelTopic
- BuildingGenerationRequest: ChannelTopic optional field
- DiscordEventConsumer + WorldGenJobProcessor: topic passthrough
