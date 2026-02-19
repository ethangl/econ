# Startup Performance Notes

This document captures what is cached, what is deferred, and how startup cache invalidation works for map loading.

## Scope

- Focused on the startup path triggered by:
  - `Generate New Map`
  - `Load Last Map`
- Relevant code:
  - `unity/Assets/Scripts/Core/GameManager.cs`
  - `unity/Assets/Scripts/Renderer/MapView.cs`
  - `unity/Assets/Scripts/Renderer/MapOverlayManager.cs`
  - `src/EconSim.Core/Simulation/SimulationRunner.cs`
  - `src/EconSim.Core/Simulation/Systems/MarketSystem.cs`
  - `src/EconSim.Core/Simulation/Systems/OffMapSupplySystem.cs`

## Startup Paths

1. `Generate New Map`
- Runs full mapgen + conversion + simulation bootstrap.
- Writes/refreshes "last map" caches after generation.

2. `Load Last Map`
- Loads previously saved `MapData` payload.
- Reuses overlay texture/spatial caches when compatible.
- Reuses simulation bootstrap cache (markets/roads/zone costs) when compatible.
- Defers non-critical work to post-first-frame.

## Cache Locations

All startup caches live under:

- `unity/debug/last-map/`

Artifacts:

- `map_payload.json`
  - Serialized last `MapData` and generation seed settings.
- `textures/overlay_cache.json`
  - Overlay cache metadata and compatibility key.
- `textures/*.bin`
  - Raw texture/spatial data:
  - `spatial_grid.bin`
  - `political_ids.bin`
  - `geography_base.bin`
  - `river_mask.bin`
  - `heightmap.bin`
  - `relief_normal.bin`
  - `realm_border_dist.bin`
  - `province_border_dist.bin`
  - `county_border_dist.bin`
  - `market_border_dist.bin`
  - `road_dist.bin`
- `simulation_bootstrap.bin`
  - Markets, county->market map, static road state, and market zone costs.

## Deferred Work (Post-First-Frame)

Deferred on cached load to reduce time-to-interactive:

1. Province/county/market mode resolve prewarm
- Deferred until startup coroutine runs.
- Province and county resolve caches are warmed so first switch from political mode is hot.
- Market resolve cache is also warmed when market assignments are ready.
- Warmup is frame-sliced (one mode per frame) to avoid a single post-load hitch.
- If user enters a mode before warmup completes, on-demand generation still works.

2. Overlay cache flush
- Dynamic texture updates (market border / road mask) mark cache dirty.
- Disk write is deferred and flushed once during deferred startup work.

3. Market location marker creation
- No longer built during initial map init.
- Built lazily on first Market mode entry/visibility.

4. Cached-map invariant checks
- `AssertElevationInvariants` and `AssertWorldInvariants` run after first frame for `Load Last Map`.
- Keeps startup snappy while preserving validation in the same session.

## Invalidation Rules

### Last-map payload (`map_payload.json`)

- Must exist and deserialize correctly.
- `MapData` must be present.
- If load fails, `Load Last Map` returns false and startup falls back to generation flow.
- Note: payload `Version` exists in schema, but there is currently no strict version gate in load logic.

### Overlay texture cache (`textures/overlay_cache.json` + `.bin`)

Must pass all compatibility checks:

- Metadata version matches `OverlayTextureCacheVersion` (currently `2`).
- Dimensions/resolution match:
  - `GridWidth`, `GridHeight`, `BaseWidth`, `BaseHeight`, `ResolutionMultiplier`
- Seed checks (when both sides are > 0):
  - `RootSeed`
  - `MapGenSeed`

Dynamic texture reuse checks:

- `market_border_dist.bin` reused only when `CountyToMarketHash` matches current economy mapping.
- `road_dist.bin` reused only when `RoadStateHash` matches current road edge traffic + thresholds.
- On mismatch, texture is regenerated and cache is marked dirty for deferred flush.

### Simulation bootstrap cache (`simulation_bootstrap.bin`)

Must pass all compatibility checks:

- Binary version matches `BootstrapCacheVersion` (currently `9`).
- County count equals current map county count.
- Cell count equals current map cell count.
- Seed checks (when both sides are > 0):
  - root seed
  - mapgen seed
  - economy seed
- `SimulationConfig.Roads.BuildStaticNetworkAtInit` must match cached flag.
- Cache payload is V2-only (no economy-mode branch flag in the payload).

On mismatch:

- Markets/zones/static roads are rebuilt normally.
- New bootstrap cache is written.

## Operational Notes

- First run after cache format changes will rebuild affected caches once.
- Warm `Load Last Map` should be significantly faster than generation path.
- To force a clean rebuild, delete `unity/debug/last-map/`.
- Safe failure behavior: cache load failures degrade to regeneration, not runtime failure.
