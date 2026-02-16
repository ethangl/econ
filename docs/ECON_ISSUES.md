# Economic Simulation Issues

Snapshot from a 12-month run (seed=12345, 100k cells, Continents template).

## Summary Stats

| Metric              | Value                              |
| ------------------- | ---------------------------------- |
| Population          | 1,018,718                          |
| Working Age         | 680,345                            |
| Employed            | 440,954 (64.8%)                    |
| Counties            | 651                                |
| Markets             | 10 (9 legit + 1 black + 1 off-map) |
| Facilities          | 163,960                            |
| Total Market Supply | 613,982                            |
| Total Market Demand | 33,770                             |
| Supply:Demand Ratio | 18:1                               |

## Issue 1: Skilled Worker Starvation (Critical)

### Symptom

Consumer goods facilities get almost no workers while primary processors are fully staffed. After 12 months, prices are pegged at floor (0.1x) for intermediates and ceiling (10x) for consumer goods across every market.

**Workers concentrated in extraction/primary processing:**

| Facility     | Workers | Product    |
| ------------ | ------- | ---------- |
| lumber_camp  | 88,732  | timber     |
| barley_farm  | 74,360  | barley     |
| rye_farm     | 65,260  | rye        |
| farm (wheat) | 58,220  | wheat      |
| copper_mine  | 22,506  | copper_ore |
| sawmill      | 22,425  | lumber     |
| barley_mill  | 19,359  | flour      |
| rye_mill     | 17,106  | flour      |
| workshop     | 15,962  | furniture  |
| mill (wheat) | 15,477  | flour      |

**Consumer goods facilities starved:**

| Facility      | Count | Workers | Unmet Demand for Output  |
| ------------- | ----- | ------- | ------------------------ |
| bakery        | 9,146 | 136     | bread: 9,983             |
| tailor        | 6,453 | 0       | clothes: 5,094           |
| creamery      | 6,453 | 0       | cheese: 2,037            |
| cobbler       | 6,453 | 0       | shoes: 2,037             |
| spinning_mill | 6,453 | 0       | cloth (input to tailors) |
| smithy        | 875   | 0       | tools: 1,019             |
| jeweler       | 2,681 | 0       | jewelry: 204             |

**Facilities with zero workers that exist in large numbers:** shearing_shed (13,286), dairy (13,286), ranch (6,078), spinning_mill (6,453), cobbler (6,453), creamery (6,453), tailor (6,453).

### Root Cause

`ProductionSystem.Tick()` (line 67) iterates `economy.Facilities.Values` — a single global dictionary — to allocate workers. Allocation is greedy first-come-first-served with no prioritization. Dictionary insertion order determines who gets workers first.

`EconomyInitializer` creates facilities in this order:

1. Extraction facilities (all types, all counties)
2. Primary processors (mills, sawmills, tanneries, smelters)
3. Secondary processors (bakeries, workshops, cobblers, coppersmiths)
4. Tertiary processors (tailors)

Each facility calls `county.Population.AllocateWorkers()` which takes from a single per-county skilled pool until empty. Primary processors always iterate before secondaries and exhaust the pool.

**Trace for a typical 200-pop county with wheat + timber + hides:**

Skilled worker pool = 24 (20% of working commoners are Artisans).

| Phase     | Facility    | Count × Labor | Skilled Used | Remaining |
| --------- | ----------- | ------------- | ------------ | --------- |
| Primary   | mill        | 2 × 3         | 6            | 18        |
| Primary   | sawmill     | 2 × 3         | 6            | 12        |
| Primary   | tannery     | 2 × 3         | 6            | 6         |
| Primary   | barley_mill | 2 × 3         | 6            | **0**     |
| Secondary | bakery      | 2 × 2         | 0            | 0         |
| Secondary | workshop    | 2 × 4         | 0            | 0         |
| Secondary | cobbler     | 2 × 3         | 0            | 0         |

### Why workshops got 15,962 workers but bakeries got 136

Timber is in 555/651 counties. Many are timber-only (no grain, no hides). In those counties the only primary processor is sawmill (6 skilled), leaving 18 for workshops.

Bakeries are co-located with grain mills (by design — placed "where mills are"). In those same counties, mills have already exhausted the skilled pool.

### Contributing Factors

1. **No price/profit signal in allocation.** A mill grinding 0.1x-priced flour gets workers before a bakery producing 10x-priced bread.
2. **Over-placement of primaries.** `ComputeFacilityCount` independently scales each facility type by the same worker pool. A county with 3 grain types + timber + hides places 10+ primary types × 2 facilities each, needing 60+ skilled workers from a pool of 24.
3. **No reallocation over time.** The system resets and re-allocates every tick, but iteration order never changes, so the starvation pattern is permanent.

## Issue 2: Massive Intermediate Stockpile Accumulation

### Symptom

Intermediates pile up with no downstream consumer:

| Good      | Total Stockpile | Unmet Demand |
| --------- | --------------- | ------------ |
| flour     | 759,676         | —            |
| furniture | 77,551          | 275          |
| lumber    | 66,546          | —            |
| timber    | 27,354          | —            |
| leather   | 7,799           | —            |
| cookware  | 4,786           | 903          |

Flour is the worst: 760k units stockpiled, produced daily by 22k+ mill workers, but bakeries (the only consumer) have 136 workers total.

### Root Cause

Direct consequence of Issue 1. Primaries produce at full capacity, secondaries can't consume. No feedback mechanism throttles primary production when output isn't being consumed.

## Issue 3: Price Discovery Ineffective

### Symptom

Every good is pegged at either 0.1x (floor) or 10x (ceiling) base price. No good reaches equilibrium. The only exceptions are cookware and furniture in a few markets where some trade volume exists.

**All markets show identical pattern:**

| Category       | Price Ratio | Examples                                      |
| -------------- | ----------- | --------------------------------------------- |
| Raw materials  | 0.1x floor  | barley, rye, wheat, timber, hides, ores       |
| Intermediates  | 0.1x floor  | flour, lumber, leather, copper, iron, gold    |
| Consumer goods | 10x ceiling | bread, cheese, clothes, shoes, tools, jewelry |

### Root Cause

Price signals exist but nothing responds to them. There is no mechanism to:

- Reallocate workers toward higher-profit facilities
- Shut down overproducing facilities
- Open new facilities in response to shortages

## Issue 4: Furniture Overproduction

### Symptom

Furniture supply: 647,193 in black market alone, plus thousands per legitimate market. Total demand: ~275. Price crashed to 0.1x. Yet 15,962 workers keep producing.

### Root Cause

Workshops are the lucky secondary processor — co-located with sawmills in timber-only counties where no grain mills compete for skilled workers. They produce without constraint since there's no mechanism to reduce production when supply vastly exceeds demand.

## Issue 5: Black Market Anomalies

### Symptom

The black market has:

- 647,193 furniture (supply with ~91 demand)
- 965 cookware at 9.8x price with negative volume
- Tiny amounts of spices/sugar at price ceiling

Most goods show zero activity despite the black market being designed as a safety valve.

## Potential Fix Directions

### A. Proportional Allocation

Give each facility in a county a share of the skilled pool proportional to its labor need, rather than first-come-first-served. Ensures all facility types get some workers.

### B. Priority-Based Allocation

Sort facilities by expected profitability (output price / input cost) before allocating. Bakeries making 10x bread from 0.1x flour would rank highest.

### C. Two-Pass Allocation

First pass: give each facility minimum viable workers (e.g. 1 worker). Second pass: distribute remaining workers proportionally or by priority.

### D. Reduce Primary Processor Labor Type

Make some primary processors (mills, sawmills) use unskilled labor. Historically, milling was unskilled work. This would free the skilled pool for downstream manufacturing.

### E. Production Feedback

Throttle extraction/primary production when local stockpiles exceed a threshold. If flour stockpile > N days of bakery capacity, idle some mill workers.
