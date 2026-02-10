# Elevation Migration Checklist (Dual-Domain Approach)

## Objective

Adopt `0-255` as the simulation/render elevation domain **without destabilizing** the current DSL/BFS terrain morphology.

Key decision:

- Keep DSL terrain shaping in a **legacy generation domain** (`0-100`).
- Rescale once after DSL.
- Run all downstream systems in a **simulation domain** (`0-255`).

This preserves existing blob behavior while unlocking a natural byte-range domain for the rest of the engine.

Status snapshot (February 10, 2026):

- Phases 1-6 code migration is implemented.
- Visual smoke-checks show no sea/land inversion artifacts.
- Regression/signoff metrics are still pending (Phase 7).

---

## Canonical Domains

Do not use one global elevation constant set during migration. Use **stage-scoped canonicals**:

### 1) DSL Domain (generation shaping only)

- `DslMin = 0`
- `DslMax = 100`
- `DslSea = 20`
- `DslLandRange = 80`

### 2) Simulation Domain (post-rescale, authoritative runtime)

- `SimMin = 0`
- `SimMax = 255`
- `SimSea = 51` (preserves 20% sea ratio)
- `SimLandRange = 204`

### Rescale function

- `hSim = round(hDsl * SimMax / DslMax)`
- clamp to `[SimMin, SimMax]`

For sea equivalence, always derive from ratio (`20%`) rather than duplicated literals.

---

## Pipeline Placement

Rescale happens in `src/MapGen/MapGenPipeline.cs`:

1. Run `HeightmapDSL.Execute(...)` in DSL domain.
2. **Rescale heights `0-100 -> 0-255`.**
3. Run Climate/Rivers/Biomes/Population/Political in simulation domain.

Do **not** put this rescale in `MapGenAdapter`; that is too late for mapgen algorithms.

---

## Execution Rules

1. Keep project compiling after each phase.
2. Never mix DSL and simulation constants in the same algorithm.
3. Prefer normalized expressions (`(h - Sea)/LandRange`) for threshold semantics.
4. Validate with fixed-seed regression suite at every phase.

---

## Phase 0 - Baseline Snapshot

- [ ] Choose 3 fixed seeds x 2 templates.
- [ ] Record:
  - land ratio,
  - biome counts (high-elevation classes in particular),
  - river count + major length distribution,
  - ore/deposit counts,
  - transport sample costs.
- [ ] Save baseline screenshots for terrain/soil/political/market.

Regression harness bootstrap:

- EditMode regression tests live in `unity/Assets/Tests/EditMode/MapGenRegressionTests.cs`.
- Baseline bands live in `unity/Assets/Tests/EditMode/MapGenRegressionBaselines.json`.
- Run via Unity Test Runner (EditMode) before and after each migration phase.

---

## Phase 1 - Introduce Domain Types and Constants

Goal: remove ambiguity.

- [x] Add an elevation domain abstraction (struct/class) with min/max/sea helpers.
- [x] Define `DslDomain` and `SimDomain` in one canonical place.
- [x] Ensure DSL code paths consume `DslDomain` only.
- [x] Ensure downstream systems are prepared to consume `SimDomain`.

Suggested symbols:

- `NormalizeHeight(h, domain)`
- `NormalizeLandHeight(h, domain)`
- `FromNormalizedLand(t, domain)`

Validation:

- [x] Build passes.
- [ ] No behavior change yet. (later phases intentionally changed behavior)

---

## Phase 2 - Add Rescale Step in MapGenPipeline

Goal: transition boundary between domains.

- [x] In `src/MapGen/MapGenPipeline.cs`, after `HeightmapDSL.Execute(...)`, rescale `heights` to simulation domain.
- [x] Ensure subsequent calls (Climate/Rivers/Biome/etc.) run on rescaled heights.

Validation:

- [x] Land/water split remains plausible.
- [x] No obvious mapgen breakage.

---

## Phase 3 - Normalize Render + Bridge Consumers

Goal: all rendering/bridge logic reads simulation-domain values correctly.

- [x] `unity/Assets/Scripts/Renderer/MapOverlayManager.cs`
  - replace `/100f` style normalization with `/SimMax`.
  - set `_SeaLevel = SimSea / SimMax`.
- [x] `unity/Assets/Scripts/Renderer/MapView.cs`
  - replace `80f`/`20f` assumptions with dynamic `(Sea, LandRange)`.
- [x] `unity/Assets/Shaders/MapOverlay.shader`
  - keep formulas normalized; `_SeaLevel` should come from code.
- [x] `src/EconSim.Core/Import/MapGenAdapter.cs`
  - remove hard-coded sea constants; use simulation-domain sea value.

Validation:

- [x] Visual parity in normalized terms.
- [x] No ocean/land inversion artifacts.

---

## Phase 4 - Retune Threshold-Heavy Systems (Simulation Domain)

Goal: convert hard-coded absolute thresholds into normalized-land semantics.

### 4A Biomes/soils

- [x] `src/MapGen/BiomeOps.cs`
  - replace raw height literals (`80`, `85`, etc.) with normalized-land thresholds.

### 4B Transport

- [x] `src/EconSim.Core/Transport/TransportGraph.cs`
  - replace fixed bands (`70`, `30`) with domain-derived bands.

### 4C Resource placement

- [x] `src/EconSim.Core/Economy/EconomyInitializer.cs`
  - convert ore/deposit cutoffs (`40/45/50`) to normalized/domain-derived values.

Validation:

- [x] Biome distribution near baseline intent. *(owner-accepted; no dedicated validation harness available)*
- [x] Ore distribution still reasonable. *(owner-accepted; no dedicated validation harness available)*
- [x] Mountain travel penalty behavior preserved.

---

## Phase 5 - DSL Domain Isolation (No Native 255 Blob Retune Yet)

Goal: explicitly keep BFS blob logic in legacy space for morphology stability.

- [x] `src/MapGen/HeightmapOps.cs`
  - mark DSL-domain assumptions clearly (`0-100`, `sea=20`).
  - avoid accidental switch to simulation constants.
- [x] `src/MapGen/HeightmapDSL.cs`
  - keep parsing/template semantics in DSL domain.
- [x] `src/MapGen/HeightmapTemplates.cs`
  - keep current values as DSL-domain values.

Validation:

- [ ] Blob/range/trough morphology stays consistent with baseline.

---

## Phase 6 - Secondary Consumer Cleanup

- [x] Audit remaining elevation literals in:
  - `src/MapGen/WorldConfig.cs`
  - `src/MapGen/PrecipitationOps.cs`
  - `src/MapGen/SuitabilityOps.cs`
  - any additional matches from search.
- [x] Replace hard assumptions with domain helpers.

Validation:

- [ ] No core systems use ambiguous or mixed-domain constants.

---

## Phase 7 - Regression and Signoff

- [ ] Re-run baseline suite and compare:
  - land ratio,
  - biome counts,
  - river stats,
  - ore stats,
  - transport samples.
- [ ] Review visuals for terrain/soil/political/market modes.
- [ ] Document accepted intentional deviations.

Done criteria:

- [x] Dual-domain boundary is explicit and stable.
- [x] Simulation/render systems fully use `0-255`.
- [ ] DSL blob morphology preserved from legacy behavior.

---

## Optional Future Phase - Native 255 DSL

Only after the dual-domain migration is stable:

- [ ] Move BFS blob ops (`Hill`, `Pit`, `Range`, `Trough`, `Strait`) to native `0-255`.
- [ ] Retune nonlinear decay/termination constants (`-1`, `<2`, caps, etc.).
- [ ] Rebuild templates in normalized semantics.

This phase is intentionally deferred because it is the highest morphology risk.

---

## Search Queries

Use these during migration:

- `SeaLevel`
- `MaxHeight`
- `100f`
- `/ 100f`
- `20f`
- `> 80`
- `> 85`
- `> 70`

---

## Save Compatibility

For old maps persisted in `0-100`:

- load with version check,
- rescale to simulation domain on load,
- keep in-memory authoritative representation in `0-255`.
