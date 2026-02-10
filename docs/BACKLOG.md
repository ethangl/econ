# Backlog

## Operating Model

This backlog uses:

- **Milestones** for major tracks,
- **Vertical slices** (code + tests + diagnostics + docs together),
- **Risk-aware ordering** (high-impact prerequisites first),
- **Migration tags** (`pre-elevation`, `post-elevation`, `pre-texture-arch`, `post-texture-arch`).

Fields per item:

- `Type`: fix / refactor / optimization / feature / process / doc
- `Impact`: H / M / L
- `Effort`: S / M / L
- `Risk`: H / M / L
- `Depends on`
- `Done when`

---

## Milestone M1 - Stabilize and Instrument (Now)

Goal: reduce debugging pain and create safety rails before deep migrations.

### M1-S1 Domain logging foundation

- **Type:** feature/process
- **Impact:** H
- **Effort:** M
- **Risk:** L
- **Tags:** `pre-elevation`, `pre-texture-arch`
- **Depends on:** none
- **Done when:**
  - `DOMAIN_LOGGING_ARCHITECTURE` Phase 1 implemented,
  - runtime domain + severity filter works,
  - Unity Inspector control panel can toggle domains.

### M1-S2 Fixed-seed regression harness

- **Type:** process/test
- **Impact:** H
- **Effort:** S-M
- **Risk:** L
- **Tags:** `pre-elevation`, `pre-texture-arch`
- **Depends on:** none
- **Done when:**
  - at least 3 fixed seeds x 2 templates are tested,
  - mapgen invariants checked (land ratio, river count sanity, non-null lookups),
  - CI/local runner can execute tests quickly.

### M1-S3 Rendering debug tooling

- **Type:** feature
- **Impact:** H
- **Effort:** M
- **Risk:** L
- **Tags:** `pre-texture-arch`
- **Depends on:** M1-S1 preferred
- **Done when:**
  - channel inspector mode exists,
  - ID probe shows decoded values under cursor,
  - shader debugging workflow from `SHADER_OVERLAY_DEBUGGING` is executable without code edits.

### M1-S4 Quick correctness fixes

- **Type:** fix
- **Impact:** H
- **Effort:** S
- **Risk:** L
- **Tags:** `pre-elevation`, `pre-texture-arch`
- **Depends on:** none
- **Done when:**
  - MarketPlacer county lookup bug fixed,
  - Economy initializer seeded from map seed,
  - transport/path caches have explicit invalidation strategy when roads change.

---

## Milestone M2 - Elevation Dual-Domain Migration

Goal: keep DSL morphology stable while moving simulation/render to `0-255`.

Reference: `ELEVATION_255_MIGRATION_CHECKLIST.md`

### M2-S1 Domain abstraction and boundary

- **Type:** refactor
- **Impact:** H
- **Effort:** M
- **Risk:** M
- **Tags:** `elevation-core`
- **Depends on:** M1-S2
- **Done when:**
  - DSL domain (`0-100`) and simulation domain (`0-255`) are explicit,
  - rescale boundary exists in `MapGenPipeline` right after DSL.

### M2-S2 Render + adapter normalization

- **Type:** refactor/fix
- **Impact:** H
- **Effort:** M
- **Risk:** M
- **Tags:** `during-elevation`
- **Depends on:** M2-S1
- **Done when:**
  - no hardcoded `/100` or `20` assumptions remain in render bridge path,
  - shader sea level comes from normalized simulation constants.

### M2-S3 Threshold retuning pass

- **Type:** feature/tuning
- **Impact:** H
- **Effort:** M-L
- **Risk:** M
- **Tags:** `during-elevation`
- **Depends on:** M2-S2
- **Done when:**
  - biome/transport/resource thresholds expressed in normalized land-space,
  - seed regression suite passes with acceptable deltas.

---

## Milestone M3 - Texture Architecture Foundation

Goal: implement preconditions for scalable map modes.

Reference: `MAP_TEXTURE_ARCHITECTURE.md`

### M3-S1 Geography channel split

- **Type:** refactor/correctness
- **Impact:** H
- **Effort:** M
- **Risk:** M
- **Tags:** `post-elevation`, `pre-texture-arch`
- **Depends on:** M2 complete or stable
- **Done when:**
  - packed B-channel removed from primary flow,
  - explicit geography channels used (biome/soil/water semantics).

### M3-S2 Shader modularization

- **Type:** refactor
- **Impact:** M-H
- **Effort:** M-L
- **Risk:** M
- **Tags:** `pre-texture-arch`
- **Depends on:** M3-S1 preferred
- **Done when:**
  - `MapOverlay.shader` logic split into maintainable include units,
  - no behavior regression in political/terrain/market modes.

### M3-S3 Resolve pipeline prototype

- **Type:** feature/architecture
- **Impact:** H
- **Effort:** L
- **Risk:** H
- **Tags:** `post-texture-arch`
- **Depends on:** M3-S1, M3-S2, M1-S3
- **Done when:**
  - one mode resolves to display texture successfully,
  - composite pass remains stable with hover/selection/water layering.

---

## Milestone M4 - New Visual and Gameplay Modes

Goal: safely expand capabilities after architecture foundations are in place.

### M4-S1 Vertex-blended biome visuals

- **Type:** feature
- **Impact:** M-H
- **Effort:** M-L
- **Risk:** M
- **Tags:** `post-texture-arch`
- **Depends on:** M3-S1, M3-S3 optional
- **Done when:**
  - vertex composition pipeline implemented,
  - soil + vegetation coverage rendering works with stable patterning.

### M4-S2 Render detail height + normal map

- **Type:** feature
- **Impact:** M-H
- **Effort:** M
- **Risk:** M
- **Tags:** `post-elevation`, `post-texture-arch`
- **Depends on:** M2 complete
- **Done when:**
  - render-only heightmap and normal map generated deterministically,
  - no gameplay system consumes render height.

### M4-S3 Weather-driven visual modulation

- **Type:** feature
- **Impact:** M
- **Effort:** M
- **Risk:** M
- **Tags:** `post-texture-arch`
- **Depends on:** M4-S1
- **Done when:**
  - weather modifies visual biome presentation without mutating base biome classification.

---

## Priority Queue (Next 6)

1. M1-S2 Fixed-seed regression harness  
2. M1-S1 Domain logging foundation  
3. M1-S4 Quick correctness fixes  
4. M1-S3 Rendering debug tooling  
5. M2-S1 Domain abstraction and boundary  
6. M2-S2 Render + adapter normalization

---

## WIP Policy

- Max 2 in-progress slices at once.
- Do not start `M3` until `M2-S1` and `M2-S2` are complete.
- Every completed slice must update relevant docs and include at least one guardrail (test, assertion, or debug tool).

---

## Decision Log Pointer

Record major architectural choices as lightweight ADR notes under:

- `docs/adr/ADR-xxxx-<title>.md`

Use ADRs for decisions like:

- dual-domain elevation boundary,
- resolve pipeline adoption,
- vertex-composition data model choices.
