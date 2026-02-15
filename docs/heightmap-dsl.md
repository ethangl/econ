# Heightmap DSL Reference

The heightmap DSL is a line-oriented scripting language for procedural terrain generation. Each template is a sequence of operations that sculpt an elevation field on a Voronoi cell mesh. Operations are executed top-to-bottom and modify elevations in-place.

**Source files:**

- `src/MapGen/HeightmapMeterDsl.cs` — parser/executor
- `src/MapGen/HeightmapTerrainOps.cs` — operation implementations
- `src/MapGen/HeightmapTemplates.cs` — predefined templates

## Syntax

```
Operation arg1 arg2 ...
```

- One operation per line
- Lines starting with `#` are comments
- Blank lines are ignored
- Case-insensitive operation names

### Value types

| Type    | Format      | Examples          | Notes                                                                                                 |
| ------- | ----------- | ----------------- | ----------------------------------------------------------------------------------------------------- |
| Meters  | `<number>m` | `5625m`, `312.5m` | Signed meters relative to sea level. Always has `m` suffix.                                           |
| Percent | `<number>`  | `44`, `100`       | Map coordinate as percentage. 0=left/bottom, 100=right/top. Optional `%` suffix.                      |
| Count   | `<number>`  | `1`, `0.5`, `3`   | Placement count. Fractional part is a probability of +1 (e.g. `1.5` = 1 guaranteed, 50% chance of 2). |
| Float   | `<number>`  | `0.8`, `3`        | Plain numeric value.                                                                                  |

### Ranges

Any value can be a range using `-` as separator: `5625m-6250m`, `44-56`, `1-3`. A random value is picked uniformly within the range (inclusive). The parser finds the `-` separator by looking for a dash between two numeric characters, so negative values work correctly.

### Height range filters

Some operations accept a height range filter as their last argument:

| Token           | Meaning                                 |
| --------------- | --------------------------------------- |
| `land`          | 0m to max elevation                     |
| `water`         | -max depth to 0m                        |
| `all`           | Full range                              |
| `<min>m-<max>m` | Custom meter range (e.g. `1875m-5000m`) |

## Operations

### Hill

```
Hill <count> <height_m> <x%> <y%>
```

Creates dome-shaped elevation blobs via BFS flood-fill from a random seed point. The seed is placed randomly within the x/y bounding box. Elevation starts at `height_m` at the seed and decays exponentially through cell neighbors (power ~0.99 per hop), creating smooth rounded hills.

The decay power scales with cell count — denser meshes use higher powers so blobs spread proportionally.

If the seed cell is already near max elevation, it retries up to 50 times within the bounding box.

**Example:** `Hill 1 5625m-6250m 44-56 40-60` — one massive 5.6-6.3km dome near map center.

### Pit

```
Pit <count> <depth_m> <x%> <y%>
```

Inverse of Hill. BFS flood-fill that _subtracts_ elevation, creating depressions, bays, or lakes. Seeds are retried to start on land cells.

**Example:** `Pit 5-7 937.5m-1562.5m 15-85 20-80` — 5-7 depressions scattered across the map interior.

### Range

```
Range <count> <height_m> <x%> <y%>
```

Creates a linear mountain ridge. Picks a start and end point within the bounding box, traces a greedy path between them on the cell graph (with random perturbation for natural wandering), then does BFS outward from the ridge line. Elevation decays with distance from the ridge using line power (~0.82 per step). Every 6th ridge cell also smooths a downhill path to blend the ridge into surroundings.

Start and end points are constrained to be within a distance band (map_width/8 to map_width/3) so ridges are neither too short nor too long.

**Example:** `Range 1-2 1875m-3750m 5-15 25-75` — 1-2 ridges along the left edge of the map.

### Trough

```
Trough <count> <depth_m> <x%> <y%>
```

Inverse of Range. Creates a linear valley or canyon. Same path-tracing as Range but subtracts elevation. Seeds retry to start on land. The distance band is slightly different (max distance = map_width/2 instead of /3), so troughs can be longer.

**Example:** `Trough 3-4 937.5m-1250m 15-85 20-80` — several valleys cut through the interior.

### Mask

```
Mask <fraction>
```

Applies an edge-falloff envelope that pushes map edges toward sea level while preserving the center. This is how island templates create ocean surrounding the landmass.

The mask computes a distance factor for each cell:

```
nx = 2 * (x / width) - 1      // normalized to [-1, 1]
ny = 2 * (y / height) - 1
distance = (1 - nx^2) * (1 - ny^2)   // 1 at center, 0 at edges
```

The fraction controls blending strength:

```
result = (elevation * (|fraction| - 1) + elevation * distance) / |fraction|
```

- Higher fraction = gentler effect (more original elevation preserved)
- `Mask 3` is typical for islands (edges strongly submerged, center mostly intact)
- `Mask 4` is gentler (more coastal land survives)
- Negative fraction inverts the distance (`1 - distance`), pushing the _center_ down instead of edges

**Example:** `Mask 3` — standard island masking.

### Add

```
Add <delta_m> [height_range]
```

Adds a constant elevation value to all cells within the optional height range. Without a range, applies to all cells. If the range is `land`, cells are clamped to stay non-negative (land won't flip to water from a negative add).

**Example:** `Add 812.5m all` — raise the entire map by ~813m (lifts sea floor into land).
**Example:** `Add -1250m 625m-5000m` — lower cells between 625-5000m by 1250m.

### Multiply

```
Multiply <factor> [height_range]
```

Multiplies elevation of cells within the height range by the factor. Useful for compressing or expanding an elevation band.

**Example:** `Multiply 0.8 1875m-5000m` — squash mid-to-high elevations to 80%.
**Example:** `Multiply 0.6 land` — reduce all land elevations to 60%.
**Example:** `Multiply 0.4 0m-5000m` — flatten positive elevations.

### Smooth

```
Smooth [passes]
```

Averages each cell with its Voronoi neighbors. The `passes` parameter is a blending factor (not iteration count):

```
mean = (cell + sum_of_neighbors) / (1 + neighbor_count)
result = passes <= 1 ? mean : (cell * (passes - 1) + mean) / passes
```

- `Smooth 1` — full average (aggressive smoothing)
- `Smooth 2` — 50/50 blend of original and neighbor average
- `Smooth 3` — 67/33 blend (gentle, preserves peaks)

**Example:** `Smooth 3` — gentle smoothing pass.

### Strait

```
Strait <width> <direction>
```

Carves a channel across the map. Direction is `vertical` (top-to-bottom) or `horizontal` (left-to-right). The path starts near the center of the perpendicular axis and traces a greedy route to the far edge with random perturbation. Width controls how many BFS rings expand outward from the path, carving deeper.

**Example:** `Strait 2 vertical` — carve a 2-cell-wide vertical channel through the map.

### Invert

```
Invert [probability] [axis]
```

Mirrors the elevation map along an axis with a given probability (default 0.5). This doubles the effective template variety — the same template can produce left-heavy or right-heavy variants.

- `probability` — chance of actually inverting (0.0 to 1.0, default 0.5)
- `axis` — `x` (flip left/right), `y` (flip top/bottom), `both` (flip both, default)

**Example:** `Invert 0.4 both` — 40% chance of mirroring the entire map.
**Example:** `Invert 0.25 x` — 25% chance of horizontal flip only.

## Template Composition Patterns

Templates typically follow this structure:

1. **Primary landmass** — large Hills or Ranges to establish the main terrain
2. **Sculpting** — Multiply/Add to adjust elevation distribution
3. **Secondary features** — smaller Hills, Ranges for variety
4. **Smoothing** — Smooth pass to blend BFS artifacts
5. **Subtractive features** — Troughs, Pits, Straits to carve water features
6. **Edge masking** — Mask to create ocean (island templates)
7. **Symmetry breaking** — Invert for random mirroring

Not every template uses all stages. Continental templates may skip masking. Templates with straits typically use them between additive and subtractive phases.

## Tuning Profiles

The `HeightmapTemplateCompiler` can apply per-template tuning profiles that scale DSL values before execution. This adjusts templates for different cell counts and aspect ratios without editing the template text. Tuning scales:

- `TerrainMagnitudeScale` — scales meter values on Hill/Pit/Range/Trough
- `AddMagnitudeScale` — scales meter values on Add
- `MaskScale` — scales the Mask fraction
- `LandMultiplyFactorScale` — scales the deviation of Multiply factors from 1.0 (only for `land` range)

Templates without explicit tuning profiles use unscaled values.
