# Philosophy & Overview

## Goals

**Primary goal:** Economic simulation. Geography is infrastructure, not the point.

**Target:** Verisimilitude — plausible randomness, not geological accuracy. We want worlds that _feel_ right, not worlds that would satisfy a geologist.

**Approach:**

- Full deterministic procedural generation (seed → world)
- Start with naive stubs, iterate toward Azgaar-quality output
- Azgaar's results are a good reference for "good enough"

## Two Buckets

1. **Geographic** — modeling the natural world: geology, terrain, climate, hydrology, biomes, flora/fauna
2. **Cultural** — modeling human response to geography: settlements, political boundaries, naming, population distribution

Cultural systems are easier to replace wholesale because:

- We already override much of Azgaar's output (county grouping, markets)
- Design choices are ours to make
- Less need for physical plausibility

Geographic systems are harder because:

- Must be internally consistent (rivers flow downhill, biomes follow climate)
- Require domain knowledge or good procedural algorithms
- Our cultural systems depend on them

**But:** We don't need to boil the ocean. A naive heightmap + "water flows downhill" gets us 80% of the way to plausible rivers without modeling precipitation, wind patterns, or erosion.

## Current Azgaar Dependencies

Important distinction:

1. **What Azgaar computes internally** — full simulation pipeline
2. **What Azgaar exports to JSON** — subset of computed data
3. **What we import/use** — subset of the export

We only see outputs, not intermediate data. This matters because replacing a system means understanding the full chain, not just visible results.

### What We Import

Via `AzgaarParser` → `MapConverter`:

| Data                                | Used For                           | Bucket     |
| ----------------------------------- | ---------------------------------- | ---------- |
| Cell geometry (vertices, neighbors) | Mesh generation, pathfinding       | Geographic |
| Heightmap                           | Terrain rendering, transport costs | Geographic |
| Biomes                              | Terrain colors, resource placement | Geographic |
| Rivers (paths, widths)              | Rendering, transport routes        | Geographic |
| Climate (temp, precipitation)       | Currently unused directly          | Geographic |
| States                              | Political mode, borders            | Cultural   |
| Provinces                           | Political subdivision              | Cultural   |
| Burgs (settlements)                 | County seating, naming             | Cultural   |
| Population per cell                 | County grouping, economy           | Cultural   |
| Names                               | Display only                       | Cultural   |

### What Azgaar Computes (but we don't see)

These intermediate results aren't in the JSON export but are necessary for the outputs we use:

| Hidden Data       | Needed For           | Notes                                |
| ----------------- | -------------------- | ------------------------------------ |
| Precipitation map | Rivers, biomes       | Derived from wind patterns + terrain |
| Temperature map   | Biomes, habitability | From latitude + altitude             |
| Wind patterns     | Precipitation        | Prevailing winds, rain shadows       |
| Flow accumulation | River paths          | Water volume per cell                |
| Soil/fertility    | Population placement | Affects where people settle          |

## MapData Contract

Any world generator (Azgaar or ours) must produce a `MapData` that satisfies this contract:

### Required Data

| Component     | Fields                                               | Used By                            |
| ------------- | ---------------------------------------------------- | ---------------------------------- |
| **MapInfo**   | Width, Height, Seed, TotalCells, LandCells, SeaLevel | Rendering, coordinate systems      |
| **Cells**     | Id, Center, VertexIndices, NeighborIds               | Mesh generation, pathfinding       |
|               | Height, BiomeId, IsLand, FeatureId                   | Terrain rendering, transport costs |
|               | StateId, ProvinceId                                  | Political display, borders         |
|               | Population                                           | County grouping, economy           |
| **Vertices**  | Vec2 positions                                       | Voronoi polygon rendering          |
| **States**    | Id, Name, Color, ProvinceIds                         | Political mode, borders            |
| **Provinces** | Id, Name, StateId, CellIds                           | Province mode, grouping            |
| **Rivers**    | Id, CellPath, Width, Discharge                       | River rendering, transport         |
| **Biomes**    | Id, Name, Color, Habitability, MovementCost          | Terrain colors, pathfinding        |
| **Features**  | Id, Type (ocean/lake/island)                         | Water detection, transport         |

### Derived Data (computed after load)

| Component           | Computed By              | Notes                      |
| ------------------- | ------------------------ | -------------------------- |
| **Counties**        | `CountyGrouper`          | Groups cells by population |
| **Lookup tables**   | `MapData.BuildLookups()` | CellById, StateById, etc.  |
| **Markets**         | `MarketPlacer`           | Trade zones                |
| **Transport graph** | `TransportGraph`         | Pathfinding weights        |

### Invariants

1. All cell IDs are unique and sequential from 0
2. All vertex indices in cells are valid into Vertices list
3. All neighbor IDs reference existing cells
4. Cell.StateId = 0 means neutral/unclaimed (valid)
5. Cell.ProvinceId = 0 means no province (valid for neutral)
6. Rivers have ordered CellPath from source to mouth
7. Every land cell has a valid BiomeId
8. Features correctly classify water bodies

## What Geography Matters for Economics?

Reframing: which geographic features actually drive interesting economic dynamics?

| Feature                    | Economic Impact                                 | Priority |
| -------------------------- | ----------------------------------------------- | -------- |
| **Coastlines/harbors**     | Maritime trade, fishing, naval power            | High     |
| **Rivers**                 | Transport corridors, irrigation, city placement | High     |
| **Mountains**              | Trade barriers, mining, defensibility           | High     |
| **Arable land**            | Food production, population capacity            | High     |
| **Forests**                | Timber, hunting, clearing for farmland          | Medium   |
| **Mineral deposits**       | Mining, industry, trade goods                   | Medium   |
| **Climate zones**          | Crop types, habitability                        | Medium   |
| **Deserts/tundra**         | Barriers, sparse population                     | Low      |
| **Exact biome boundaries** | Mostly cosmetic                                 | Low      |

**Implication:** We need good terrain (height, water) and resource placement. Biome rendering is cosmetic polish — a simple temperature×moisture lookup is fine.

## Cell Scale

**Design decision: 2.5 km × 2.5 km cells (6.25 km² area)**

This is a fixed constant across all maps, enabling tessellated submaps that stitch together seamlessly.

### Derivation from River Constraints

Rivers flow along cell edges (see [rivers.md](./rivers.md)). River width as a percentage of cell width determines visibility:

| River Width   | % of Cell (2.5 km) | Appearance                 |
| ------------- | ------------------ | -------------------------- |
| 50m           | 2%                 | Minimum visible edge river |
| 250m (Thames) | 10%                | Substantial river          |
| 500m          | 20%                | Major river                |
| **750m**      | **30%**            | **Maximum edge river**     |
| >1 km         | >40%               | Should fill cells instead  |

Rivers wider than ~750m (Mississippi, Amazon) become cell-filling geographic features rather than edges.

### Reference Scales

| Territory     | Land Area   | Land Cells |
| ------------- | ----------- | ---------- |
| England       | 130,000 km² | ~20,800    |
| Ireland       | 84,000 km²  | ~13,400    |
| Great Britain | 209,000 km² | ~33,400    |

**Mental model:** A cell is about 1.5 miles across.

### Azgaar Reference Settings (England-scale)

For generating maps with England-like characteristics (~130k km², ~40 provinces, ~10 kingdoms):

| Setting        | Value      |
| -------------- | ---------- |
| Seed           | 123        |
| Resolution     | 1920×1080  |
| Points         | 60,000     |
| Template       | Low Island |
| States         | 9          |
| Burgs          | 39         |
| Province ratio | 100        |
| Size variety   | 4          |
| Growth rate    | 1.5        |

These settings produce ~21,500 land cells, ~50 provinces, ~10 states — close to historic England's 39 counties across the Anglo-Saxon heptarchy.

### Implications

- Templates define _shape and land ratio_, not cell density
- A "continent" template means more cells, not larger cells
- CountyGrouper parameters may need adjustment (currently tuned for ~400 km² Azgaar cells)
- Historic English county (~3,300 km²) contains ~530 cells — good granularity

## Open Questions

- **Performance:** Generation time budget? Real-time editing?
- **Art style:** Realistic vs. stylized terrain?
