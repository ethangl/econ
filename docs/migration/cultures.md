# Culture Generation

Cultures are placed before states. States grow from culture centers, inheriting the culture's "type" and expansion preferences.

## Culture Sets

Azgaar provides themed culture sets with pre-defined cultures:

| Set          | Theme                | Cultures                                       |
| ------------ | -------------------- | ---------------------------------------------- |
| European     | Medieval Europe      | Shwazen, Angshire, Luari, Norse, Elladan, etc. |
| Oriental     | East/Middle East     | Koryo, Hantzu, Yamoto, Eurabic, etc.           |
| English      | All-English variants | Multiple named from same base                  |
| Antique      | Classical era        | Roman, Hellenic, Celtic, Persian, etc.         |
| High Fantasy | Tolkien-style        | Elves, Dwarves, Orcs, Giants, Humans           |
| Dark Fantasy | Grimdark             | Mostly English + rare fantasy races            |
| Random       | Procedural           | Random name bases                              |
| All-World    | Global diversity     | Mix of all real-world cultures                 |

## Culture Properties

| Property       | Purpose                                      |
| -------------- | -------------------------------------------- |
| `name`         | Display name                                 |
| `base`         | Index into naming system (Markov chains)     |
| `odd`          | Probability weight for selection             |
| `sort`         | Scoring function for center placement        |
| `shield`       | Heraldry style (heater, round, banner, etc.) |
| `type`         | Playstyle (Naval, Nomadic, Highland, etc.)   |
| `expansionism` | Growth multiplier                            |
| `center`       | Origin cell                                  |
| `color`        | Map display color                            |

## Placement Algorithm

1. Select N cultures from set (weighted by `odd`)
2. For each culture:
   a. Sort populated cells by culture's `sort` function
   b. Pick from top candidates with spacing enforced (quadtree)
   c. Assign culture type based on terrain at center
   d. Calculate expansionism based on type

### Sorting Functions

Sorting functions combine terrain preferences:

```javascript
// Example: Norse culture prefers cold (temp ~5°C)
sort: (i) => n(i) / td(i, 5);

// Example: Elladan (Greek) prefers warm + highlands
sort: (i) => (n(i) / td(i, 18)) * h[i];

// Example: Angshire (English) prefers temperate + coast
sort: (i) => n(i) / td(i, 10) / sf(i);
```

Helpers:

- `n(i)` — normalized suitability score
- `td(i, goal)` — temperature difference penalty
- `bd(i, biomes)` — biome mismatch penalty
- `sf(i)` — seafront penalty (non-coastal)
- `h[i]` — height (for highland preference)
- `t[i]` — terrain type

## Culture Types

Assigned based on where culture center lands:

| Type     | Condition                      | Expansion Preference             |
| -------- | ------------------------------ | -------------------------------- |
| Nomadic  | Desert/steppe biome, not hilly | Open terrain, avoids forests     |
| Highland | Elevation > 50                 | Mountains, penalized on lowlands |
| Lake     | Adjacent to large lake         | Lake shores                      |
| Naval    | Coastal or island              | Coastlines, can cross water      |
| River    | On major river (flux > 100)    | Along rivers                     |
| Hunting  | Forest biome, inland           | Forests, penalized elsewhere     |
| Generic  | Default                        | Balanced                         |

## Expansionism Modifiers

Base expansionism varies by type:

| Type     | Base | Notes                       |
| -------- | ---- | --------------------------- |
| Naval    | 1.5× | Most expansive — sea access |
| Nomadic  | 1.5× | Mobile — open terrain       |
| Highland | 1.2× | Defensive but can grow      |
| Generic  | 1.0× | Baseline                    |
| River    | 0.9× | Linear, focused growth      |
| Lake     | 0.8× | Localized                   |
| Hunting  | 0.7× | Least expansive             |

Final value: `base × random(1, sizeVariety/2 + 1)`

## Expansion Algorithm

Same priority-queue flood fill as states:

```
1. Initialize queue with all culture centers
2. While queue not empty:
   a. Pop lowest-cost cell
   b. For each neighbor:
      - Calculate cost (biome, height, river, terrain)
      - If total < maxExpansionCost and improves, assign culture
      - Add to queue
```

### Cost Factors

| Factor       | Calculation                                                           |
| ------------ | --------------------------------------------------------------------- |
| Biome        | Native = 10, non-native = `biomeCost × 2-10` depending on type        |
| Biome change | +20 penalty when crossing biome boundary                              |
| Height       | Type-specific: Naval low water penalty, Highland high lowland penalty |
| River        | River types get bonus, others pay 20-100 based on flux                |
| Terrain type | Coastline/mainland penalties based on culture type                    |

**Key insight:** Culture expansion happens _before_ state generation. States inherit the culture of their capital, so the culture map establishes "zones" that states then formalize politically.

## Naming System

Each culture has a `base` index into `nameBases` — arrays of phoneme rules for Markov-style name generation:

- Base 0 = German-style
- Base 1 = English-style
- Base 7 = Greek-style
- Base 12 = Japanese-style
- Etc.

Names for burgs, states, provinces all use the dominant culture's naming base.

## Our Approach

**Do we need cultures?**

For economics, culture affects:

- State formation (states follow cultural lines)
- Naming (cosmetic)
- Trade friction? (could add cultural trade penalties)

### Options

**Option A: Skip cultures**

- Place states directly without culture layer
- Simpler, but states might feel arbitrary
- Naming becomes random or uniform

**Option B: Simplified cultures**

- N culture centers placed by terrain preference
- Flood-fill to create cultural regions
- States spawn within cultural regions
- Keeps naming diversity without full system

**Option C: Port Azgaar's system**

- Full culture sets with themed preferences
- Most authentic results
- More complexity to maintain

**Recommendation:** Start with Option B. We want states that feel culturally coherent without the full weight of fantasy culture sets. Key elements to keep:

- Culture types (Naval, Highland, etc.) — they drive interesting state shapes
- Naming bases — procedural names are better than random strings
- Expansion preferences — cultures should "fit" their terrain

**Simplification:** We don't need heraldry, shields, or detailed cultural origins. Focus on:

1. Terrain-aware placement
2. Type-based expansion costs
3. Naming base assignment
