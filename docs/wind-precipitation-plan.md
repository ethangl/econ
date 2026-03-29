# Wind & Precipitation on the Sphere

## Data Model

New per-cell arrays on `TectonicData` (following existing pattern — all coarse per-cell data lives there):

- `Vec3[] CellWind` — tangent-to-sphere wind vectors (direction wind blows toward)
- `float[] CellWindSpeed` — magnitudes for quick access
- `float[] CellPrecipitation` — normalized 0-1
- `float[] CellHumidity` — remaining moisture after precipitation

New config params on `WorldGenConfig`:

| Parameter               | Default | Description                                                                                 |
| ----------------------- | ------- | ------------------------------------------------------------------------------------------- |
| `WindNoiseAmplitude`    | 0.35    | Curl noise strength relative to base zonal wind (0-1)                                       |
| `WindNoiseFrequency`    | 4.0     | Curl noise frequency on sphere (higher = more eddies)                                       |
| `WindNoiseOctaves`      | 3       | Curl noise octave count                                                                     |
| `OceanBaseHumidity`     | 0.9     | Starting humidity over ocean cells                                                          |
| `BasePrecipitationRate` | 0.03    | Fraction of humidity deposited per land cell                                                |
| `OrographicScale`       | 0.25    | How strongly upslope wind dumps moisture                                                    |
| `EquatorTempC`          | 29.0    | Equatorial baseline temperature (°C)                                                        |
| `PoleTempC`             | -20.0   | Polar baseline temperature (°C, average of north/south)                                     |
| `LapseRateCPerKm`       | 6.5     | Temperature decrease per km of elevation                                                    |
| `PermafrostThresholdC`  | -5.0    | Below this temperature, humidity clamped to 0.1                                             |
| `MaxLandElevationKm`    | 10.0    | Physical height of the highest land (CellElevation=1.0), for lapse rate and orographic math |

## Pipeline Position

After isostatic adjustment (last elevation modifier), before dense terrain generation. Wind and precipitation depend on finalized coarse elevation.

## Wind (WindOps.cs)

### Base Zonal Circulation

Latitude-driven wind belts following Earth-like atmospheric cells:

| Latitude Band | Wind Pattern                                          | Compass (NH / SH) |
| ------------- | ----------------------------------------------------- | ----------------- |
| 0-30°         | Trade winds (toward equator, Coriolis-deflected)      | SW / NW           |
| 30-60°        | Westerlies (toward pole, Coriolis-deflected)          | NE / SE           |
| 60-90°        | Polar easterlies (toward equator, Coriolis-deflected) | SW / NW           |

Smooth 5° transition zones between bands (lerp adjacent directions). Produces a tangent Vec3 per cell using local east/north basis vectors — same math already in `SiteSelector`.

### Curl Noise Perturbation

Evaluate 3D Perlin noise at each cell center's (x, y, z) position on the sphere (no projection artifacts, no seam issues). Compute the gradient via finite differences, then:

```
curlWind = cross(surfaceNormal, gradientOfNoise)
```

This is tangent to the sphere (cross product with the normal). Mathematically, `n × ∇φ` equals `n × ∇_s φ` (the cross product kills the normal component of the 3D gradient), and the surface divergence of `n × ∇_s φ` is zero — the 2D analogue of how a stream function produces a divergence-free velocity field. On a discrete irregular mesh this identity holds only approximately, so we should validate empirically that no significant convergence/divergence artifacts appear.

Scaled by `WindNoiseAmplitude`.

### Combined Wind

```
CellWind[c] = projectOntoTangentPlane(zonalBase + curlNoise * amplitude)
```

Magnitude normalized to [0, 1] range.

## Precipitation (PrecipitationOps.cs)

### Why Iterative Relaxation

The flat-map `PrecipitationModelOps` sorts cells by dot product with a single global wind direction and sweeps. On the sphere, each cell has its own wind direction — no single valid sort order exists. Iterative relaxation handles this naturally.

### Elevation Conversion

`TectonicData.CellElevation` is normalized 0-1 with sea level at 0.5 (used consistently across the pipeline: `CratonOps`, `BasinOps`, `DenseTerrainOps`, `SiteSelector`). Convert to physical units for temperature and orographic math:

```
SeaLevel     = 0.5f   // same constant used by CratonOps, BasinOps, DenseTerrainOps
elevationKm  = (CellElevation - SeaLevel) / (1 - SeaLevel) * MaxLandElevationKm  // land (> SeaLevel)
elevationKm  = 0                                                                  // ocean (≤ SeaLevel)
```

`MaxLandElevationKm` (default 10.0) is the physical height of the highest possible land (`CellElevation = 1.0`). The `/ (1 - SeaLevel)` normalization ensures the full land range [0.5, 1.0] maps to [0, MaxLandElevationKm]. Pre-compute `elevationKm[]` and `tempC[]` arrays once before the humidity solve.

### Temperature Estimate

No full thermal model exists on the sphere mesh yet, so we estimate temperature from latitude as a proxy:

```
tempC = EquatorTempC - (EquatorTempC - PoleTempC) * abs(latitude) / 90
       - LapseRateCPerKm * elevationKm
```

Uses `EquatorTempC`, `PoleTempC`, `LapseRateCPerKm`, and `MaxLandElevationKm` from `WorldGenConfig` (see config table above). This feeds moisture capacity (below) and permafrost damping.

### Moisture Capacity

Following the flat-map model (`PrecipitationModelOps`), humidity is bounded by temperature-dependent moisture capacity:

```
moistureCapacity = clamp(2^(tempC / 10), 0.05, 4.0)
```

Hot equatorial air holds more moisture than cold polar air. All humidity values are clamped to `[0, moistureCapacity[c]]` at every step.

### Algorithm — Two-Phase

Precipitation is computed in two separate phases to avoid coupling the iteration count to the rainfall total. Critically, phase 1 includes orographic humidity loss so that mountains drain moisture from the windward side and create real rain shadows in the converged humidity field.

**Phase 1: Solve for steady-state humidity.** Double-buffered (prev/next arrays, swapped each pass), no precipitation accumulated. Iterates until convergence so moisture penetrates arbitrarily deep inland.

1. **Pre-compute per-cell orographic factor.** For each land cell `c`, compute the max uphill gradient among upwind neighbors using physical elevation (km):

   ```
   oroFactor[c] = max over upwind neighbors nb of:
       max(0, elevationKm[c] - elevationKm[nb]) / arcDistanceKm(c, nb)
   ```

   where `arcDistanceKm = acos(dot(normalize(center[c]), normalize(center[nb]))) × Radius`.
   This is constant across passes (elevation doesn't change), so compute it once.

2. **Pre-compute tangent-plane alignment directions.** For each edge (c, nb), project the chord `center[c] - center[nb]` onto the tangent plane at `nb`:

   ```
   n_nb  = normalize(center[nb])
   chord = center[c] - center[nb]
   tangentDir[nb→c] = normalize(chord - dot(chord, n_nb) × n_nb)
   ```

   This gives the great-circle departure direction from `nb` toward `c`, in the same tangent frame as `wind[nb]`. Pre-compute once per directed edge.

3. **Initialize:** Ocean cells at `min(OceanBaseHumidity, moistureCapacity[c])`; land cells at 0. Mark ocean cells as **fixed boundary conditions** — their humidity is not updated by the solver.

4. **Each pass (read `prev`, write `next`):**
   - **Ocean cells:** `next[c] = prev[c]` (unchanged — oceans are fixed boundary conditions, not participants in the relaxation). This prevents ocean humidity from collapsing toward zero when an ocean cell has no upwind contributors.
   - **Land cells:** Gather humidity from upwind neighbors as a **weighted average** (not sum):
     ```
     gathered = 0; totalWeight = 0
     for each neighbor nb:
         alignment = dot(tangentDir[nb→c], normalize(wind[nb]))
         if alignment > 0:
             w = alignment² × windSpeed[nb]
             gathered += prev[nb] × w
             totalWeight += w
     if totalWeight > 0:
         gathered /= totalWeight
     ```
     The weighted average ensures the result is independent of mesh valence — a cell with 7 upwind neighbors gets the same humidity as one with 3, all else equal. The tangent-plane projection ensures alignment is computed in the same geometric frame as the wind vector.
   - **Land cells (continued):** Apply both base and orographic humidity loss:
     ```
     baseLoss = BasePrecipitationRate × gathered
     oroLoss  = OrographicScale × oroFactor[c] × gathered
     next[c]  = clamp(gathered - baseLoss - oroLoss, 0, moistureCapacity[c])
     ```
     Mountains consume extra moisture during transport, draining the humidity field on the windward side.
   - **Permafrost damping:** If `tempC[c] < PermafrostThresholdC` and `next[c] > 0.1`, clamp `next[c]` to `0.1`. (The excess is condensation/snowfall — accounted for in phase 2's precipitation.)
   - Swap `prev`/`next` at end of pass.

5. **Convergence:** Stop when max humidity delta between passes < 0.001, or safety cap of 50 passes. Each pass is ~12k operations — even 50 is trivial.

**Phase 2: Derive precipitation from converged humidity.** Single pass over all land cells, no iteration. Reconstructs the same incoming humidity flow used in phase 1 so that precipitation is physically consistent with the moisture removed during transport.

1. For each land cell, re-gather incoming humidity from the converged field using the same weighted average as phase 1:
   ```
   gathered = weightedAvg over upwind neighbors nb of convergedHumidity[nb]
              (same alignment/speed weights as phase 1)
   ```
2. Compute precipitation as **all** moisture lost from `gathered` — including capacity excess and permafrost condensation:
   ```
   baseLoss       = BasePrecipitationRate × gathered
   oroLoss        = OrographicScale × oroFactor[c] × gathered
   remainder      = gathered - baseLoss - oroLoss
   capExcess      = max(0, remainder - moistureCapacity[c])
   afterCap       = remainder - capExcess
   permafrostLoss = (tempC[c] < PermafrostThresholdC) ? max(0, afterCap - 0.1) : 0
   precip[c]      = baseLoss + oroLoss + capExcess + permafrostLoss
   ```

   - `capExcess`: condensation when humid air enters a cell whose temperature-based moisture capacity is lower than the incoming humidity.
   - `permafrostLoss`: forced condensation/snowfall in frozen regions, matching the phase-1 clamp to 0.1.
   - Invariant: `convergedHumidity[c] = gathered - precip[c]` (exactly, no silent loss).
3. **Rain shadows** are real: phase 1's orographic loss drained humidity on the windward side of mountains, so lee-side cells have low converged humidity and their downwind neighbors gather little incoming moisture.
4. **Normalize:** Power-law compression (`pow(x, 0.225)`) to spread the distribution.

## Debug Visualization

Wind and precipitation debug images are drawn as overlays on the existing `.debug.png` (same pattern as hotspot/arc/craton/basin/seamount overlays in `Program.cs`). Additionally, two standalone equirectangular heatmaps are saved as separate PNGs for detailed inspection:

- `{name}.wind.png` — wind speed heatmap (blue-to-red) with direction arrows at each coarse cell center
- `{name}.precip.png` — precipitation heatmap (brown/yellow through green to blue)

Both use a coarse-mesh `SphereLookup` (separate from the dense-mesh one in `HeightmapRenderer`). Arrow projection: cell center to equirectangular pixel coords, wind tangent vector decomposed into local east/north, then to pixel dx/dy (negated dy for image-space y-down).

The `.debug.png` overlay versions are lightweight (dot + short arrow per cell for wind, colored dots for precip) so they compose with the existing overlays. The standalone PNGs are full-resolution nearest-cell fills for actual analysis.

## Implementation Order

Build and verify incrementally — validate wind visually before building precipitation on top.

1. **Data structures** — TectonicData arrays, WorldGenConfig params, timing fields
2. **WindOps** — implement and wire into pipeline
3. **Wind debug rendering** — visually verify circulation patterns
4. **PrecipitationOps** — implement and wire in
5. **Precipitation debug rendering** — verify moisture patterns

## Files

| File                                           | Action                                                                              |
| ---------------------------------------------- | ----------------------------------------------------------------------------------- |
| `src/WorldGen/TectonicData.cs`                 | Add CellWind, CellWindSpeed, CellPrecipitation, CellHumidity arrays                 |
| `src/WorldGen/WorldGenConfig.cs`               | Add wind/precip config params                                                       |
| `src/WorldGen/WorldGenResult.cs`               | Add WindSeconds, PrecipitationSeconds timing                                        |
| `src/WorldGen/WindOps.cs`                      | **New** — base zonal wind + curl noise                                              |
| `src/WorldGen/PrecipitationOps.cs`             | **New** — double-buffered iterative moisture transport                              |
| `src/WorldGen/WorldGenPipeline.cs`             | Insert wind + precip steps after isostasy                                           |
| `cli/WorldGen.Lib/AtmosphericDebugRenderer.cs` | **New** — standalone wind/precip heatmap PNGs + coarse SphereLookup                 |
| `cli/WorldGen.Cli/Program.cs`                  | Add wind/precip overlay to `.debug.png`, wire standalone heatmap output, add timing |

## Design Decisions

**Why TectonicData, not a new AtmosphericData?** Every per-coarse-cell array lives on TectonicData. It's really "coarse world data" despite the name. New class would require threading through WorldGenResult, pipeline, CLI, and all consumers. Can refactor later if needed.

**Why curl noise, not raw Perlin?** Raw Perlin added to wind vectors creates convergence/divergence zones (air piling up or disappearing). Curl noise is approximately divergence-free on the sphere surface (exactly so in the continuous limit, approximately on the discrete mesh) — primarily adds rotation.

**Why coarse mesh only (~2000 cells)?** Wind and precipitation are large-scale. Each coarse cell is ~250 km across — correct scale for global circulation. Computing on the 20k dense mesh wastes time on detail that doesn't exist at atmospheric scale. Values can be interpolated to dense mesh later if needed.

**Why double-buffered convergence iteration, not fixed passes?** Fixed pass count limits moisture penetration to N cell hops inland. Large continents can be 20+ hops across. Convergence-based iteration ensures moisture reaches deep interiors regardless of continent size, while the double buffer ensures pass results don't depend on traversal order.

**Why two-phase (solve humidity, then derive precipitation)?** If precipitation is accumulated during convergence iteration, the total rainfall depends on how many passes the solver runs — an implementation detail, not a climate property. Solving humidity to steady state first, then deriving precipitation in one pass, produces a result that depends only on the climate parameters.

**Why wind speed scales advection?** Wind speed varies across the globe (strong trade winds vs calm doldrums). Transport intensity should reflect this — calm zones advect less moisture, strong belts advect more. Both alignment and speed weight the neighbor humidity contribution.

**Why latitude-based temperature estimate?** The sphere mesh has no thermal model yet. Latitude + elevation lapse rate is the simplest proxy that captures the dominant gradient (equator-to-pole) and the main local modifier (altitude). This feeds moisture capacity and permafrost damping, matching the flat-map model's temperature dependence without requiring a full thermal simulation. Temperature parameters (`EquatorTempC`, `PoleTempC`, `LapseRateCPerKm`) live on `WorldGenConfig` directly — no dependency on `MapGenConfig`.

**Why weighted average, not weighted sum?** A raw weighted sum makes humidity dependent on mesh valence: cells with more upwind neighbors accumulate more moisture and saturate at the cap, creating artificial hotspots. A weighted average produces a value bounded by the upwind humidity values regardless of connectivity.

**Why tangent-plane projection for alignment?** `wind[nb]` is tangent to the sphere at `nb`, but the raw chord `center[c] - center[nb]` is not — it cuts through the sphere interior. Dotting a tangent vector with a non-tangent vector biases the result, especially for distant neighbors. Projecting the chord onto the tangent plane at `nb` gives the great-circle departure direction, which lives in the same geometric frame as the wind vector.

**Why re-gather in phase 2 instead of reading humidity[c]?** Phase 1 stores `next[c] = gathered - baseLoss - oroLoss` (the remainder). Phase 2 needs the loss itself (precipitation = baseLoss + oroLoss). Reading `humidity[c]` and applying loss rates would compute loss-of-remainder, not loss-of-incoming, systematically underestimating rainfall. Re-gathering from converged neighbors reconstructs the same incoming term so precipitation equals the actual moisture removed.

**Why MaxLandElevationKm?** `CellElevation` is normalized 0-1, but temperature lapse rate and orographic gradients need physical units (km). `MaxLandElevationKm` (default 10) provides the conversion factor. Sea level is 0.5 (matching `CratonOps`, `BasinOps`, `DenseTerrainOps`). This is a worldgen-level constant — the flat-map pipeline has its own elevation scale via the DSL envelope.

**Why are ocean cells fixed boundary conditions?** The solver iterates humidity to steady state. If ocean cells participate in the relaxation, an ocean cell with no upwind contributors would relax toward zero, starving nearby coasts. Fixing ocean humidity at `OceanBaseHumidity` makes them persistent moisture reservoirs, which is physically correct — the ocean surface always evaporates.
