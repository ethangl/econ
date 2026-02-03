# Climate & Biomes

Azgaar's climate model is a simplified but plausible approximation.

## Temperature

Based on latitude + altitude:

```
1. Map Y position → latitude (configurable: equator position, pole temps)
2. Latitude → sea-level temperature:
   - Tropical zone (16°N to 20°S): shallow gradient from equator
   - Temperate/polar: steeper gradient to poles
3. Altitude adjustment: -6.5°C per 1000m (realistic lapse rate)
```

```javascript
temp[cell] = seaLevelTemp(latitude) - altitudeDrop(height);
altitudeDrop = (height / 1000) * 6.5; // °C
```

## Precipitation

Wind-based model with rain shadows:

```
1. Define prevailing winds by latitude band:
   - Trade winds (easterly) in tropics
   - Westerlies in mid-latitudes
   - Polar easterlies at high latitudes

2. Wind carries moisture from ocean
   - Starts with base moisture at coast
   - Drops moisture when hitting elevation (orographic effect)
   - Rain shadow: dry on lee side of mountains

3. Latitude modifier (atmospheric circulation):
   - High at ITCZ (equator): rising air, wet
   - Low at subtropical high (~30°): sinking air, dry
   - Medium at mid-latitudes: variable
   - Low at poles: cold, dry
```

Key simplification: Wind is 1D (east-west or north-south per row), not true 2D flow.

## Biome Assignment

Simple 2D matrix lookup with special cases:

```
Input: moisture, temperature, height, hasRiver
Output: biomeId (0-12)

Special cases (checked first):
- height < 20 → Marine (0)
- temp < -5°C → Glacier (11)
- temp > 25°C && dry && no river → Hot Desert (1)
- very wet + right conditions → Wetland (12)

Otherwise: biomesMatrix[moistureBand][tempBand]
```

### Biomes Matrix

Moisture 0-4 rows × temperature 0-25 columns:

| Moisture     | Hot → Cold biomes                          |
| ------------ | ------------------------------------------ |
| Dry (0)      | Hot Desert → Cold Desert → Tundra          |
| Low (1)      | Savanna → Grassland → Taiga → Tundra       |
| Medium (2-3) | Tropical Forest → Temperate Forest → Taiga |
| Wet (4)      | Rainforest → Temperate Rainforest → Taiga  |

## Biome Properties

| Biome                | Habitability | Movement Cost |
| -------------------- | ------------ | ------------- |
| Marine               | 0            | 10            |
| Hot Desert           | 4            | 200           |
| Cold Desert          | 10           | 150           |
| Savanna              | 22           | 60            |
| Grassland            | 30           | 50            |
| Tropical Seasonal    | 50           | 70            |
| Temperate Deciduous  | 100          | 70            |
| Tropical Rainforest  | 80           | 80            |
| Temperate Rainforest | 90           | 90            |
| Taiga                | 12           | 200           |
| Tundra               | 4            | 1000          |
| Glacier              | 0            | 5000          |
| Wetland              | 12           | 150           |

**Habitability** drives population placement. **Movement cost** affects pathfinding.

## Our Approach

This system is straightforward to port:

1. **Temperature:** Latitude + altitude. We have both.
2. **Precipitation:** Wind model is the complexity. Could stub with uniform precipitation initially.
3. **Biomes:** Matrix lookup. Trivial.

**Stub approach:**

- Temperature: latitude bands + altitude drop
- Precipitation: distance from coast + random variation
- Biomes: matrix lookup

**Full approach:**

- Port wind model for rain shadows
- Mountains create realistic dry regions

For economics, habitability and movement cost matter most. The visual biome assignment is secondary.
