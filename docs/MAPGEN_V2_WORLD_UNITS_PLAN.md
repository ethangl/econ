# MapGen V2 World-Units Plan

## Status

- Owner: MapGen
- Scope: New `MapGenV2` pipeline alongside existing `MapGen` pipeline
- Decision: Keep V1 intact for comparison, build V2 native in world units
- Constraint: Runtime maps are generated on demand and not persisted, so no legacy save compatibility is required

## Why We Are Doing This

Current map generation logic is internally coherent but tied to legacy elevation units (`0..100`, sea at `20`).
That creates hidden remaps, tuning fragility, and repeated unit translation pressure across climate, rivers, and biome logic.

The goal is to produce maps directly in explicit world units while preserving the qualitative "feel" of the current templates.
Exact per-cell parity with V1 is not required.

## Success Criteria

1. V2 elevation domain is signed meters relative to sea level (`0` at sea level, `>0` land, `<0` water).
2. No V2 stage depends on `HeightGrid.SeaLevel`, `HeightGrid.MinHeight`, or `HeightGrid.MaxHeight`.
3. V2 pipeline generates maps judged visually acceptable for all existing template families.
4. V1 and V2 can be run side-by-side from the same seed/config for comparison.
5. Runtime import path can consume V2 output without compatibility shims specific to V1 `0..100` elevation math.

## Non-Goals

1. Bit-identical replication of V1 map outputs.
2. Rewriting rendering/material systems in this phase.
3. Persisted map migration tooling (maps are runtime generated).

## Design Principles

1. Single canonical unit per quantity.
2. Unit conversion only at explicit boundaries, never as hidden internal remaps.
3. Keep DSL authoring shape familiar (same style and command vocabulary where sensible).
4. Determinism: same config + seed => same output.
5. Prefer explicit data contracts over implicit static constants.

## V2 Canonical Units

1. Elevation: signed meters relative to sea level.
2. Horizontal distance: kilometers (derived from mesh scale + `CellSizeKm`).
3. Latitude: degrees.
4. Temperature: Celsius.
5. Precipitation: millimeters per year.
6. Flow thresholds: explicit discharge/accumulation units documented at config boundary.

## Proposed V2 Types

- `MapGenV2Config`
  - Seed, CellCount, AspectRatio, CellSizeKm, LatitudeSouth
  - Elevation scale config: `MaxElevationMeters`, `MaxSeaDepthMeters`
  - River thresholds and stage tuning constants in named units

- `ElevationFieldV2`
  - `float[] ElevationMetersSigned`
  - `bool IsLand(i) => elevation > 0`
  - `float SeaLevelMeters = 0`
  - Helpers: slope/gradient using km-scale neighbors

- `ClimateFieldV2`, `RiverFieldV2`, `BiomeFieldV2`, `PoliticalFieldV2`
  - V2-native derivatives that never require V1 elevation constants

- `MapGenV2Result`
  - Mirrors current `MapGenResult` shape at high level but with V2-native fields
  - Includes explicit `WorldMetadata`

## World Metadata Contract (V2)

`WorldMetadata` remains explicit and required, with no legacy coupling:

1. `CellSizeKm`
2. `MapWidthKm`, `MapHeightKm`, `MapAreaKm2`
3. `LatitudeSouth`, `LatitudeNorth`
4. `MaxElevationMeters`, `MaxSeaDepthMeters`
5. `SeaLevelMeters` (new explicit field in V2-facing contracts, value `0`)

Note: V1 legacy height-domain fields can remain in V1 only; V2 contracts should not rely on them.

## DSL V2 Strategy

### Goals

1. Keep template authoring recognizable for current users.
2. Remove ambiguous percent-based elevation semantics.
3. Ensure every magnitude-bearing argument has documented units.

### Command Surface (initial)

1. `hill x y height_m`
2. `pit x y depth_m`
3. `range x1 y1 x2 y2 height_m`
4. `trough x1 y1 x2 y2 depth_m`
5. `mask fraction`
6. `add delta_m [land|water|all]`
7. `multiply factor [land|water|all]`
8. `smooth passes`
9. `strait width_cells direction`
10. `invert axis`

### Coordinate Semantics

1. `x/y` remain normalized map coordinates (`0..1`) for authoring convenience.
2. Width-like values are either in cells (`*_cells`) or km (`*_km`) with explicit suffix.
3. Elevation-changing values use `_m` suffix in grammar and docs.

### Range/Selector Semantics

1. `land` means elevation `> 0`.
2. `water` means elevation `<= 0`.
3. `all` means all cells.

### Parser and Validation Rules

1. Fail fast on unknown commands.
2. Fail fast on missing unit-suffixed numeric args where required.
3. Clamp only at explicit global bounds (`-MaxSeaDepthMeters..MaxElevationMeters`).
4. Seed behavior must be deterministic and versioned (`DslVersion = 2`).

## V2 Pipeline Architecture

1. Mesh generation (same approach as V1).
2. Elevation generation (DSL V2 + ops V2, native meters).
3. Climate generation (temperature/precipitation from V2 elevation directly).
4. River generation (flow and tracing from V2 gradients).
5. Biome/suitability/population/geography using V2 units.
6. Political generation using V2 geography outputs.

V1 remains untouched and callable for baseline comparisons.

## Execution Plan

### Phase A: Scaffolding

1. Add `src/MapGen/V2/` namespace and core result/config/types.
2. Add `MapGenPipelineV2.Generate(MapGenV2Config)` entrypoint.
3. Wire deterministic seed partitioning for all V2 subsystems.
4. Add smoke test proving V2 pipeline executes end-to-end on a small cell count.

Done when:

- V2 pipeline runs without referencing V1 `HeightGrid` constants.

### Phase B: Elevation + DSL

1. Implement `ElevationFieldV2` with signed-meter bounds.
2. Port core terrain ops into `HeightmapOpsV2` with explicit meter semantics.
3. Implement `HeightmapDslV2` parser/executor.
4. Create V2 template set corresponding to current template names.
5. Add golden distribution tests (land ratio, elevation quantiles, coastline roughness ranges).

Done when:

- All templates generate plausible terrain in V2 and pass metric gates.

### Phase C: Climate + Rivers

1. Port temperature calculation to consume signed meters directly.
2. Port precipitation sweeps/orographic effects with explicit km+m assumptions.
3. Port flow accumulation and river tracing to V2 elevation field.
4. Add regression thresholds for river count, total river length, and drainage coverage.

Done when:

- River systems form plausible basins across representative templates/seeds.

### Phase D: Biomes + Population + Political

1. Port biome classification thresholds to meter-native logic.
2. Port suitability/population using V2 climate/elevation inputs.
3. Port political stages and landmass detection to V2 land/water semantics.
4. Add consistency tests for biome coverage and polity generation sanity.

Done when:

- V2 produces viable biome and polity distributions across baseline seeds.

### Phase E: Side-by-Side Tooling + Integration

1. Add comparison runner that executes V1 and V2 for same config/seed set.
2. Emit a compact report:
   - Land/water ratio
   - Elevation percentiles
   - River metrics
   - Biome coverage
   - Realm count distribution
3. Update runtime wiring to select V1 or V2 generator via config flag.
4. Default remains V1 until V2 acceptance gates are green.

Done when:

- V2 can be enabled intentionally and evaluated without touching V1 behavior.

## Acceptance Gates

Per template on baseline seed suite:

1. `landRatio` within target band (template-specific).
2. Elevation `p10/p50/p90` in expected meter bands.
3. Coastline complexity within tolerance band.
4. Riverized land fraction within tolerance band.
5. Biome coverage has no impossible collapse (single-biome domination unless template is explicitly desert/oceanic).
6. Political output has non-degenerate structure (landmasses > 0, capitals > 0 where land exists).

These are quality gates, not parity gates.

## Risks and Mitigations

1. Risk: V2 templates initially produce wrong landmass ratios.
   - Mitigation: add template-local calibration constants and distribution tests early (Phase B).
2. Risk: Rivers regress due to subtle slope/flow differences.
   - Mitigation: isolate flow thresholds in config; add dedicated river metrics before biome/political tuning.
3. Risk: Scope creep from trying to perfect parity.
   - Mitigation: enforce acceptance bands and visual plausibility criteria; avoid exact-match goals.
4. Risk: Hidden V1 constants leak into V2.
   - Mitigation: grep-based guard test that fails on `HeightGrid.SeaLevel/MinHeight/MaxHeight` usage inside `src/MapGen/V2`.

## Definition of Phase 1 for V2 Program

Phase 1 is complete when:

1. V2 stack exists end-to-end and deterministic.
2. V2 uses signed meters as canonical elevation in every stage.
3. V1 remains available for comparison.
4. Side-by-side metrics report is available for baseline seed/template matrix.

## Immediate Work Queue (single unit)

1. Create V2 scaffolding types and pipeline entrypoint.
2. Implement V2 elevation field + DSL + template port.
3. Add template metric gates and comparison harness.

After that single unit lands, proceed with climate/rivers port as the next full unit.
