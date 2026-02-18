# Economy Observations

Running log of observed behavior at each layer. Validates design assumptions against real simulation runs.

## Layer 1: Autarky

**Run: 12 months, 651 counties**

- Avg productivity 1.03, range 0.57–1.34
- 360 surplus counties (productivity > 1.0), 291 starving (productivity < 1.0)
- Steady state reached immediately — surplus counties accumulate linearly, starving counties sit at zero stock with constant unmet need
- Economy system: ~0.16ms/tick

## Layer 2: Feudal Tax Redistribution

**Run: 12 months, 433 counties, 31 provinces, 5 realms**

### Starvation reduction

| Model       | Starving | %   | Unmet Need/day |
| ----------- | -------- | --- | -------------- |
| Autarky     | 208      | 48% | 42,934         |
| Duke only   | 114      | 26% | 27,339         |
| Duke + King | 66       | 15% | 12,126         |

72% reduction in unmet need vs autarky. King tier nearly halved starving count vs duke-only.

### Steady state (reached by ~day 90)

- Production = consumption (657,858 ≈ 657,858) — all available food consumed
- Ducal tax/tick = ducal relief/tick (30,809) — dukes distribute everything they collect
- Royal stockpile = 0 for all realms — kings fully distribute each tick
- Provincial stockpiles stabilize at ~55K total (small buffer in 8 surplus provinces)
- Stock accumulation: ~620K total (surplus counties with nowhere to send excess)

### Why 66 counties still starve

Structural deficit: total daily need (669,984) exceeds total production (657,858) by ~12K goods/day. No redistribution can close this gap — the map simply doesn't produce enough. The 66 starving counties have the lowest biome productivity and their realm's surplus is exhausted.

### Duke-only model failed — why the king tier was needed

Without the king, surplus provinces hoarded indefinitely. Province 22 (all-surplus, no deficit counties) collected 3,279/day and distributed zero. Provincial stockpiles ballooned to 5.2M by day 361. The king fixes this by skimming 20% from surplus provinces and routing it to deficit provinces in the same realm.

### Price-driven trade was tried first and abandoned

Initial Layer 2 used per-county prices with neighbor-to-neighbor trade (price gradient flow). Problems:

- Prices ratcheted to ceiling (avg 7.0) because price update saw post-consumption stock, not post-trade stock
- 73% of counties pinned at price >5.0
- Got starvation down to 45 (10%) — better raw number but the price signal was broken

Replaced with feudal redistribution because: (a) historically accurate for intra-realm flows — lords don't use prices, they use administrative fiat; (b) simpler and more stable; (c) prices belong at inter-realm markets (Layer 4).

### Performance

- Economy: 0.017ms/tick
- Trade (4-phase): 0.031ms/tick
- Combined: 0.051ms/tick — well under 1ms budget
