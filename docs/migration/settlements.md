# Settlement Placement

Azgaar places settlements (burgs) based on cell suitability scores.

## Cell Suitability (rankCells)

Each cell gets a suitability score based on:

```javascript
score =
  biomeHabitability + // base: 0-100
  riverBonus(flux, confluence) - // up to +250 for major rivers
  elevationPenalty(height) + // high = bad
  coastalBonus(harbor, estuary, lake); // ports valued
```

### Scoring Factors

| Factor             | Score    | Notes                                   |
| ------------------ | -------- | --------------------------------------- |
| Biome habitability | 0-100    | Grassland=30, TempForest=100, Glacier=0 |
| Major river        | +250 max | Normalized by map's flux range          |
| Confluence         | bonus    | Rivers meeting = valuable               |
| Low elevation      | +10 max  | `-(height - 50) / 5`                    |
| High elevation     | -10 max  | Mountains penalized                     |
| Estuary            | +15      | River mouth at coast                    |
| Ocean coast        | +5       | Any coastal cell                        |
| Safe harbor        | +20      | Single adjacent water cell              |
| Freshwater lake    | +30      | Lake access                             |
| Salt lake          | +10      | Less valuable than fresh                |

**Population:** `pop = suitability * cellArea / meanArea`

## Capital Placement

1. Sort cells by `suitability * random(0.5, 1.0)`
2. Place N capitals with minimum spacing (quadtree enforced)
3. If can't fit, reduce spacing and retry
4. Each capital seeds a state

## Town Placement

1. Sort cells by `suitability * gauss(1, 3)`
2. Place towns with smaller minimum spacing
3. Gaussian randomization creates clustering
4. Skip cells that already have a burg

## Port Detection

A burg becomes a port if:

- Capital with any harbor, OR
- Any burg with safe harbor (exactly 1 adjacent water cell)

Ports get shifted toward water edge for rendering.

## Burg Properties

After placement, burgs get detailed properties:

| Property   | Calculation                                                  |
| ---------- | ------------------------------------------------------------ |
| Population | `suitability / 5 * capitalBonus * connectivityRate * random` |
| Type       | Naval, Lake, Highland, River, Nomadic, Hunting, Generic      |
| Citadel    | Probability based on population                              |
| Walls      | Probability based on population                              |
| Plaza      | Has road + probability                                       |
| Temple     | Based on religion + population                               |

## Our Approach

The suitability scoring is the core insight. We can simplify or expand:

**Minimum viable:**

```
suitability = habitability + riverBonus + coastBonus
```

**Our additions for economics:**

- Resource proximity (mines, fertile land)
- Trade route potential (between markets)
- Defensibility (for capitals)

## Key Difference from Azgaar

- Azgaar places burgs, then grows states from them
- We have counties as the primary unit, seats are derived from grouping

**Our CountyGrouper already handles this:**

- Uses `cell.Population` for seeding and growth
- Seed cell (highest pop or burg) becomes the seat
- No separate "settlement placement" step needed

**The dependency:** CountyGrouper assumes `cell.Population` is pre-populated with sensible values. Currently this comes from Azgaar's export, which bakes in their suitability scoring (rivers, coasts, etc.).

**When we generate our own maps, we need:**

```
Heightmap → Rivers → Climate → Biomes → RankCells (suitability) → Population
                                              ↓
                                        CountyGrouper works as-is
```

So we don't need to port Azgaar's burg placement logic. We need to port their `rankCells()` suitability scoring to feed our existing CountyGrouper.
