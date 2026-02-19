# Philosophy & Overview

See also: [Startup Performance Notes](./STARTUP_PERFORMANCE.md)
See also: [Render Performance Baseline](./debug/RENDER_PERFORMANCE_BASELINE.md)

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

## MapData Contract

Any world generator must produce a `MapData` that satisfies this contract:

### Required Data

| Component     | Fields                                                                    | Used By                            |
| ------------- | ------------------------------------------------------------------------- | ---------------------------------- |
| **MapInfo**   | Width, Height, Seed, TotalCells, LandCells, World.\*                      | Rendering, coordinate systems      |
| **Cells**     | Id, Center, VertexIndices, NeighborIds                                    | Mesh generation, pathfinding       |
|               | SeaRelativeElevation, HasSeaRelativeElevation, BiomeId, IsLand, FeatureId | Terrain rendering, transport costs |
|               | RealmId, ProvinceId, CountyId                                             | Political display, borders         |
|               | Population                                                                | County grouping, economy           |
| **Vertices**  | Vec2 positions                                                            | Voronoi polygon rendering          |
| **Realms**    | Id, Name, Color, ProvinceIds                                              | Political mode, borders            |
| **Provinces** | Id, Name, RealmId, CellIds                                                | Province mode, grouping            |
| **Rivers**    | Id, CellPath, Width, Discharge                                            | River rendering, transport         |
| **Biomes**    | Id, Name, Color, Habitability, MovementCost                               | Terrain colors, pathfinding        |
| **Features**  | Id, Type (ocean/lake/island)                                              | Water detection, transport         |

### Derived Data (computed after load)

| Component           | Computed By                         | Notes                                               |
| ------------------- | ----------------------------------- | --------------------------------------------------- |
| **Counties**        | MapGen political pipeline + adapter | Grouped before runtime simulation                   |
| **Lookup tables**   | `MapData.BuildLookups()`            | CellById, RealmById, ProvinceById, CountyById, etc. |
| **Markets**         | `MarketPlacer`                      | Trade zones                                         |
| **Transport graph** | `TransportGraph`                    | Pathfinding weights                                 |

### Invariants

1. All cell IDs are unique and sequential from 0
2. All vertex indices in cells are valid into Vertices list
3. All neighbor IDs reference existing cells
4. Cell.RealmId = 0 means neutral/unclaimed (valid)
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

**Design decision: configurable cell scale (default 2.5 km × 2.5 km, 6.25 km² area)**

Cell scale is emitted via `MapInfo.World.CellSizeKm` and defaults to `2.5 km` unless generation config overrides it.

### Map Dimensions Are Derived, Not Specified

Since cell size is fixed, map dimensions follow from cell count and aspect ratio:

| Input        | Example | Notes                           |
| ------------ | ------- | ------------------------------- |
| Cell count   | 20,000  | Controls map complexity/detail  |
| Aspect ratio | 16:9    | Controls map shape              |
| Cell size    | 2.5 km  | Default (~1.5 mi), configurable |

**Derivation:**

```
cellArea     = 2.5² = 6.25 km²
mapArea      = cellCount × cellArea
mapWidthKm   = sqrt(mapArea × aspectRatio)
mapHeightKm  = mapWidthKm / aspectRatio
```

**Example:** 20,000 cells at 16:9 → 125,000 km² total → 471 km × 265 km

This means:

- Changing cell count changes map size, not cell density
- Templates define shape and land ratio, not cell density
- All downstream systems (climate sweep, river accumulation, settlement spacing) can rely on consistent cell dimensions

### Derivation from River Constraints

Rivers flow along cell edges. River width as a percentage of cell width determines visibility:

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

### Reference Configurations

| Name          | Cell Count | Ratio | Map Size     | Land Area\*  | Comparable To |
| ------------- | ---------- | ----- | ------------ | ------------ | ------------- |
| Small island  | 5,000      | 16:9  | 236 × 133 km | ~16,000 km²  | Jamaica       |
| Large island  | 20,000     | 16:9  | 471 × 265 km | ~63,000 km²  | Sri Lanka     |
| England-scale | 40,000     | 16:9  | 667 × 375 km | ~125,000 km² | England       |
| Subcontinent  | 100,000    | 3:2   | 968 × 645 km | ~313,000 km² | Italy         |

\* Land area depends on template land ratio (assumes ~50% here)
