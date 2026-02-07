# Stage 2: Biomes

Biomes are the visible ecosystem layer. Soil constrains which biomes are possible; temperature and precipitation select among them.

## Biome Assignment

Each soil type maps to 1-3 possible biomes. Temperature and precipitation select within those options.

| Soil Type  | Biome Options                                     | Selection                                                        |
| ---------- | ------------------------------------------------- | ---------------------------------------------------------------- |
| Permafrost | Glacier, Tundra                                   | temp < -10 → Glacier; else Tundra                                |
| Saline     | Salt Flat, Coastal Marsh                          | precip < 15 → Salt Flat; else Coastal Marsh                      |
| Lithosol   | Alpine Barren, Mountain Shrub                     | temp < -3 OR elev > 85 → Alpine Barren; else Mountain Shrub      |
| Alluvial   | Floodplain, Wetland                               | cellFlux > 500 AND slope < 0.1 → Wetland; else Floodplain        |
| Aridisol   | Hot Desert, Cold Desert, Scrubland                | temp > 25 → Hot Desert; temp < 5 → Cold Desert; else Scrubland   |
| Laterite   | Tropical Rainforest, Tropical Dry Forest, Savanna | precip > 80 → Rainforest; precip > 70 → Dry Forest; else Savanna |
| Podzol     | Boreal Forest, Temperate Forest                   | temp < 5 → Boreal; else Temperate                                |
| Chernozem  | Grassland, Woodland                               | precip > 45 → Woodland; else Grassland                           |

This gives 18 land biomes, each with a clear physical basis. A 19th biome — **Lake** — is assigned to inland water cells detected by the river pipeline (see below).

## Biome Properties

| Biome               | Soil Parent | Habitability |
| ------------------- | ----------- | ------------ |
| Glacier             | Permafrost  | 0            |
| Tundra              | Permafrost  | 5            |
| Salt Flat           | Saline      | 2            |
| Coastal Marsh       | Saline      | 10           |
| Alpine Barren       | Lithosol    | 0            |
| Mountain Shrub      | Lithosol    | 8            |
| Floodplain          | Alluvial    | 90           |
| Wetland             | Alluvial    | 15           |
| Hot Desert          | Aridisol    | 4            |
| Cold Desert         | Aridisol    | 5            |
| Scrubland           | Aridisol    | 15           |
| Tropical Rainforest | Laterite    | 60           |
| Tropical Dry Forest | Laterite    | 50           |
| Savanna             | Laterite    | 25           |
| Boreal Forest       | Podzol      | 12           |
| Temperate Forest    | Podzol      | 70           |
| Grassland           | Chernozem   | 80           |
| Woodland            | Chernozem   | 85           |
| Lake                | —           | 0            |

## Lake Cells

Lakes are inland depressions filled by the river pipeline. They are detected at vertex level (`RiverData.IsLake`); a cell is classified as Lake if a majority (> 50%) of its vertices are lake vertices. Lake cells are excluded from the entire biome pipeline — no soil, vegetation, fauna, resources, or movement cost. They render as water.

**Lake proximity effect.** Freshwater lakes increase soil moisture in surrounding land. A BFS spreads outward from lake cells up to 3 hops, producing a `lakeEffect` value (1.0 adjacent, decaying to 0). Fertility is multiplied by `(1 + lakeEffect)`, so cells directly adjacent to a lake get up to 2× fertility. The BFS does not spread through ocean cells.

Lakes also contribute to fauna on neighboring land cells: lake neighbors get fish abundance (lake fish base = 0.3) and lake edges count as water for waterfowl proximity.

Note: lakes are freshwater and do **not** seed salt proximity (only ocean cells do).

## Habitability vs. Subsistence

Two independent per-cell values, both computed at biome time.

**Habitability** measures how comfortable a place is to live — climate, terrain, exposure. It answers "do people _want_ to settle here?" independent of whether the land can feed them. A temperate forest on granite has high habitability (nice climate) but poor subsistence (need to clear trees, acidic soil). Habitability comes from the biome base value in the table above, plus a river adjacency bonus (see below).

**Subsistence** measures how many people the land can feed without economic development — no farms, no trade, no clearing. It answers "can people survive here on day one?" This is the natural carrying capacity from foraging, hunting, fishing, and unimproved grazing.

|                       | High Subsistence                                             | Low Subsistence                                               |
| --------------------- | ------------------------------------------------------------ | ------------------------------------------------------------- |
| **High Habitability** | Grassland, floodplain — settle immediately, population grows | Temperate forest on granite — nice but need development first |
| **Low Habitability**  | Tropical floodplain — rich but harsh, disease                | Desert, tundra, glacier — need development AND trade          |

Population placement uses both: initial settlements gravitate toward cells where both values are high. Growing beyond subsistence capacity requires development (clearing forest for farmland, building farms) or trade (grain imports to a mining town).

## Subsistence Capacity

Subsistence is a per-cell float (0-1) representing natural food production without economic development — no farms, no trade, no clearing. Computed in Stage 4 after fauna abundance is known, from vegetation foraging, fauna hunting/fishing, and climate modifiers. See [Subsistence Calculation](fauna.md#subsistence-calculation) for the formula.

## Subsistence by Biome (Typical Values)

| Biome               | Subsistence | Why                                   |
| ------------------- | ----------- | ------------------------------------- |
| Glacier             | 0           | No food source                        |
| Tundra              | 0.02        | Lichen, sparse hunting                |
| Salt Flat           | 0           | Nothing grows                         |
| Coastal Marsh       | 0.25        | Fish, waterfowl, reeds                |
| Alpine Barren       | 0           | Too exposed                           |
| Mountain Shrub      | 0.05        | Sparse berries, small game            |
| Floodplain          | 0.45        | Wild grains, fish, game               |
| Wetland             | 0.20        | Fish, waterfowl, but hard to farm     |
| Hot Desert          | 0.01        | Almost nothing                        |
| Cold Desert         | 0.01        | Almost nothing                        |
| Scrubland           | 0.05        | Sparse game, tubers                   |
| Tropical Rainforest | 0.15        | Fruit, game (but dense, disease)      |
| Tropical Dry Forest | 0.12        | Fruit, game, seasonal                 |
| Savanna             | 0.20        | Grazing, wild grain, game             |
| Boreal Forest       | 0.10        | Hunting, some foraging                |
| Temperate Forest    | 0.15        | Hunting, nuts, berries                |
| Grassland           | 0.35        | Grazing, wild grain                   |
| Woodland            | 0.25        | Mixed hunting, foraging, some grazing |
| Lake                | 0           | Water body, no land food production   |

These are typical values including fauna contributions. A grassland cell on a river gets ~0.50 (fish fauna boosts subsistence). A coastal floodplain at a river mouth could reach ~0.80 — the most naturally productive land on the map.

## River Adjacency Bonus

Cells adjacent to river edges get a habitability bonus. Water access is universally valuable. Uses our edge-based river topology:

```
maxEdgeFlux = max flux across all of this cell's Voronoi edges
fluxNorm = clamp(maxEdgeFlux / referenceFlux, 0, 1)
habitabilityBonus = riverBonusMax × sqrt(fluxNorm)
```

Where `referenceFlux = 1000` (a significant river — roughly 20 average-precip vertices of upstream drainage; flux units are accumulated precipitation per vertex) and `riverBonusMax = 15`. The sqrt gives diminishing returns — a small stream provides most of the benefit, a large river adds incrementally more:

- Tiny stream (1% of reference): bonus ≈ 1.5
- Moderate river (25% of reference): bonus ≈ 7.5
- Large river (100% of reference): bonus = 15

Subsistence benefits from rivers come through fish fauna abundance ([Stage 4](fauna.md)) rather than a separate habitability bonus. Both are computed at biome time, stored per-cell.
