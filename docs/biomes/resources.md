# Resources

Resource placement bridges the biome pipeline and the economy. Most resources are derived from vegetation and fauna (already computed in earlier stages). Geological resources (ores) are the only resources placed independently.

## Resource Types

### Organic (Biome-Derived)

These are computed during the biome pipeline and stored in BiomeData. See the linked docs for placement details.

| Resource   | Source                           | Placement                                                                 | Defined In                  |
| ---------- | -------------------------------- | ------------------------------------------------------------------------- | --------------------------- |
| Wheat      | Grass vegetation on fertile soil | Grass biomes (Grassland, Savanna, Floodplain); abundance = soil fertility | [Vegetation](vegetation.md) |
| Timber     | Forest vegetation                | Deciduous, Coniferous, Broadleaf Forest; abundance = density × quality    | [Vegetation](vegetation.md) |
| Fish       | Water adjacency                  | Rivers, coast, lakes; scales with flux                                    | [Fauna](fauna.md)           |
| Game Meat  | Game fauna                       | Forest, grassland, savanna biomes                                         | [Fauna](fauna.md)           |
| Hides      | Game fauna (byproduct)           | Same as game meat                                                         | [Fauna](fauna.md)           |
| Pelts/Furs | Fur animal fauna                 | Cold forests, tundra; cold bonus                                          | [Fauna](fauna.md)           |
| Feathers   | Waterfowl fauna                  | Wetland, coastal marsh, floodplain                                        | [Fauna](fauna.md)           |
| Salt       | Saline soil + ocean proximity    | SaltFlat, CoastalMarsh biomes; abundance = salt effect                    |                             |

### Geological (Independent Placement)

Ores are placed using Perlin noise fields independent of the biome pipeline. Each ore type uses its own noise field (different seeds) so deposits are spatially independent — a gold deposit doesn't imply iron nearby, and vice versa.

| Resource | Placement Rule                                            |
| -------- | --------------------------------------------------------- |
| Iron Ore | Height > 50; iron noise > iron threshold; moderate rarity |
| Gold Ore | Height > 50; gold noise > gold threshold; high rarity     |
| Lead Ore | Height > 50; lead noise > lead threshold; moderate rarity |
| Stone    | Rock type + slope; granite/limestone best; Lithosol bonus |

See [Geological Placement](#geological-placement) below.

## Production Chains

```
Wheat     → Flour     → Bread         (Basic need)
Iron Ore  → Iron      → Tools         (Comfort need)
Gold Ore  → Gold      → Jewelry       (Luxury need)
Lead Ore  → Lead      → (various)     (Comfort need)
Salt      →                          (Basic need, preservative)
Stone     → Cut Stone → (building)    (Basic need)
Timber    → Lumber    → Furniture     (Luxury need)
Fish      → Dried Fish               (Basic need, preservable)
Game Meat → Cured Meat               (Comfort need)
Hides     → Leather   → Leather Goods (Comfort need)
Pelts     → Fur Goods                (Luxury need, cold climate export)
Feathers  →                          (Luxury need, low volume)
```

These map to `GoodDef` entries in `InitialData.cs`. Raw goods have a `HarvestMethod` (farming, mining, logging, hunting, fishing) that determines which facility type extracts them.

## Geological Placement

Each ore type gets its own Perlin noise field, seeded independently from each other, the heightmap, and rock type noise. This means iron and gold deposits are spatially uncorrelated — some regions have both, some have one, some have neither.

**Parameters per ore type:**

| Parameter       | Iron   | Gold   | Lead   | Notes                        |
| --------------- | ------ | ------ | ------ | ---------------------------- |
| Noise seed      | unique | unique | unique | Independent spatial patterns |
| Noise frequency | Low    | Low    | Low    | Large deposit regions        |
| Threshold       | 0.6    | 0.7    | 0.65   | Higher = sparser             |
| Height gate     | 50     | 50     | 50     | Foothills and above          |

Gold has a higher threshold than iron (sparser), and its independent noise field means gold-only deposits are common — a highland region might have gold but no iron, or vice versa.

**Abundance** within a deposit scales with how far above the threshold the noise value is:

```
if oreNoise[type] > threshold[type] AND height > heightGate:
    abundance = (oreNoise[type] - threshold[type]) / (1 - threshold[type])
```

This produces a 0-1 abundance that peaks at the center of a deposit and tapers at edges.

**Rock type interaction.** Volcanic and granite rock types could boost ore likelihood (igneous geology = more mineralization). Optional modifier: multiply ore noise by a rock type weight before thresholding.

## Integration with Suitability

Resources feed into settlement suitability scoring (see [settlements](../migration/settlements.md)):

```
suitability = habitability
            + resourceDiversityBonus
```

Note: habitability already includes a river adjacency bonus (see [biomes.md](biomes.md#river-adjacency-bonus)), so river access is not double-counted here.

A cell with multiple resource types (grassland near a river with fishing, adjacent to ore deposits) scores higher than one with just grassland. This creates natural settlement clusters: river valleys with arable land, mountain mining towns, coastal fishing villages.

## Future Extensions

Resources to add later (not in the current implementation):

- **Horses** — military/transport; Grassland, Savanna biomes
- **Spices/Dyes** — luxury trade goods; Tropical Rainforest, Tropical Dry Forest
- **Clay** — pottery/bricks; Alluvial soil (Floodplain, Wetland)

The system should be data-driven: adding a new resource means adding a `GoodDef` + placement rule, not changing the algorithm.
