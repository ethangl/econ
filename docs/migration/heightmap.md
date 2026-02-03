# Heightmap Generation

## Azgaar's Two-Cell System

Azgaar uses two Voronoi tessellations at different resolutions:

### Grid Cells (coarse)

- **Purpose:** Heightmap generation, climate modeling
- **Generation:** Jittered square grid → Voronoi
- **Count:** User-configurable (~10k typical)
- **Used for:** Heights, biomes, precipitation, temperature

### Pack Cells (fine)

- **Purpose:** Detailed features (rivers, burgs, states)
- **Generation:** Derived from grid via `reGraph()`:
  1. Include all land cells from grid
  2. Include coastal cells (shoreline)
  3. Add midpoints between coastal cells (more detail at coasts)
  4. Run Voronoi on augmented points
- **Mapping:** `pack.cells.g[i]` → source grid cell index
- **Used for:** Rivers, settlements, political boundaries

### Why Two Levels?

- Heightmap operations (blob, range, smooth) are expensive → keep grid coarse
- Detail matters at coasts where players spend most attention
- Rivers need finer resolution to look good
- Political boundaries need detail for interesting borders

### Our Approach

We currently use a single cell level (Azgaar's pack cells imported as our cells). Options:

1. **Keep single level:** Simpler, matches our current code
2. **Add grid level:** If we need to do heightmap DSL operations

Likely path: Start single-level, add grid if heightmap generation needs it.

## Heightmap Templates

Azgaar has an elegant system: templates define high-level constraints, and the generator produces maps that satisfy them. Examples:

- **Continents** — large landmasses with inland seas
- **Archipelago** — scattered islands, lots of coastline
- **Pangaea** — single supercontinent
- **Low Island** — small landmass, minimal elevation
- **Highland** — mountainous, dramatic terrain
- **Atoll** — ring islands around central lagoon

Same template + different seeds → maps that feel related but aren't identical.

### Why This Matters

1. **Designer control** — Pick a template, get appropriate geography
2. **Replayability** — Same template, different experience each time
3. **Benchmark** — If our "Archipelago" feels like Azgaar's, we're good enough
4. **Testing** — Compare output against Azgaar for same template type

### Template Parameters (inferred from Azgaar)

| Parameter              | Affects                           |
| ---------------------- | --------------------------------- |
| Land ratio             | How much of map is land vs. water |
| Elevation distribution | Flat vs. mountainous              |
| Landmass count         | Continents vs. islands            |
| Coastline complexity   | Smooth vs. fjords/peninsulas      |
| Island size variance   | Uniform vs. mixed (big + small)   |

## Azgaar's DSL

Azgaar uses a **procedural DSL** — templates are sequences of operations, not constraint sets:

```
Hill 1 90-99 60-80 45-55
Hill 6-7 25-35 20-70 30-70
Range 1 40-50 45-55 45-55
Trough 2-3 20-30 15-85 20-30
Multiply 0.4 20-100
Mask 4
```

### Operations

| Operation    | Effect                                      | Parameters            |
| ------------ | ------------------------------------------- | --------------------- |
| **Hill**     | BFS flood fill, height decays exponentially | count, height, x%, y% |
| **Pit**      | Inverse of Hill (subtracts height)          | count, height, x%, y% |
| **Range**    | Mountain ridge between two points           | count, height, x%, y% |
| **Trough**   | Valley (inverse of Range)                   | count, height, x%, y% |
| **Strait**   | Water passage cutting through land          | width, direction      |
| **Mask**     | Distance-from-edge falloff (makes islands)  | fraction              |
| **Add**      | Add constant to heights                     | value, target         |
| **Multiply** | Scale heights                               | value, target         |
| **Smooth**   | Average with neighbors                      | fraction              |
| **Invert**   | Mirror heightmap                            | probability, axis     |

### Key Algorithm Insights

**Blob growth (Hill/Pit):**

```
height[neighbor] = height[current] ** blobPower * random(0.9, 1.1)
```

- `blobPower` ≈ 0.98 for 10k cells — controls decay rate
- Results in organic, circular-ish shapes

**Linear features (Range/Trough):**

- Path from A→B with random deviations (15-20% chance to veer)
- Height decays outward from ridge using `linePower`
- "Prominences" extend downhill every 6th cell

**Mask (island insulation):**

```
distance = (1 - nx²) * (1 - ny²)  // 1 at center, 0 at edges
height = height * distance
```

- Prevents land from touching map edges
- Higher fraction = more insulation

### Example: Low Island Decoded

```
Hill 1 90-99 60-80 45-55    # Main landmass, center-east
Hill 1-2 20-30 10-30 10-90  # Secondary hills, west side
Smooth 2                     # Blend everything
Hill 6-7 25-35 20-70 30-70  # Scattered terrain
Range 1 40-50 45-55 45-55   # Central ridge
Trough 2-3 20-30 15-85 20-30 # Northern valleys
Trough 2-3 20-30 15-85 70-80 # Southern valleys
Pit 5-7 15-25 15-85 20-80   # Lakes/depressions
Multiply 0.4 20-100         # Flatten land (keep it low)
Mask 4                       # Insulate from edges
```

## Our Implementation Approach

Two options:

**Option A: Port Azgaar's DSL**

- Implement the same operations in C#
- Templates are directly portable
- Known-good results

**Option B: Constraint-based**

- Define templates as goals (land ratio, elevation range, etc.)
- Generator tries to satisfy constraints
- More flexible, less predictable

**Recommendation:** Start with Option A. Port the DSL, validate against Azgaar output, then evolve.

## Azgaar as Benchmark

For land/terrain generation, Azgaar output is the quality bar:

- Generate map with our system using "Low Island" template
- Generate map with Azgaar using same template type
- Visual comparison: Does ours feel as plausible?
- Statistical comparison: Similar land ratio, elevation distribution?

We don't need to match Azgaar exactly — just be in the same ballpark of "this looks like a real place."
