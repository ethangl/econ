# Azgaar Shortcomings

Issues with Azgaar's approach that we want to address in our implementation.

## Inconsistent Map Scale

Maps with identical settings (resolution, point count, template) produce wildly different physical scales. An island might be 500km across in one generation and 1500km in the next.

**Root cause (verified in source):**

The `distanceScale` (km per pixel) is literally randomized on each map generation:

```javascript
// options.js:612
distanceScale = gauss(3, 1, 1, 5); // random 1-5, centered at 3
```

Physical size is simply `pixelWidth × distanceScale`, so a 1440px map could be 1440km to 7200km wide based purely on RNG.

**Disconnected parameters:**

| Parameter           | What it controls            | Connected to scale?                |
| ------------------- | --------------------------- | ---------------------------------- |
| `graphWidth/Height` | Pixel dimensions            | No                                 |
| `points`            | Cell count                  | No                                 |
| `template`          | Land shape/distribution     | No (except atoll has tiny mapSize) |
| `mapSize`           | "% of Earth" for globe view | No — separate concept entirely     |
| `distanceScale`     | km per pixel                | This IS the scale, but it's random |

An atoll template might generate 500 cells of land, roll `distanceScale=4.5`, and claim to be 6000km across — physically absurd.

**What we want:** Predictable, meaningful scale:

- Fixed scale derived from parameters (e.g., cell count → target area → scale)
- Or explicit scale parameter that constrains generation
- At minimum, consistent scale for same template type
- Template-appropriate defaults (atolls should be small, continents large)

This matters for economics because distances affect:

- Transport costs and travel times
- Market reach and trade zones
- Population density plausibility
- Resource distribution density

## Ocean Cell Culling

Ocean areas have drastically different (sparser) point distribution than land. This isn't a rendering artifact — deep ocean cells are deliberately excluded from the fine-detail mesh.

**Root cause (verified in source):**

The `reGraph()` function that creates pack cells from grid cells explicitly culls ocean:

```javascript
// main.js:1111-1112
if (height < 20 && type !== -1 && type !== -2) continue; // exclude ALL deep ocean
if (type === -2 && (i % 4 === 0 || ...)) continue;       // exclude 75% of lake cells
```

**Cell type values:**
| Type | Meaning | Fate in reGraph |
|------|---------|-----------------|
| `1` | Land coastline | Kept + extra midpoints added |
| `-1` | Water coastline | Kept |
| `-2` | Shallow water/lake | 75% culled |
| (none) | Deep ocean | 100% culled |

**Result:** The pack cell mesh has:

- High detail at coastlines (extra points inserted)
- Normal detail on land
- Almost no cells in deep ocean

**Why this matters for us:**

1. **Pathfinding gaps** — Sea routes need cells to pathfind through. Empty ocean means no sea travel representation.

2. **Ocean features** — Can't place ocean features (fishing grounds, sea lanes, islands) if there are no cells.

3. **Asymmetric mesh** — Land and sea have fundamentally different resolution, making consistent simulation harder.

**What we want:**

- Uniform cell distribution across entire map
- Or explicit ocean cell density parameter
- Sea cells should exist for naval pathfinding and ocean resources

## Flat Ocean Depth

Ocean depth is essentially fixed — anything below height 20 (out of 0-100) is "ocean" with no meaningful depth variation. The heightmap only models land topography; ocean is a flat floor.

**How it works:**

```javascript
// Height interpretation
height >= 20  → land (variable elevation)
height < 20   → water (all treated equally)
```

The heightmap generator (Hill, Range, etc.) operates on land. Ocean areas just get whatever low values remain after land generation — there's no deliberate modeling of:

- Continental shelves
- Ocean trenches
- Mid-ocean ridges
- Seamounts
- Depth gradients

**Related to ocean culling:** Since ocean has no meaningful height data AND cells are culled, the ocean is doubly featureless — no geometry AND no topology.

**Does this matter for economics?**

Possibly not critical, but ocean depth affects:

- Fishing zones (continental shelf vs deep ocean)
- Naval navigation (shallow water hazards, deep water ports)
- Underwater resources (if we ever model them)
- Realism of coastline generation (shallow seas vs steep drop-offs)

**What we might want:**

- Ocean depth as negative height values (0 = sea level, -100 = deep trench)
- Or separate ocean depth layer
- At minimum, continental shelf modeling for coastal fishing/ports

---

## Additional Shortcomings (from code analysis)

### Rivers Flow Through Cells, Not Along Edges

Rivers occupy cells rather than flowing along cell boundaries. This creates problems:

- Counties/provinces get bisected by rivers (historically unrealistic)
- Ambiguous transport model (is a river cell land or water for pathfinding?)
- Rivers can't naturally serve as borders without special-case code

Already documented in [rivers.md](./rivers.md) as a design change we want.

### No Explicit Resource Model

Resources are implicitly derived from biome habitability. There's no concept of:

- Mineral deposits (iron, gold, coal)
- Fertile vs. poor soil
- Timber density
- Fishing grounds

Everything economic is derived from the single "suitability" score, which conflates habitability with resource wealth.

**Impact:** Can't model resource-driven trade (grain from fertile plains, iron from mountains) without adding our own resource layer.

### Static Population

Population is calculated once during generation from suitability scores and never changes:

```javascript
pop = (suitability * cellArea) / meanArea;
```

No modeling of:

- Population growth over time
- Migration toward opportunities
- Urbanization dynamics
- Carrying capacity limits

**Impact:** For a dynamic economic simulation, we need population that responds to economic conditions.

### Boundaries Ignore Natural Features

State and province boundaries are pure flood-fill from capitals/burgs. They don't preferentially follow:

- Rivers (natural borders historically)
- Mountain ridges
- Coastlines

A state might expand across a mountain range into a disconnected valley rather than stopping at the ridge.

**Impact:** Less realistic political geography. With edge-based rivers, we get river borders for free, but mountain ridges would need explicit handling.

### Settlement Placement is Greedy

Capitals and towns are placed by sorting cells by suitability and placing greedily with spacing constraints. No consideration of:

- Network effects (towns along trade routes)
- Defensive positioning relative to neighbors
- Control of strategic chokepoints (mountain passes, river crossings)

**Impact:** Settlements end up in "good" locations but not necessarily "strategic" locations. A mountain pass might be empty while a nearby valley has the town.

### Precipitation Model is 1D

Wind blows in horizontal rows (east or west per latitude band), not realistic 2D atmospheric flow:

```javascript
// Wind is processed row-by-row
for each row:
  if westerly: blow moisture west-to-east
  if easterly: blow moisture east-to-west
```

No modeling of:

- Pressure systems
- Monsoons
- Wind deflection around mountains

**Impact:** Rain shadows only work for east-west mountain ranges. A north-south range won't create proper rain shadow effects.

### No Temporal/Historical Dimension

Everything is generated as a static snapshot. There's no simulation of:

- How states formed and expanded over time
- Historical borders that influence current culture
- Ruins of previous civilizations
- Established trade routes predating current political boundaries

**Note:** This is expected — Azgaar is a map generator, not a history simulator. Listed here only as context for what we might layer on top, not as a criticism of Azgaar.

### Template Scale Mismatch

The heightmap templates produce wildly different land areas at the same point count. 40k points on an isthmus vs. 40k points on a pangea results in:

| Template | Land Area | Cell Density on Land |
| -------- | --------- | -------------------- |
| Isthmus  | Small     | Very high (detailed) |
| Pangea   | Huge      | Very low (sparse)    |

The templates are designed for visual variety, not consistent simulation scale.

**Impact for economics:**

- Cell count affects simulation performance
- Want consistent "resolution" (cells per county, cells per market zone)
- An isthmus might have 50 cells per county; a pangea might have 3

**What we'll likely do:**

- Constrain to a subset of templates that make sense at similar scales
- Or: scale point count with expected land area per template
- Or: define templates by target land cell count, not total points

This connects to the scale inconsistency issue — templates need to specify not just shape but expected physical scale and cell density.

---

## Design Constraint: Fixed Cell Scale

For our implementation, **cell scale must be constant across all maps**.

**Why:** We want to support tessellated submaps — generating adjacent map regions that stitch together seamlessly. This requires:

- Consistent cell size (km² per cell)
- Matching cell boundaries at edges
- Same resolution everywhere

**Implication:** Templates define _shape and land ratio_, not cell density. A pangea template just means "generate more cells" than an island template, not "same cells spread thinner."

This is a fundamental departure from Azgaar's approach where point count is user-specified independent of template.

**See [overview.md](./overview.md#cell-scale) for the actual cell dimensions (2.5 km × 2.5 km).**
