# Vertex-Blended Soil and Vegetation Notes

## Context

We are considering assigning soil and vegetation data at **vertices** and deriving per-cell values as a blend of surrounding vertex values.

Primary motivation is **aesthetic quality** (smoother transitions, fewer hard cell seams), not immediate gameplay complexity.

At current scale (roughly 2.5 km^2 per cell), mixed composition inside a cell is plausible.

---

## Why this makes sense

- Reduces abrupt visual boundaries from strict per-cell categorical assignment.
- Produces natural ecotone transitions (forest edge, wet-to-dry gradients, soil changes).
- Aligns with mesh interpolation and shading workflows.
- Provides a richer basis for future terrain detail synthesis.

---

## Data Model Recommendation

Represent soil/vegetation as **composition weights**, not single labels.

### Vertex data (continuous)

- Soil composition vector (example: 8 soil classes, normalized weights summing to 1).
- Vegetation composition vector (example: 8-16 veg classes, normalized weights summing to 1).
- Optional support fields: moisture, drainage, fertility, biomass.

### Cell data (derived)

- Blended composition from the cell's vertices.
- Optional collapsed outputs:
  - dominant soil class + dominance score,
  - dominant vegetation class + dominance score,
  - diversity/entropy metric.

---

## Blending Strategy

For each cell:

1. Gather its polygon vertices.
2. Compute weighted average of each composition channel.
3. Renormalize to ensure sum = 1.
4. Optionally keep top-k channels (k=2 or 3) for compact storage/rendering.

Default weighting options:

- Uniform vertex weighting (simplest).
- Distance-to-cell-center weighting (slightly smoother center behavior).
- Polygon-area contribution weighting (best geometric fidelity, more work).

Start with uniform weighting unless artifacts appear.

---

## Rendering Guidance

- Use blended composition directly for color/material mixing.
- Avoid hard thresholding in the primary visual path.
- Keep optional thresholded view modes for debug (dominant class map).

This gives smooth visuals while still supporting class-based debug and tuning.

---

## Gameplay Guardrails

If gameplay remains class-based:

- derive discrete class from blended composition with a stable threshold rule,
- store dominance score to avoid noisy flips near boundaries,
- optionally apply hysteresis if classes can update dynamically.

If gameplay is composition-based later:

- use weighted effects directly (e.g., yield modifiers from each component).

---

## Performance and Storage Notes

- Full composition vectors can be expensive; use top-k sparse encoding when possible.
- Keep "always-bound" render channels minimal.
- Domain-specific composition textures can be resolve-stage inputs instead of permanent composite pass dependencies.

Potential compact schemes:

- Top-2 IDs + weights for soil, top-2 IDs + weights for vegetation.
- Quantized weights in `UNorm8`/`UNorm16` based on visible artifact tolerance.

---

## Risks

- Category blur may be visually pleasing but harder to reason about in gameplay rules.
- Overly noisy source fields can produce shimmering compositions unless smoothed.
- Too many channels can increase memory and fetch cost if not resolved/cached carefully.

---

## Suggested First Experiment

1. Generate vertex soil/vegetation compositions deterministically from existing biome/moisture context.
2. Derive cell compositions by uniform averaging.
3. Render side-by-side:
   - old per-cell categorical map,
   - new blended map,
   - dominant-class debug map.
4. Evaluate:
   - seam reduction,
   - readability at zoom levels,
   - stability over map seeds.

---

## Soil + Vegetation Coverage Rendering

Layer vegetation visually on top of soil using density-driven coverage.

### Visual model

- **Base:** soil visual from blended soil composition.
- **Overlay:** vegetation visual from blended vegetation composition.
- **Coverage:** alpha driven by vegetation density (and optional modifiers).

Formula:

- `finalColor = lerp(soilColor, vegetationColor, coverage)`

Where:

- sparse vegetation -> low `coverage`, soil shows through,
- dense vegetation -> high `coverage`, vegetation becomes nearly opaque.

### Halftone/stipple approach

For stylized rendering, convert continuous coverage to a world-stable pattern:

1. Generate a pattern value `p` in world space (blue noise, Bayer-like tile, or hash noise).
2. Compare `coverage` against `p`.
3. Use anti-aliased thresholding (`smoothstep`) around the cutoff.

This yields a halftone-like transition where soil remains visible under low vegetation density.

### Recommended controls

- `vegCoverageBias` (global push toward more/less soil visibility)
- `vegCoverageContrast` (sharper vs softer transitions)
- `vegPatternScale` (pattern texel/world frequency)
- `vegPatternJitter` (break up visible regularity)
- `vegMaxOpacity` (cap, e.g. 0.9 to always retain some soil influence)

### Stability guardrails

1. Use **world-space** pattern coordinates, not screen-space, to avoid camera crawl.
2. Keep threshold AA width proportional to pixel derivatives to reduce shimmer.
3. Clamp effective coverage on steep slopes if detail noise causes flicker.
4. Keep density generation deterministic from map seed + config.

### Data fit with vertex blending

This approach is directly compatible with vertex-blended composition:

- interpolate vegetation density/composition smoothly,
- compute per-fragment coverage from interpolated density,
- blend against interpolated/blended soil base.

No discrete biome boundaries are required for the visual pass.

---

## Decision framing

For aesthetics at current scale, vertex-first blended composition is a sound direction.
Keep discrete class derivation as a compatibility layer for any systems that still require categorical inputs.

---

## Future Consideration: Weather-Driven Visualization

Biome visualization should later incorporate weather/state overlays in addition to static soil/vegetation composition.

Examples:

- seasonal dryness/wetness shifting vegetation saturation and coverage,
- snow/frost overlays at low temperature events,
- drought stress reducing apparent canopy density,
- recent rainfall temporarily darkening soils and increasing perceived biomass.

Design intent:

- treat weather as a **visual modulation layer** over base biome composition,
- keep base soil/vegetation data deterministic and stable,
- avoid coupling short-term weather visuals to permanent biome classification changes.
