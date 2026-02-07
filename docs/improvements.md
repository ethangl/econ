# Future Improvements

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

---

### Settlement Placement is Greedy

Capitals and towns are placed by sorting cells by suitability and placing greedily with spacing constraints. No consideration of:

- Network effects (towns along trade routes)
- Defensive positioning relative to neighbors
- Control of strategic chokepoints (mountain passes, river crossings)

**Impact:** Settlements end up in "good" locations but not necessarily "strategic" locations. A mountain pass might be empty while a nearby valley has the town.

---

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

---

### No Temporal/Historical Dimension

Everything is generated as a static snapshot. There's no simulation of:

- How realms formed and expanded over time
- Historical borders that influence current culture
- Ruins of previous civilizations
- Established trade routes predating current political boundaries

---

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
