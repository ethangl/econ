# Stage 4: Fauna

Animals inhabit biomes based on vegetation and water access. They contribute to subsistence (hunting, fishing) and are harvestable economic resources (meat, furs, fish as trade goods).

## Fauna Types

| Fauna           | Habitat                       | Economic Use                                            |
| --------------- | ----------------------------- | ------------------------------------------------------- |
| **Fish**        | Rivers, lakes, ocean coast    | Food (subsistence + trade), scales with water body size |
| **Game**        | Forest, grassland, savanna    | Meat, hides. Large animals — deer, boar, elk, antelope  |
| **Waterfowl**   | Wetland, coastal marsh, lakes | Meat, feathers. Ducks, geese, wading birds              |
| **Fur Animals** | Cold forests, tundra          | Pelts (trade good). Fox, beaver, mink, ermine           |

## Biome → Fauna Mapping

Each biome supports 0-3 fauna types. Base values are the pre-density multiplier for each fauna type (0 = absent, applied in [Abundance Calculation](#abundance-calculation) below).

| Biome               | Game | Waterfowl | Fur | Notes                        |
| ------------------- | ---- | --------- | --- | ---------------------------- |
| Glacier             | 0    | 0         | 0   |                              |
| Tundra              | 0    | 0         | 0.3 | Sparse fur animals           |
| Salt Flat           | 0    | 0         | 0   |                              |
| Coastal Marsh       | 0    | 0.8       | 0   | Prime waterfowl habitat      |
| Alpine Barren       | 0    | 0         | 0   |                              |
| Mountain Shrub      | 0.3  | 0         | 0.3 | Sparse game and fur          |
| Floodplain          | 0.5  | 0.5       | 0   | Mixed fauna                  |
| Wetland             | 0    | 0.8       | 0   | Prime waterfowl habitat      |
| Hot Desert          | 0    | 0         | 0   |                              |
| Cold Desert         | 0    | 0         | 0   |                              |
| Scrubland           | 0.3  | 0         | 0   | Sparse game                  |
| Tropical Rainforest | 0.5  | 0         | 0   | Moderate game in dense cover |
| Tropical Dry Forest | 0.5  | 0         | 0   | Moderate game                |
| Savanna             | 0.8  | 0         | 0   | Large herds, open grazing    |
| Boreal Forest       | 0.5  | 0         | 0.8 | Prime fur territory          |
| Temperate Forest    | 0.8  | 0         | 0.5 | Good game, moderate fur      |
| Grassland           | 0.8  | 0         | 0   | Large herds                  |
| Woodland            | 0.5  | 0         | 0.3 | Mixed                        |

Fish abundance is computed from water features, not biome — see [Abundance Calculation](#abundance-calculation). Coastal and river fish bonuses apply to any biome.

## Abundance Calculation

Per-cell, per-fauna-type. All abundances clamped to [0, 1].

**Game** scales with vegetation density — denser habitat supports more animals:

```
gameAbundance = biomeGameBase × vegetationDensity
```

Savanna and grassland are high (0.8) because large herds graze open land. Forest game is moderate (0.5 — dense cover, smaller groups).

**Fur animals** scale with density and a cold bonus that increases abundance as temperature drops:

```
coldBonus = 1.0 + max(0, coldBonusThreshold - temp) / coldBonusRange
furAbundance = clamp(biomeFurBase × vegetationDensity × coldBonus, 0, 1)
```

Where `coldBonusThreshold = 5°C` (bonus starts below this) and `coldBonusRange = 20°C` (scales over this range). At 5°C coldBonus = 1.0, at -15°C coldBonus = 2.0. Peaks in boreal forest and tundra.

**Waterfowl** scale with water proximity — how many of the cell's edges border water (river-carrying edges or edges adjacent to water cells):

```
waterProximity = clamp(waterEdgeCount / waterProximityScale, 0, 1)
waterfowlAbundance = biomeWaterfowlBase × waterProximity
```

Where `waterProximityScale = 3` (a cell with 3+ water edges gets full proximity). Wetlands and coastal marshes naturally have high waterEdgeCount.

**Fish** are computed from water features, not biome. Three additive components:

```
riverFish = clamp(maxAdjacentEdgeFlux / referenceFlux, 0, 1) × riverFishBase
coastalFish = coastalFishBase if any neighbor is ocean, else 0
lakeFish = lakeFishBase if any neighbor is lake, else 0
estuaryBonus = estuaryFishBonus if both coastalFish > 0 AND riverFish > 0, else 0

fishAbundance = clamp(riverFish + coastalFish + lakeFish + estuaryBonus, 0, 1)
```

| Parameter        | Value | Notes                             |
| ---------------- | ----- | --------------------------------- |
| riverFishBase    | 0.6   | Max fish from river alone         |
| coastalFishBase  | 0.4   | Ocean adjacency                   |
| lakeFishBase     | 0.3   | Lake adjacency                    |
| estuaryFishBonus | 0.3   | River mouth bonus (coast + river) |
| referenceFlux    | 1000  | Edge flux for a "large river"     |

A river mouth: 0.6 + 0.4 + 0.3 = 1.3 → clamped to 1.0. Estuaries are the most productive water feature, as expected.

## Fauna as Economic Resources

Fauna abundance beyond subsistence needs becomes harvestable for trade (meat, hides, pelts, feathers). See [Resources](resources.md) for the full production chains and resource type list.

## Depletion (Future)

In the current implementation, fauna abundance is static — computed at generation time and doesn't change. Future versions could model:

- Overhunting reducing game/fur abundance
- Overfishing depleting fish stocks
- Population growth displacing wildlife (habitat loss from clearing)
- Recovery over time when pressure decreases

This would create a dynamic resource layer where early exploitation of easy wildlife transitions to managed agriculture — a natural economic progression.

## Subsistence Calculation

Subsistence is computed after fauna abundance is known. Per-cell:

```
subsistence = vegetationFood + sum(faunaAbundance[type] × faunaFoodValue[type]) - climatePenalty
```

Clamped to [0, 1].

**Vegetation food.** What the land naturally provides, based on vegetation type, density, and soil fertility:

| Vegetation        | Food Source              | Base Value       |
| ----------------- | ------------------------ | ---------------- |
| None              | Nothing                  | 0                |
| Lichen/Moss       | Negligible               | 0.02             |
| Grass             | Grazing, wild grains     | 0.15 × fertility |
| Shrub             | Berries, small game      | 0.08             |
| Deciduous Forest  | Hunting, nuts, foraging  | 0.12 × density   |
| Coniferous Forest | Hunting, sparse foraging | 0.06 × density   |
| Broadleaf Forest  | Fruit, game, foraging    | 0.10 × density   |

Grass subsistence scales with soil fertility — rich grassland supports more grazing and wild grain than poor grassland. Forest subsistence scales with density — denser forest has more game and forage.

**Fauna food values.** How much each fauna type contributes to subsistence per unit abundance:

| Fauna Type  | Food Value | Notes                                |
| ----------- | ---------- | ------------------------------------ |
| Fish        | 0.25       | Reliable protein, easily harvested   |
| Game        | 0.20       | Large animals, harder to catch       |
| Waterfowl   | 0.10       | Seasonal, lower caloric value        |
| Fur Animals | 0.05       | Primarily valued for pelts, not food |

A coastal floodplain cell with fishAbundance=0.8, gameAbundance=0.4, and grass vegetationFood=0.10 gets: 0.10 + (0.8×0.25 + 0.4×0.20) = 0.10 + 0.28 = 0.38 before climate penalty. Add a river fish bonus and it approaches the ~0.45 in the typical values table.

**Climate penalty.** Extreme temperatures reduce subsistence — short growing seasons, harsh winters, extreme heat:

```
if temp < 0°C:  climatePenalty = (0 - temp) × coldPenaltyRate (0.02)
if temp > 35°C: climatePenalty = (temp - 35) × heatPenaltyRate (0.02)
```

This mostly affects tundra and desert margins. Moderate climates have no penalty.
