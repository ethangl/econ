# Economy V2 High-Cardinality Opportunities

This document evaluates high-cardinality bottlenecks in the current Economy V2 runtime and proposes abstractions to reduce daily simulation cost while preserving behavior.

## Anchor Benchmark

Generated on day 20 of an Economy V2 run with game defaults, prior to most optimization efforts.

- `unity/econ_debug_output_d20_bench.json`

## Compare Workflow

Use the compare script to evaluate a candidate run against the baseline:

```bash
scripts/compare_econ_dumps.sh unity/econ_debug_output_d20_bench.json unity/econ_debug_output.json
```

Optional third argument prints more top drift rows:

```bash
scripts/compare_econ_dumps.sh <bench.json> <candidate.json> 20
```

For repeated A/B runs, use the multi-run comparer to reduce incidental noise:

```bash
scripts/compare_econ_dump_sets.sh "unity/debug/econ/bench/*.json" "unity/debug/econ/candidate/*.json"
```

Optional third argument prints more top per-system drift rows:

```bash
scripts/compare_econ_dump_sets.sh "unity/debug/econ/bench/*.json" "unity/debug/econ/candidate/*.json" 12
```

Capture each finished run into a timestamped set first:

```bash
scripts/archive_econ_dump.sh bench
scripts/archive_econ_dump.sh candidate
```

The script reports:

- Summary deltas (population, facilities, supply/demand/volume, money metrics)
- Global and per-market order/lot cardinality deltas
- Tick timing deltas from the `performance` block (`avgTickMs`, `maxTickMs`, per-system `avgMs`)
- Largest market-good drifts and top price/volume movers

## Scope

- Focus on runtime tick cost (daily/weekly/monthly systems), memory pressure, and object/list cardinality.
- Startup-only costs are out of scope unless they materially affect large-map usability.
- Code areas reviewed:
- `src/EconSim.Core/Economy/EconomyInitializer.cs`
- `src/EconSim.Core/Economy/EconomyState.cs`
- `src/EconSim.Core/Economy/Facility.cs`
- `src/EconSim.Core/Economy/Stockpile.cs`
- `src/EconSim.Core/Economy/Market.cs`
- `src/EconSim.Core/Economy/MarketOrders.cs`
- `src/EconSim.Core/Simulation/Systems/ProductionSystem.cs`
- `src/EconSim.Core/Simulation/Systems/OrderSystem.cs`
- `src/EconSim.Core/Simulation/Systems/MarketSystem.cs`
- `src/EconSim.Core/Simulation/Systems/WageSystem.cs`
- `src/EconSim.Core/Simulation/Systems/LaborSystem.cs`
- `src/EconSim.Core/Simulation/Systems/MigrationSystem.cs`

## Current High-Cardinality Drivers

1. Facility instance explosion

- Initial placement creates many repeated facility instances per county/type using `for (j < count)` loops.
- One object per instance carries substantial mutable state (`Stockpile`, 3x 7-day arrays, treasury, wage, activation flags).
- Daily systems repeatedly scan all facilities.

2. Market order and lot explosion

- Orders are posted per county population and per active facility input.
- Consignments are posted as one lot per seller/good/day.
- Market clearing scans full inventory and order lists every day.

3. String-keyed inventory and good IDs in hot loops

- `Stockpile` uses `Dictionary<string,float>`.
- `BuyOrder` and `ConsignmentLot` store `string GoodId`.
- This adds hashing and allocation pressure across many tight loops.

4. Cell-level zone maps used where county-level data is sufficient

- Market zones store `ZoneCellIds` and `ZoneCellCosts`.
- Runtime economics mostly consume county-level assignments (`CountyToMarket`) and county seat costs.

5. Repeated per-day recomputation/allocation patterns

- Multiple systems rebuild temporary lists/maps each tick slice or tick pass.
- Labor and production paths perform repeated scans and sorting per county/facility group.

## Evaluation: County-Level Facility Aggregation (`Completed`)

The proposal to represent multiple same-type facilities as county-level aggregates is a strong first-order optimization.

### Why it fits the current model

- Economic ownership is already county-based (`Facility.CountyId`, `CountyEconomy.FacilityIds`).
- Processing facilities are already placed at county seats; extraction is effectively county-resource driven.
- Main daily costs are proportional to facility count, not county count.

### Recommended abstraction

Represent one runtime "facility cluster" per `(countyId, facilityType)` with a `UnitCount` (or capacity multiplier).

- Branch note: placement now creates one clustered facility actor per county/type with `UnitCount`; labor requirements and throughput scale by `UnitCount`, and bootstrap/labor/wage/production paths use the scaled values.

Cluster state should preserve:

- Labor demand and assignment
- Treasury and wage dynamics
- Input/output buffers
- Activation and loss/debt tracking

Derived values should scale by `UnitCount`:

- `LaborRequired`
- `BaseThroughput`
- Wage bill and profitability thresholds

### Expected impact

- If a large map currently has about 150k facilities and clusters reduce that to about 20k, most facility-driven daily loops drop by roughly 7x to 8x.
- Memory usage should drop materially by removing per-instance object overhead and duplicate buffers.

### Key caveat

Naively merging all facilities of a type can alter behavior if facilities are intentionally heterogeneous (different treasury history, debt state, partial activation cadence). The cluster model should be treated as a first-class economic actor, not just a visual grouping.

## Additional High-Cardinality Abstractions

### Priority A (high impact, moderate risk)

1. Aggregate market demand/supply books (`Completed`)

- Replace per-order/per-lot scanning with aggregated books per good, optionally with seller/buyer buckets.
- Keep FIFO only where gameplay requires it; otherwise clear against aggregate liquidity.
- Primary hotspots: `OrderSystem`, `MarketSystem`, `Market.PendingBuyOrders`, `Market.Inventory`.
- Branch note: market books are grouped by good (`PendingBuyOrdersByGood`, `InventoryLotsByGood`) and posting/clearing paths use the new APIs.

2. Dense good indexing (int IDs) for runtime paths (`Completed`)

- Add a runtime good index table and use `int` IDs in stockpiles/orders/lots.
- Keep string IDs at data boundaries only (data loading, debug dump, UI).
- Primary hotspots: `Stockpile`, `MarketOrders`, production/order/market loops.

3. County transport cost cache per market assignment epoch

- Precompute county seat cost to assigned market and reuse in production/order/wage paths.
- Invalidate when market zones are recomputed.
- Primary hotspots: repeated `ResolveTransportCost`/`ResolveCountyTransportCost` calls.

### Priority B (medium-high impact, lower risk)

1. Runtime county-level market zone representation

- Keep cell-level zone data only for rendering/debug tools.
- Runtime economics should consume county-zone costs directly.

2. Migration county-graph reachability

- Compute county adjacency/cost graph and run migration reachability on counties, not full cell graph.
- Preserve cell-graph pathfinding for rendering/road features where needed.

3. Allocation pooling and reuse in labor/market systems

- Reuse list/map buffers by county/market key to reduce daily GC churn.
- Avoid repeated short-lived `new List<>` in hot paths.

### Priority C (follow-up after structural changes)

1. Data-oriented facility state storage

- Move from many heap objects to compact arrays/struct-of-arrays for hot fields.
- Best done after cluster model stabilizes.

2. Adaptive cadence for low-activity actors

- Increase tick interval for persistently idle clusters/markets with bounded error.
- Requires clear gameplay tolerance for delayed response.

## Suggested Execution Order

1. Instrument cardinality and per-system tick times on a large map baseline.
   Status: `Completed`
2. Implement facility clustering by `(countyId, facilityType)` with behavior parity tests.
   Status: `Completed` (initial placement now emits clustered facilities with `UnitCount`; labor/wage/production/bootstrap flows use scaled labor and throughput.)
3. Introduce dense good indexing in `Stockpile` and market records.
   Status: `Completed`
4. Replace per-order/per-lot market clearing with aggregated books.
   Status: `Completed`
5. Shift runtime zone and migration logic to county-level representations.
   Status: `Not started`

## Acceptance Metrics

- Daily tick wall time reduced by at least 3x on large-map test seeds.
- Facility count reduction target: at least 70% with no systemic collapse (raw-input starvation, price runaway).
- No regression in determinism for fixed seed runs.
- Memory footprint reduced measurably at simulation day 30.
- Gameplay parity: similar unemployment range by estate/skill.
- Gameplay parity: similar basic-need fulfillment rates.
- Gameplay parity: similar market price trajectories for staple goods.

## Notes

- The county-level facility aggregation proposal is the highest-leverage first change and is compatible with the current county-centric economy model.
- Market aggregation and dense good IDs are the next largest multipliers once facility count is reduced.
