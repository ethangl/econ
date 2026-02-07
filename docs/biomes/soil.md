# Stage 1: Soil

## Inputs

| Input            | Source                                           | What it drives                      |
| ---------------- | ------------------------------------------------ | ----------------------------------- |
| Temperature      | `ClimateData.Temperature`                        | Permafrost gate, decomposition rate |
| Precipitation    | `ClimateData.Precipitation`                      | Leaching, moisture, arid/wet split  |
| Elevation        | `HeightGrid.Heights`                             | Alpine gate, combined with slope    |
| Slope            | Derived: max height diff to neighbors / distance | Erosion vs. accumulation            |
| Drainage flux    | `RiverData.VertexFlux` averaged onto cells       | Alluvial detection                  |
| Rock type        | New Perlin noise layer                           | Base mineral content                |
| Salt proximity   | BFS from ocean cells                             | Salinization                        |
| Loess deposition | Wind vectors + upwind aridity                    | Fertility modifier                  |

## Derived Inputs

**Slope.** For each cell, find the maximum height gradient to any neighbor:

```
slope[c] = max over neighbors n of:
    abs(Heights[c] - Heights[n]) / distance(CellCenters[c], CellCenters[n])
```

Normalize to 0-1 range across the map. High slope = ridges and cliffs. Low slope = plains and valley floors.

**Salt proximity.** BFS from all ocean cells outward across land cells. Each step increments distance. Effect decays with distance and elevation above sea level:

```
saltEffect[c] = max(0, 1 - distToOcean[c] / maxSaltReach) × max(0, 1 - elevAboveSea / saltElevCutoff)
```

Where `maxSaltReach = 5` (cells from ocean) and `saltElevCutoff = 20` (height units above sea level). Coastal lowlands get strong salt effect; inland or elevated cells get none.

**Drainage flux.** RiverData stores flux per-vertex (units: accumulated upstream precipitation, 0-100 per contributing vertex). To get per-cell values, average the flux of the cell's surrounding Voronoi vertices (~3 per cell, available from `CellMesh.CellVertices`). A cell with no concentrated flow has cell flux ~50-100 (just local precip). A cell adjacent to a small river has ~100-200. A cell on a major river has ~500+. The alluvial threshold of 200 catches cells with actual concentrated drainage — river valleys and well-drained lowlands.

**Loess deposition.** Wind carries fine silt from bare, dry source areas and deposits it downwind. Uses the same wind-sweep infrastructure as `PrecipitationOps` — sort cells by projection onto wind vector, propagate upwind-to-downwind through the neighbor graph.

Per wind band:

```
loessCarry = 0 per cell (analogous to humidity)

for each cell in upwind-to-downwind order:
    // Gather carry from upwind neighbors (same alignment weighting as precip sweep)
    loessCarry[c] = weighted average of upwind neighbors' loessCarry

    // Source emission: bare arid/frozen ground produces silt
    if precip[c] < 15 OR temp[c] < -5:
        loessCarry[c] += loessSourceStrength (0.3)

    // Orographic blocking: rising terrain traps silt on windward side
    if cell is uphill from upwind neighbor:
        deposit = loessCarry[c] × loessOrographicCapture (0.5) × slopeFactor
        loessAccum[c] += deposit
        loessCarry[c] -= deposit

    // Distance decay: silt settles out over distance
    deposit = loessCarry[c] × loessDepositRate (0.1)
    loessAccum[c] += deposit
    loessCarry[c] -= deposit
```

After all bands (weighted by overlap fraction, same as precip), normalize:

```
loess[c] = clamp(loessAccum[c] / maxLoessAccum, 0, 1)
```

This produces thick loess belts downwind of deserts and glacial margins, blocked by mountain ranges — matching real-world patterns (Chinese Loess Plateau, US Great Plains, Ukrainian steppe).

## Rock Type Layer

A single Perlin noise field, thresholded into 4 categories:

| Rock Type   | Noise Range | Fertility Modifier | Notes                          |
| ----------- | ----------- | ------------------ | ------------------------------ |
| Volcanic    | > 0.75      | ×1.4               | Mineral-rich basalt weathering |
| Limestone   | 0.50 – 0.75 | ×1.1               | Good calcium, alkaline         |
| Sedimentary | 0.25 – 0.50 | ×1.0               | Neutral baseline               |
| Granite     | < 0.25      | ×0.7               | Poor, acidic weathering        |

Noise frequency should be low (large regions of uniform geology). Seed independently from heightmap.

Optional elevation bias: volcanic rock more likely at high elevation (tectonic activity), sedimentary more likely in lowlands (deposition). Could blend noise with a height-based weight.

## Soil Types

8 types, assigned via priority cascade. First match wins.

| Priority | Soil Type      | Gate Condition                         | Base Fertility |
| -------- | -------------- | -------------------------------------- | -------------- |
| 1        | **Permafrost** | temp < -5°C                            | 0.05           |
| 2        | **Saline**     | saltEffect > 0.3                       | 0.10           |
| 3        | **Lithosol**   | slope > 0.6 OR elev > 80               | 0.10           |
| 4        | **Alluvial**   | cellFlux > 200 AND slope < 0.15        | 0.90           |
| 5        | **Aridisol**   | precip < 15                            | 0.15           |
| 6        | **Laterite**   | temp > 20°C AND precip > 60            | 0.45           |
| 7        | **Podzol**     | temp < 20°C AND precip > 30            | 0.35           |
| 8        | **Chernozem**  | everything else (moderate temp/precip) | 0.70           |

Chernozem is the default/fallback — temperate grassland with decent organic matter. The cascade ensures extreme conditions (frozen, salty, steep, flooded, dry, tropical, boreal) are caught first. Note: the Lithosol elevation gate (80) is slightly below the Alpine Barren biome split (85 in [biomes.md](biomes.md)), so cells at elev 80-85 become Lithosol → Mountain Shrub rather than skipping to a lower-priority soil.

## Fertility Calculation

```
fertility = baseFertility[soilType]
          × rockTypeModifier
          × (1 + loess[c])
          × drainageModifier
```

Where `drainageModifier` penalizes moisture extremes for non-alluvial soils:

```
if soilType == Alluvial:
    drainageModifier = 1.0      // base fertility already accounts for water

else:
    precipNorm = precip / 100   // 0-1

    dryPenalty  = max(0, 0.3 - precipNorm) / 0.3 × 0.3    // up to -30% below precip 30
    wetPenalty  = max(0, precipNorm - 0.7) / 0.3 × 0.2     // up to -20% above precip 70

    drainageModifier = 1.0 - dryPenalty - wetPenalty
```

At precip 0 → 0.7 (poor nutrient cycling, no weathering). At precip 30-70 → 1.0 (optimal). At precip 100 → 0.8 (slight waterlogging). In practice this mostly affects chernozem at the margins — laterite (precip > 60) and podzol (precip > 30) already require wet conditions, so they never hit the dry penalty.

Final fertility is clamped to [0, 1].

## Soil Properties

| Soil Type  | Fertility | Real-World Analogue        |
| ---------- | --------- | -------------------------- |
| Permafrost | 0.05      | Siberian tundra, Arctic    |
| Saline     | 0.10      | Salt flats, coastal marsh  |
| Lithosol   | 0.10      | Alpine scree, cliff faces  |
| Alluvial   | 0.90      | Nile delta, Ganges plain   |
| Aridisol   | 0.15      | Sahara margin, Gobi        |
| Laterite   | 0.45      | Amazon basin, Congo        |
| Podzol     | 0.35      | Boreal Canada, Scandinavia |
| Chernozem  | 0.70      | Ukraine, US Midwest        |
