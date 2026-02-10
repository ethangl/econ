# Debugging Playbook

## Purpose

Provide a repeatable, cross-system debugging workflow for EconSim.

Use this as the first stop when a bug appears in map generation, rendering, economy, UI, or simulation behavior.

For rendering/shader-specific deep dives, see:

- `docs/SHADER_OVERLAY_DEBUGGING.md`

---

## Debugging Mindset

1. Reproduce deterministically.
2. Isolate the failing stage.
3. Validate data before tuning constants.
4. Fix root cause, not symptoms.
5. Add a guardrail (log, assertion, test, doc note) after fix.

---

## Standard Workflow

### Step 1: Lock Repro

Capture:

- seed,
- template,
- map mode,
- simulation day/speed,
- camera location/zoom,
- exact expected vs actual behavior.

If issue is intermittent, capture multiple occurrences and shared conditions.

### Step 2: Classify by Layer

Assign primary failing layer:

- MapGen data generation,
- MapGen -> Core adapter/contract,
- simulation systems (economy/trade/roads),
- rendering/overlay/shader,
- UI binding/presentation.

### Step 3: Isolate with Toggles

Disable or bypass adjacent layers until the issue disappears:

- map mode overlays off,
- hover/selection off,
- economy updates paused,
- specific system step skipped (temporarily).

### Step 4: Validate Invariants

Check structural assumptions before visual tuning:

- IDs are valid and in range,
- lookups are populated (`ById`, `CellToCounty`, `CountyToMarket`),
- no NaN/invalid values in derived fields,
- map land/water classification is coherent.

### Step 5: Add Focused Instrumentation

Use domain logs (when available) or temporary concise logs:

- emit only key IDs/stats,
- avoid per-cell spam at normal levels,
- remove temporary noisy logs after root cause confirmed.

### Step 6: Fix + Verify in Full Stack

After isolated fix works:

- re-enable all layers,
- verify in at least 2-3 seeds,
- check adjacent map modes/systems for regressions.

---

## Symptom Triage Matrix

### 1) "Map looks wrong" (color/borders/hover/selection)

Start with:

- `docs/SHADER_OVERLAY_DEBUGGING.md`

Typical causes:

- bad texture channel encoding/decoding,
- palette mismatch,
- border/hover transform side effects,
- mode routing mismatch.

### 2) "Rivers missing / weird drainage"

Check:

- river thresholds (`RiverThreshold`, trace threshold),
- depression filling / flow accumulation assumptions,
- water classification consistency (`SeaLevel` and water flags).

### 3) "Economy is dead/stalled" (no production/trade)

Check:

- facility placement and biome/terrain requirements,
- county/market assignments,
- transport connectivity and movement costs,
- stockpile updates through production/consumption/trade order.

### 4) "Roads don’t appear or look wrong"

Check:

- `RoadState` has traffic events,
- road tier promotion logic,
- road mask generation and shader bindings.

### 5) "UI panel values wrong or stale"

Check:

- data source object lifecycle,
- event/subscription wiring timing,
- panel mode/selection routing.

---

## Domain-Specific Checklists

### MapGen

- same seed gives deterministic result,
- template script loaded as expected,
- stage outputs exist and have sane ranges.

### Adapter / Core data contract

- all lookups built (`BuildLookups`),
- required references non-null,
- IDs are contiguous/consistent where expected.

### Economy

- system tick order is correct,
- all required registries initialized,
- no silent early returns due to missing mappings.

### Rendering

- source textures generated and bound,
- map mode integer matches selected mode,
- interaction uniforms updated when hover/selection changes.

### UI

- panel reads from current selected entity,
- hidden/visible state follows mode rules,
- no stale references after map regeneration.

---

## Debug Logging Conventions

When adding logs:

- include domain + key IDs (`cellId`, `countyId`, `marketId`),
- prefer structured context over giant interpolated strings,
- use `Warn/Error` for true failures only,
- keep high-volume traces behind debug/trace level.

---

## Regression Checks After Fix

Minimum checks before closing:

1. Original repro fixed.
2. Same system works in at least 2 additional seeds.
3. Neighboring systems still work:
   - if render fix: test political/terrain/market modes,
   - if economy fix: verify production + trade + roads,
   - if mapgen fix: verify rivers/biomes/political still plausible.
4. No new obvious console errors.

---

## Bug Report Template

Use this for internal issue capture:

- Title:
- Seed/template:
- Repro steps:
- Expected:
- Actual:
- Layer classification:
- Logs/metrics:
- Screenshot(s):
- Root cause:
- Fix summary:
- Regression checks performed:

---

## Playbook Evolution

This document should evolve with architecture changes.

When major pipeline changes land (e.g., `MAP_TEXTURE_ARCHITECTURE` resolve/composite split), update:

- isolation workflow,
- triage matrix,
- system checklists,
- linked deep-dive docs.
