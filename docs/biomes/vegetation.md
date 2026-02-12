# Stage 3: Vegetation

Vegetation is what grows (or doesn't) on the soil. It's the visible surface of the biome and the source of organic resources. Each biome implies a dominant vegetation type, but density varies continuously with precipitation and temperature within a biome.

## Vegetation Types

| Vegetation            | Color                            | Description                                      |
| --------------------- | -------------------------------- | ------------------------------------------------ |
| **None**              | Transparent (soil shows through) | Bare ground — ice, rock, salt crust, sand        |
| **Lichen/Moss**       | Muted gray-green                 | Ground-hugging, no canopy, very slow-growing     |
| **Grass**             | Gold-green                       | Open cover, low movement cost, grazeable         |
| **Shrub**             | Dusty olive                      | Woody but short, partial cover, sparse resources |
| **Deciduous Forest**  | Green                            | Seasonal canopy, good timber, moderate density   |
| **Coniferous Forest** | Dark blue-green                  | Year-round canopy, softwood timber, dense        |
| **Broadleaf Forest**  | Deep green                       | Tropical canopy, hardwood, very dense            |

## Biome → Vegetation Mapping

Each biome has a dominant vegetation type and a density range. Density is a float (0-1) driven by precipitation within the biome's precip range — wetter = denser.

| Biome               | Dominant Vegetation | Density Range | Notes                                 |
| ------------------- | ------------------- | ------------- | ------------------------------------- |
| Glacier             | None                | 0             | Bare ice                              |
| Tundra              | Lichen/Moss         | 0.1 – 0.3     | Sparse ground cover, no trees         |
| Salt Flat           | None                | 0             | Too saline for growth                 |
| Coastal Marsh       | Grass               | 0.3 – 0.6     | Salt-tolerant grasses, reeds          |
| Alpine Barren       | None                | 0 – 0.1       | Exposed rock, occasional lichen       |
| Mountain Shrub      | Shrub               | 0.2 – 0.5     | Hardy bushes, thin soil               |
| Floodplain          | Grass               | 0.5 – 0.8     | Rich grassland, ideal for agriculture |
| Wetland             | Grass               | 0.6 – 0.9     | Dense reeds, waterlogged              |
| Hot Desert          | None                | 0 – 0.1       | Bare sand/rock, occasional scrub      |
| Cold Desert         | None                | 0 – 0.1       | Bare gravel, sparse bunch grass       |
| Scrubland           | Shrub               | 0.2 – 0.4     | Dry-adapted bushes, open              |
| Tropical Rainforest | Broadleaf Forest    | 0.8 – 1.0     | Closed canopy, multiple layers        |
| Tropical Dry Forest | Broadleaf Forest    | 0.5 – 0.7     | Seasonal, thinner canopy              |
| Savanna             | Grass               | 0.3 – 0.5     | Grass with scattered trees            |
| Boreal Forest       | Coniferous Forest   | 0.5 – 0.8     | Dense spruce/pine, dark understory    |
| Temperate Forest    | Deciduous Forest    | 0.5 – 0.8     | Seasonal canopy, mixed species        |
| Grassland           | Grass               | 0.4 – 0.7     | Tall grass prairie, few trees         |
| Woodland            | Deciduous Forest    | 0.3 – 0.5     | Open canopy, grass understory         |

## Vegetation Properties

Vegetation type determines what you can extract and what it costs to change.

| Vegetation        | Timber          | Grazing | Crop Potential          | Clearing Cost |
| ----------------- | --------------- | ------- | ----------------------- | ------------- |
| None              | —               | —       | —                       | None          |
| Lichen/Moss       | —               | Poor    | —                       | None          |
| Grass             | —               | Good    | High (if fertile soil)  | Trivial       |
| Shrub             | —               | Poor    | Moderate                | Low           |
| Deciduous Forest  | Good            | —       | High (once cleared)     | Moderate      |
| Coniferous Forest | Moderate        | —       | Low (acidic soil)       | Moderate      |
| Broadleaf Forest  | High (hardwood) | —       | Moderate (once cleared) | High          |

**Timber** quality and yield scale with density. A boreal forest at 0.8 density produces more timber than one at 0.5. Broadleaf hardwood is the most valuable per unit but tropical forests are hard to exploit (high movement cost, difficult terrain).

**Grazing** is only viable on grass-type vegetation. Density affects carrying capacity — a dense floodplain supports more livestock than sparse scrubland. Forest understory is not grazeable without first clearing.

**Crop potential** is the agricultural yield if the cell were farmed. This combines soil fertility with vegetation clearability. Grass on chernozem = immediately farmable. Broadleaf forest on laterite = high clearing cost, moderate fertility once cleared. Coniferous forest on podzol = moderate clearing cost, poor farming due to acidic soil.

**Clearing cost** represents the labor/time to convert vegetation to farmland. This is a gameplay-relevant property — settling dense forest takes investment, while grasslands are ready to farm. Cleared forest doesn't regenerate in our timescale (no reforestation simulation in the current implementation).

## Elevation Effects

Elevation constrains vegetation independent of soil and precipitation. Two effects:

**Treeline.** Above a threshold altitude, trees cannot grow. The treeline varies with latitude — higher near the equator (warmer), lower near the poles (colder). We approximate this using temperature, which already encodes both latitude and altitude lapse rate:

```
treeline applies when: temp < treeline_temp (e.g., -3°C)
```

Below treeline, vegetation is capped at the highest type the temperature can sustain:

```
Forest types require temp >= treeline_temp (-3°C)   → downgrade to Shrub
Shrub requires temp >= -5°C                         → downgrade to Grass
Grass requires temp >= -8°C                         → downgrade to Lichen/Moss
Lichen/Moss requires temp >= -10°C                  → downgrade to None
```

Each step checks whether the current type is sustainable; if not, it downgrades and checks again. A cell at -4°C with Coniferous Forest: forest requires -3°C (fail) → Shrub requires -5°C (pass) → stays Shrub.

This interacts with the soil cascade — very high elevation already triggers Lithosol, which maps to Alpine Barren or Mountain Shrub. The treeline gate catches moderate-elevation cells that have forest-capable soil (e.g., podzol at high altitude) but are too cold for trees. Below -5°C, the permafrost gate handles things.

**Density falloff.** Even below treeline, vegetation density decreases as temperature drops toward the treeline threshold. Shorter growing season, wind exposure, and cold stress reduce growth:

```
elevationFactor = clamp((temp - treeline_temp) / treeline_fade_range, 0, 1)
```

Where `treeline_fade_range` (e.g., 13°C) is the temperature band over which density tapers. At `treeline_temp + treeline_fade_range` (e.g., 10°C), no penalty. At `treeline_temp` (-3°C), density reaches zero. Using temperature instead of raw elevation means the falloff naturally shifts with latitude — equatorial mountains lose density at higher altitudes than polar ones.

Typical density factors for boreal forest (temp -3°C to 5°C): at 0°C → 0.23 (sparse, forest-tundra transition), at 3°C → 0.46 (open boreal), at 5°C → 0.62 (moderate). Temperate forests (temp ≥ 5°C) reach full density by 10°C.

## Salinity Effects

Salinity is mostly baked into the soil cascade — Saline soil maps to Salt Flat (None) or Coastal Marsh (Grass). But cells near the saline threshold that fell through to another soil type can still have mildly salt-affected vegetation. For these, apply a density penalty proportional to `saltEffect`:

```
salinityFactor = 1 - 0.5 * saltEffect[c]
```

This creates a gradient: cells just inland of salt flats have visibly thinner vegetation than cells further away, even if they share the same soil type.

## Density Calculation

```
precipFactor = lerp(biome.densityMin, biome.densityMax, precipNorm)
density = precipFactor × elevationFactor × salinityFactor
```

Where `precipNorm` is the cell's precipitation normalized within the biome's precip range. Each biome's range is defined by its soil gate and biome selection thresholds:

| Biome               | Precip Min | Precip Max | Notes                               |
| ------------------- | ---------- | ---------- | ----------------------------------- |
| Glacier             | 0          | 100        | Density always 0, precip irrelevant |
| Tundra              | 0          | 100        | Narrow density range regardless     |
| Salt Flat           | 0          | 15         | Dry saline                          |
| Coastal Marsh       | 15         | 100        | Wet saline                          |
| Alpine Barren       | 0          | 100        | Density ≈ 0                         |
| Mountain Shrub      | 0          | 100        | Elevation-constrained, not precip   |
| Floodplain          | 0          | 100        | Alluvial — no precip gate on soil   |
| Wetland             | 0          | 100        | Alluvial — no precip gate on soil   |
| Hot Desert          | 0          | 15         | Arid, hot                           |
| Cold Desert         | 0          | 15         | Arid, cold                          |
| Scrubland           | 0          | 15         | Arid, moderate temp                 |
| Tropical Rainforest | 80         | 100        | Wettest laterite                    |
| Tropical Dry Forest | 70         | 80         | Mid laterite                        |
| Savanna             | 60         | 70         | Driest laterite                     |
| Boreal Forest       | 30         | 100        | Podzol, cold                        |
| Temperate Forest    | 30         | 100        | Podzol, warm                        |
| Grassland           | 15         | 45         | Dry chernozem                       |
| Woodland            | 45         | 100        | Wet chernozem                       |

`precipNorm = clamp((precip - precipMin) / (precipMax - precipMin), 0, 1)`

Density is stored per-cell and feeds into:

- Timber resource abundance (density × timber quality)
- Movement cost (see [Movement Cost](movement.md))
- Visual rendering (see below)

## Rendering: Two-Layer Compositing

Visuals are not a single biome color. Instead, two layers composite:

1. **Soil layer** (base) — color from soil type. Always visible. This is the ground.
2. **Vegetation layer** (overlay) — color from vegetation type, blended on top at vegetation density as opacity.

```
cellColor = lerp(soilColor[soilType], vegetationColor[vegType], vegetationDensity)
```

At density 0 (desert, glacier, salt flat, alpine barren) → pure soil color. At density 1 (dense rainforest) → pure vegetation color. In between, soil bleeds through — a sparse savanna shows laterite red-orange under gold-green grass. A woodland on chernozem looks different from a woodland on podzol because the dark soil peeks through the open canopy differently than gray-brown soil would.

This means two cells with the same biome but different geology are visually distinct. A floodplain on volcanic soil (dark, mineral-rich) looks different from a floodplain on limestone (pale). An aridisol desert on granite (gray) differs from one on sedimentary (sandy yellow).

**Soil colors** (base layer):

| Soil Type  | Color        |
| ---------- | ------------ |
| Permafrost | Blue-gray    |
| Saline     | White/pale   |
| Lithosol   | Rocky gray   |
| Alluvial   | Dark brown   |
| Aridisol   | Sandy yellow |
| Laterite   | Red-orange   |
| Podzol     | Gray-brown   |
| Chernozem  | Near-black   |

**Vegetation colors** (overlay, opacity = density):

| Vegetation        | Color            |
| ----------------- | ---------------- |
| None              | Transparent      |
| Lichen/Moss       | Muted gray-green |
| Grass             | Gold-green       |
| Shrub             | Dusty olive      |
| Deciduous Forest  | Green            |
| Coniferous Forest | Dark blue-green  |
| Broadleaf Forest  | Deep green       |

Vegetation feeds into resource placement — timber, wheat, and grazing derive from vegetation type and density. See [Resources](resources.md) for the full resource type list and production chains, and [Fauna](fauna.md) for animal-derived resources.
