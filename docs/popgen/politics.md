# Political Hierarchy

Settlements emerge from the political pipeline rather than being placed individually. The pipeline derives everything from per-cell suitability and population scores computed by the biome pipeline.

## Pipeline

```
Heightmap → Rivers → Climate → Biomes → Suitability → Population → Political
                                                                      ├─ Landmasses
                                                                      ├─ Capitals → Realms
                                                                      ├─ Provinces
                                                                      └─ Counties (seats = settlements)
```

## Cell Suitability

Each land cell gets a composite suitability score (`SuitabilityOps`). This drives population, which drives all political grouping.

**Population:** `pop = suitability * cellArea / meanArea`

## Political Pipeline (`PoliticalOps`)

### 1. Landmass Detection

BFS connected components on land cells. Tiny islands (< 5 cells) are filtered out. Landmasses qualify for capitals only if they hold >= 2% of total map population.

### 2. Capital Placement

- **Count:** `max(qualifyingLandmasses, ceil(totalPop / PopPerRealm))`
- **Distribution:** at least 1 per qualifying landmass, rest proportional to population
- **Placement:** highest-suitability cells with minimum spacing per landmass
- Each capital seeds a realm

### 3. Realm Growth

Multi-source Dijkstra from all capitals weighted by `MovementCost`. Realms naturally conform to terrain — mountains and deserts form borders. Water/lakes are impassable.

### 4. Realm Normalization

Single-pass smoothing: flip cells where a supermajority of neighbors belong to a different realm. Capitals are never flipped.

### 5. Province Subdivision

Per realm: `max(2, ceil(realmPop / PopPerProvince))` provinces. Capital cell is always a province seed. Growth via Dijkstra within realm boundaries.

### 6. County Grouping

Per province:

1. **High-density cells** (pop >= threshold) become single-cell counties
2. **Flood fill** from highest-population seeds, growing outward concentrically (closest to seed first) until target population or max cell count reached
3. **Orphan cells** form counties from remaining highest-pop seeds

The seed cell of each county is its **county seat** — the primary settlement.

## County Seats as Settlements

There is no separate settlement placement step. County seats _are_ the settlements:

- They are the highest-population cell in their county
- Their location is driven by suitability (rivers, coasts, fertile land, low elevation)
- County grouping ensures they are spatially centered within their county

## Tuning Constants

| Constant               | Default | Purpose                                               |
| ---------------------- | ------- | ----------------------------------------------------- |
| MinLandmassSize        | 5       | Absolute floor for landmass filtering (noise removal) |
| MinLandmassPopFraction | 0.02    | Landmass needs 2% of total pop to get a capital       |
| PopPerRealm            | 200,000 | Population per realm capital                          |
| PopPerProvince         | 40,000  | Population per province within a realm                |
| HighDensityThreshold   | 20,000  | Single-cell county threshold                          |
| TargetCountyPop        | 5,000   | Target population for county growth                   |
| MaxCellsPerCounty      | 64      | Hard cap on county size                               |
| CapitalSpacingFactor   | 0.6     | Fraction of even-distribution spacing                 |
| ProvinceSpacingFactor  | 0.5     | Fraction of even-distribution spacing                 |

## Key Files

| File                                        | Purpose                          |
| ------------------------------------------- | -------------------------------- |
| `Assets/Scripts/Core/PoliticalData.cs`      | Data container (per-cell arrays) |
| `Assets/Scripts/Core/PoliticalOps.cs`       | All 6 algorithms + MinHeap       |
| `Assets/Scripts/PoliticalGenerator.cs`      | Unity MonoBehaviour orchestrator |
| `Assets/Editor/PoliticalGeneratorEditor.cs` | Inspector with stats             |
