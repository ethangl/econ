# Movement Cost

Movement cost has two parts: a per-cell base cost (terrain difficulty at that location) and per-edge modifiers (elevation change and river crossing, computed at pathfinding time).

## Per-Cell Components

Four factors, all additive:

**Slope cost.** Steep terrain is hard to traverse regardless of what's on it. Scales with the slope value (0-1):

```
slopeCost = 1 + slope × slopeWeight
```

Where `slopeWeight` controls how much slope matters (e.g., 4.0 → a max-slope cell costs 5× flat ground). This is the dominant factor for mountain passes and cliff faces.

**Altitude cost.** High elevation is harder to traverse even on flat ground — thin air, exposure, temperature extremes. Kicks in above a threshold and scales linearly:

```
altitudeCost = max(0, (height - altitudeThreshold) / (maxHeight - altitudeThreshold)) × altitudeWeight
```

Where `altitudeThreshold` (e.g., 50 height units) is where the penalty starts, and `altitudeWeight` (e.g., 2.0) is the max penalty at the height ceiling. A flat plateau at height 80 is meaningfully harder to cross than a flat plain at height 30, even with identical slope.

**Ground cost.** Some soil types impose a flat traversal penalty — frozen ground, loose scree, boggy wetland, salt crust:

| Soil Type  | Ground Cost |
| ---------- | ----------- |
| Permafrost | 3.0         |
| Saline     | 1.5         |
| Lithosol   | 2.0         |
| Alluvial   | 0.5         |
| Aridisol   | 1.0         |
| Laterite   | 1.0         |
| Podzol     | 1.2         |
| Chernozem  | 0.5         |

Low values = easy ground. Alluvial and chernozem are flat, firm, easy to walk on. Permafrost is the worst — uneven frozen/thawed surface.

**Biome override: Wetland.** Both Floodplain and Wetland sit on Alluvial soil (ground cost 0.5), but wetlands are waterlogged and much harder to traverse. Wetland biome overrides its soil ground cost to **2.5**. This is the only biome-level ground cost override; all other biomes use their soil's value directly.

**Vegetation cost.** Dense vegetation impedes movement. Scales with both vegetation type and density:

| Vegetation        | Veg Weight |
| ----------------- | ---------- |
| None              | 0          |
| Lichen/Moss       | 0.2        |
| Grass             | 0          |
| Shrub             | 0.5        |
| Deciduous Forest  | 1.5        |
| Coniferous Forest | 2.0        |
| Broadleaf Forest  | 3.0        |

```
vegCost = vegWeight[vegType] × density
```

Dense broadleaf forest (density 1.0) adds 3.0. Sparse woodland (density 0.3, deciduous) adds 0.45. Open grassland adds nothing.

## Per-Cell Formula

```
baseCost = slopeCost + altitudeCost + groundCost[soilType] + vegWeight[vegType] × density
```

Stored per-cell in BiomeData.

**Examples:**

| Cell Description          | Slope | Altitude | Ground | Veg  | Total |
| ------------------------- | ----- | -------- | ------ | ---- | ----- |
| Flat lowland grassland    | 1.0   | 0        | 0.5    | 0    | 1.5   |
| Gentle floodplain         | 1.0   | 0        | 0.5    | 0    | 1.5   |
| Wetland (reed marsh)      | 1.0   | 0        | 2.5    | 0    | 3.5   |
| Dense boreal forest       | 1.0   | 0        | 1.2    | 1.6  | 3.8   |
| Steep mountain shrub      | 3.0   | 1.2      | 2.0    | 0.25 | 6.45  |
| Dense tropical rainforest | 1.0   | 0        | 1.0    | 3.0  | 5.0   |
| High flat plateau         | 1.0   | 1.5      | 1.0    | 0    | 3.5   |
| Glacier                   | 1.0   | 2.0      | 3.0    | 0    | 6.0   |
| Flat hot desert           | 1.0   | 0        | 1.0    | 0    | 2.0   |

Glacier is not literally "impassable" in the formula but will be effectively so due to habitability 0, high altitude, and no resources to motivate crossing. Could add an explicit impassable flag if needed.

## Per-Edge Modifier: Elevation Change

The per-cell base cost captures how hard a cell is to be _in_. But moving _between_ cells also depends on direction — climbing is harder than descending. This is asymmetric and can't be stored per-cell, so it's applied at pathfinding time:

```
heightDelta = Heights[B] - Heights[A]
if heightDelta > 0:   // uphill
    elevationCost = heightDelta × uphillWeight
else:                  // downhill
    elevationCost = abs(heightDelta) × downhillWeight
```

Where `uphillWeight` > `downhillWeight` (climbing costs more than descending). Starting values: `uphillWeight = 0.05`, `downhillWeight = 0.01`. A 10-unit climb adds 0.5; the same descent adds 0.1.

This means pathfinding naturally prefers: flat routes > downhill > uphill. Mountain passes are expensive in both directions (high base cost from slope + altitude) but even more expensive when approached from the low side.

## Per-Edge Modifier: River Crossing

Rivers flow along Voronoi edges (cell boundaries). Crossing a river edge adds a flat penalty that scales with the edge's flux — fording a creek is easy, crossing a major river is hard:

```
if edge has river flux:
    fluxNorm = clamp(edgeFlux / referenceFlux, 0, 1)
    riverCrossingCost = riverCrossingBase + fluxNorm × riverFluxWeight
```

Where `referenceFlux = 1000` (same as biomes/fauna), `riverCrossingBase = 1.0`, `riverFluxWeight = 2.0`. A small stream (flux 100, 10% of reference) adds ~1.2, a major river (flux 1000) adds 3.0.

Combined per-edge formula (all additive):

```
edgeCost(A → B) = baseCost[B] + elevationCost(A, B) + riverCrossingCost(A, B)
```

Consider: altitude cost (per-cell) could be made multiplicative instead of additive — `baseCost × (1 + altitudeFactor)` — so that hard terrain at altitude is disproportionately worse (thin air compounds the difficulty). Keeping it additive for now; easy to change during tuning.

River crossing cost is symmetric (same penalty in both directions). Bridges (future) would reduce or eliminate this penalty along developed roads.
