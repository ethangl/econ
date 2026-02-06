# Soil & Biomes

Four-stage model: physical inputs → soil → biome → vegetation → fauna. Geology, topography, and drainage create spatial variation that a pure climate matrix can't produce.

## Pipeline

```
HeightGrid + ClimateData + RiverData
        ↓
   Derived Inputs (slope, salt proximity, loess)
        ↓
   Rock Type Layer (Perlin noise → 4 categories)
        ↓
   Stage 1: Soil Assignment (priority cascade → 8 types + fertility float)
        ↓
   Stage 2: Biome Assignment (soil + temp + precip → 18 biomes)
        ↓
   Stage 3: Vegetation (biome → type + density, modified by elevation and salinity)
        ↓
   Stage 4: Fauna (vegetation + water → fish, game, waterfowl, fur animals)
        ↓
   BiomeData (per-cell: soil, rock, fertility, biome, veg, fauna, habitability, subsistence, movementCost)
```

Water cells (height <= sea level) are excluded from this pipeline. They have no soil, biome, or vegetation.

## Contents

| Document                     | Topic                                              |
| ---------------------------- | -------------------------------------------------- |
| [Soil](soil.md)              | Inputs, derived inputs, rock types, 8 soil types   |
| [Biomes](biomes.md)          | 18 biomes, habitability, subsistence concept       |
| [Vegetation](vegetation.md)  | 7 types, density, treeline, rendering              |
| [Fauna](fauna.md)            | Fish, game, waterfowl, fur; subsistence formula    |
| [Movement Cost](movement.md) | Per-cell terrain cost, per-edge elevation & rivers |
| [Resources](resources.md)    | Resource types, production chains, geological ores |

## Data Structures

### BiomeData

```
BiomeData {
    Mesh: CellMesh              // reference
    SoilType: SoilType[]        // per-cell enum (8 values)
    RockType: RockType[]        // per-cell enum (4 values)
    Fertility: float[]          // per-cell 0-1
    BiomeId: BiomeId[]          // per-cell enum (18 values)
    Vegetation: VegetationType[]// per-cell enum (7 values)
    VegetationDensity: float[]  // per-cell 0-1
    Habitability: float[]       // per-cell 0-100 (biome base + river bonus)
    Subsistence: float[]        // per-cell 0-1 (natural carrying capacity, derived from fauna + veg)
    FishAbundance: float[]      // per-cell 0-1
    GameAbundance: float[]      // per-cell 0-1
    WaterfowlAbundance: float[] // per-cell 0-1
    FurAbundance: float[]       // per-cell 0-1
    MovementCost: float[]       // per-cell (slope + altitude + ground + vegetation)
    Slope: float[]              // per-cell 0-1 (derived, useful downstream)
    SaltEffect: float[]         // per-cell 0-1 (derived, useful downstream)
    IronAbundance: float[]      // per-cell 0-1 (geological, see resources.md)
    GoldAbundance: float[]      // per-cell 0-1 (geological, see resources.md)
}
```

### Enums

```
enum SoilType       { Permafrost, Saline, Lithosol, Alluvial, Aridisol, Laterite, Podzol, Chernozem }
enum RockType       { Volcanic, Limestone, Sedimentary, Granite }
enum VegetationType { None, LichenMoss, Grass, Shrub, DeciduousForest, ConiferousForest, BroadleafForest }
enum BiomeId        { Glacier, Tundra, SaltFlat, CoastalMarsh, AlpineBarren, MountainShrub,
                      Floodplain, Wetland, HotDesert, ColdDesert, Scrubland,
                      TropicalRainforest, TropicalDryForest, Savanna,
                      BorealForest, TemperateForest, Grassland, Woodland }
```

The key design property: two cells at the same latitude with the same climate can have different soil, different biomes, and different economic potential because of geology, slope, and drainage. All tuning parameters are defined in their respective source documents.
