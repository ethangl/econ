# Political Boundaries

Azgaar generates states first, then subdivides into provinces.

## State Generation

**Creation:** Each capital burg becomes a state. States grow outward from capitals.

**Expansion algorithm:** Priority queue (Dijkstra-style) with culture-aware costs.

```
1. Initialize: each capital cell has cost 1, add to queue
2. While queue not empty:
   a. Pop lowest-cost cell
   b. For each neighbor:
      - If locked state, skip
      - If another state's capital, skip
      - Calculate expansion cost
      - If cost < existing, assign state and enqueue
3. Normalize pass: smooth jagged edges
```

### Cost Function

```
cellCost = cultureCost + populationCost + biomeCost + heightCost + riverCost + typeCost
totalCost = previousCost + 10 + cellCost / expansionism
```

| Factor        | Calculation                        | Notes                                 |
| ------------- | ---------------------------------- | ------------------------------------- |
| Culture match | -9 if same culture, +100 otherwise | Strong pull toward own culture        |
| Population    | 0-20 based on `cells.s[e]`         | Low pop = higher cost                 |
| Biome         | `biomesData.cost[biome]`           | Native biome = 10                     |
| Height        | 0-2200 based on elevation          | Mountains = 2200, hills = 300         |
| River         | 20-100 based on flux               | Rivers impede (except River cultures) |
| Type          | 0-100 based on terrain type        | Coastline, inland, etc.               |

### Culture Type Modifiers

States have a culture "type" that affects expansion preferences:

| Type     | Preference             | Penalty                        |
| -------- | ---------------------- | ------------------------------ |
| Naval    | Low sea crossing (300) | Inland (+100)                  |
| Lake     | Lake crossing (10)     | —                              |
| River    | Rivers (+0)            | No rivers (+100)               |
| Highland | Mountains (0)          | Lowlands (+1100)               |
| Nomadic  | —                      | Forests (3× cost), sea (10000) |
| Hunting  | —                      | Non-native biome (2× cost)     |

### Expansionism

Each state gets a random `expansionism` value (1 to sizeVariety). Higher = grows farther. Cost is divided by expansionism, so expansive states tolerate higher costs.

**Growth limit:** `growthRate = (cellCount / 2) * globalGrowthRate * statesGrowthRate`

Expansion stops when totalCost exceeds growthRate.

### Normalize Pass

Smooths boundaries by flipping isolated cells:

```
for each cell:
  if cell has a burg, skip
  count neighbors in same state vs other states
  if adversaries > 2 and buddies <= 2:
    flip cell to majority neighbor's state
```

### Color Assignment

Greedy graph coloring. Each state gets a color different from all neighbors. Base palette of 6 colors, randomized variants for duplicates.

## Province Generation

**Purpose:** Administrative subdivisions within states.

**Creation:** Each state's burgs become province centers, sorted by population (capitals first).

**Count:** `provincesNumber = max(stateBurgs * provincesRatio / 100, 2)`

### Expansion Algorithm

Same priority queue approach, but simpler cost function:

```
elevation cost:
  height >= 70: 100 (mountains)
  height >= 50: 30  (hills)
  height >= 20: 10  (land)
  water (h < 20): 100 if coastal, skip if deep ocean

totalCost = previousCost + elevationCost
growth stops when totalCost > maxGrowth
```

**Key difference from states:** Provinces don't cross state borders. Each state's provinces expand independently.

### Shape Justification

Same normalize pass as states — smooth jagged edges.

### Wild Provinces

Cells without a province after initial expansion get assigned to "wild" provinces:

1. Find orphan cells (in state, no province)
2. Seed from burg if present, else first orphan
3. Expand with relaxed costs
4. Named as colonies, islands, or territories

**Colony detection:** If province is separated from state core (no land path within state), it's a colony. Gets names like "New [StateName]" or "New [ProvinceName]".

## Province Forms

Province names depend on state government type:

| State Form | Province Forms                                                   |
| ---------- | ---------------------------------------------------------------- |
| Monarchy   | County (22), Earldom (6), Shire (2), Barony (2), Margrave (2)... |
| Republic   | Province (6), Department (2), Governorate (2), District (1)...   |
| Theocracy  | Parish (3), Deanery (1)                                          |
| Union      | Province, State, Canton, Republic, County, Council               |
| Anarchy    | Council, Commune, Community, Tribe                               |
| Wild       | Territory (10), Land (5), Region (2), Tribe, Clan...             |

Numbers are weights for random selection.

## Our Approach

We have several options:

**Option 1: Port Azgaar's system**

- Implement flood-fill expansion from capitals
- Use culture-aware costs
- Subdivide into provinces

**Option 2: Voronoi-based**

- Voronoi cells from capital positions = state boundaries
- Snap to terrain features (rivers, ridges)
- Simpler, but less organic

**Option 3: Historical simulation**

- Start with many small states
- Simulate conquest, inheritance, federation
- Emergent borders from simulated history

**Recommendation:** Start with Option 1 (port Azgaar). It produces good results with known algorithms. Option 3 is interesting for long-term but much more complex.

### Edge-Based Rivers as Borders

If we adopt edge-based rivers as discussed in [rivers.md](./rivers.md), they become natural province/state borders automatically. The flood-fill expansion naturally stops at river edges (can't cross to neighbor if edge is a major river). This is historically realistic — rivers have always been administrative boundaries.

## Integration with Our Systems

Our current pipeline:

```
States (Azgaar) → Provinces (Azgaar) → CountyGrouper (ours) → Markets (ours)
```

After migration:

```
Capitals (suitability) → States (expansion) → Provinces (subdivision) → CountyGrouper → Markets
```

Counties are _within_ provinces (our grouper respects province boundaries). So province generation must happen before county grouping.

### Simplification Opportunity

Azgaar's provinces are essentially what our counties are — administrative subdivisions seeded from population centers. We might merge these concepts:

```
States (expansion) → Counties (our grouper, but within state boundaries)
                          ↓
                   "Provinces" = groups of counties (optional)
```

This would mean: generate states, then run CountyGrouper within each state. Province as an intermediate layer becomes optional grouping of counties.
