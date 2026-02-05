# River Generation

Azgaar uses classic **flow accumulation** algorithm.

## Algorithm

```
1. Resolve depressions (fill so water can always flow downhill)
2. Sort land cells by height (highest first)
3. For each cell:
   a. Add precipitation to flux
   b. Find lowest neighbor
   c. If flux > threshold (30): declare river
   d. Flow to lowest neighbor, accumulating flux
4. Handle confluences (larger flux wins river identity)
5. Handle lakes (accumulate flux, outlet continues river)
6. Optional: erode terrain (downcut river beds)
```

## Key Data Structures

| Array        | Type   | Purpose                 |
| ------------ | ------ | ----------------------- |
| `cells.fl`   | Uint16 | Water flux per cell     |
| `cells.r`    | Uint16 | River ID (0 = no river) |
| `cells.conf` | Uint8  | Confluence marker       |

## River Properties

| Property     | Calculation                             |
| ------------ | --------------------------------------- |
| Discharge    | Flux at mouth cell                      |
| Width        | `(discharge^0.7 / 500)` capped at 1     |
| Length       | Sum of segment lengths after meandering |
| Source width | `(source_flux^0.9 / 500)`               |

## Meandering

Points interpolated between cells based on:

- Distance between cells (more points for longer segments)
- River length (less meandering for young rivers)
- Perpendicular offset using sin/cos of flow angle

## Lake Integration

- Lakes accumulate flux from all inlet rivers
- Outlet cell drains to lowest shoreline neighbor
- Evaporation reduces outflow: `outlet_flux = inlet_flux - evaporation`

## Depression Resolution

Before flow simulation, depressions are filled:

```
for each land cell (sorted low to high):
    if height <= min(neighbor heights):
        height = min(neighbor heights) + 0.1
```

This ensures water can always find a path to the sea.

## Design Decision: Cell-Based vs Edge-Based Rivers

**Azgaar:** Rivers flow cell-to-cell. River "occupies" cells.

**Our preference:** Rivers flow along edges (Voronoi cell boundaries).

| Aspect             | Cell-based (Azgaar)                | Edge-based (ours)                   |
| ------------------ | ---------------------------------- | ----------------------------------- |
| Topology           | River path = list of cells         | River path = list of edges          |
| Query "has river?" | `cell.riverId > 0`                 | Check adjacent edges                |
| Political borders  | Rivers inside territory            | Rivers ARE borders naturally        |
| Transport routing  | Ambiguous (cross cell? alongside?) | Clear (travel along edge)           |
| Rendering          | Interpolate through cell centers   | Draw edge polyline (relaxed curves) |
| Flow direction     | Cell A → Cell B                    | Edge has upstream/downstream vertex |

## Why Edge-Based is Better for Us

1. **Rivers as natural borders** — Historical pattern. If rivers are edges, political boundaries that "follow the river" come for free. Counties don't get bisected by rivers — that's not how administrative boundaries form. People on opposite banks are in different jurisdictions.

2. **Inland islands** — Where rivers fork and rejoin, the enclosed area is naturally a separate region (cells surrounded by river edges). Cell-based rivers can't represent this cleanly.

3. **Cleaner transport model** — River transport follows edges, land transport crosses edges. No ambiguity.

4. **Dual graph elegance** — Voronoi edges are Delaunay edges. Water flows along Delaunay triangulation, land is partitioned by Voronoi. Clean separation.

5. **Rendering** — Our current `RiverRenderer` already draws polylines. Edge-based rivers map directly.

6. **County grouping** — Our `CountyGrouper` flood-fills across cell neighbors. If rivers are edges, flood fill naturally stops at rivers (can't cross a river edge). Counties form on one side or the other without special-case code.

## Implementation Notes

- Flow accumulation still happens per-cell (precipitation falls on cells)
- When flow crosses to neighbor, it exits via the shared edge
- Edge accumulates flux from all cells draining through it
- River threshold applies to edges, not cells
- Cells adjacent to river edges get `HasRiver` flag for gameplay queries

```
Cell data:          Edge data:
- precipitation     - flux (accumulated)
- height            - riverId (0 = no river)
- biome             - width (derived from flux)
                    - upstream/downstream vertex
```
