# Economy V2 Implementation Plan

## Objective

Implement the market-mediated monetary economy defined in `/Users/ethan/w/econ/docs/ECONOMY_V2.md` with minimal regressions and clear verification checkpoints.

## Delivery Strategy

Build in vertical slices so each phase is runnable and inspectable in isolation:

1. Data model and bootstrap foundations
2. Market clearing and transport accounting
3. Production, ordering, wages, labor, and prices
4. Telemetry, balancing, and hardening

## Phase 0: Baseline and Safety Rails

- Add feature flag for Economy V2 system registration path.
- Keep current economy path intact until Phase 3 is complete.
- Add deterministic seed hooks for reproducible economic runs.

Done when:

- V1 and V2 can be selected at startup/config without code edits.

## Phase 1: Data Model Changes

- Add treasury fields:
  - `CountyPopulation.Treasury`
  - `Facility.Treasury`, `Facility.WageRate`
- Add facility tracking:
  - `DailyRevenue[7]`, `DailyInputCost[7]`, `DailyWageBill[7]`
  - `ConsecutiveLossDays`, `GraceDaysRemaining`, `WageDebtDays`
- Add market storage:
  - `List<BuyOrder> PendingBuyOrders`
  - `List<ConsignmentLot> Inventory`
- Define synthetic seller ID constants (reserved negative range).

Done when:

- Save/load (if applicable) supports all new fields.
- Null/empty initialization is safe for new lists and buffers.

## Phase 2: Bootstrap Pipeline

- Seed population/facility treasuries.
- Seed market inventory as consignment lots owned by synthetic market seed seller.
- Seed facility input buffers.
- Prefill day-1 buy orders (`DayPosted = 0`) to avoid dead first tick.

Done when:

- Tick 1 executes non-zero trade volume.
- Seed inventory sales route revenue to hub county population.

## Phase 3: MarketSystem Core

- Implement one-day lag gate:
  - clear buys only if `DayPosted < currentDay`
  - sell from lots only if `DayListed < currentDay`
- Implement posted-price clearing with proportional rationing.
- Implement FIFO lot resolution for consignment sales.
- Implement transport accounting:
  - seller loss + seller fee at ship-time
  - buyer fee at purchase-time
  - fees transferred to county population treasuries
- Apply decay on consignment inventory.
- Update `MarketGoodState` metrics each tick.

Done when:

- Money is conserved except documented sinks (gold->jewelry, decay value loss).
- No lot can sell before its eligible day.

## Phase 4: ProductionSystem and Subsistence

- Extraction: retain subsistence fraction in county stockpile, list remainder.
- Processing: consume `InputBuffer`, produce to `OutputBuffer`.
- Active facility deactivation rule via realized 7-day rolling profit.
- Idle facility activation probe with:
  - labor availability threshold
  - transport-adjusted hypothetical costs/revenue
  - friction haircut and grace period
- Implement subsistence conversion from stockpile to basic-food equivalent before market demand posting.

Done when:

- No double-counting between stockpile subsistence and market basic demand.
- Multi-day production chain latency is observable (wheat->flour->bread).

## Phase 5: OrderSystem

- Replace old consumption ordering path with unified `OrderSystem`.
- Post facility input orders with treasury-capped `MaxSpend`.
- Post population buy orders:
  - demand = daily base consumption minus subsistence coverage
  - strict budget waterfall (basic -> comfort -> luxury)
  - affordability at effective buyer price (includes buyer transport fee)

Done when:

- Orders never exceed available buyer treasury.
- Demand curves remain smooth under partial affordability.

## Phase 6: WageSystem and LaborSystem

- Wage setting from rolling realized margins.
- Subsistence wage from 30-day EMA basic basket with 2% daily clamp.
- Wage debt tracking and distressed-state transitions.
- Weekly labor reallocation with friction.
- Distressed facility worker exit at next labor tick with no friction.

Done when:

- Distressed facilities reliably shed workers and deactivate.
- Wage dynamics do not oscillate violently from single-day price shocks.

## Phase 7: PriceSystem and Off-Map Rules

- Apply price update only to legitimate markets.
- Keep OffMap prices fixed at `2x base`.
- Use bounded range `0.25x` to `4x` base.

Done when:

- OffMap goods remain fixed-price across ticks.
- Legitimate market prices move gradually and stay within bounds.

## Phase 8: System Registration and Cutover

- Register systems in final order:
  1. `MarketSystem`
  2. `ProductionSystem`
  3. `OrderSystem`
  4. `WageSystem`
  5. `PriceSystem`
  6. `LaborSystem` (weekly)
  7. `MigrationSystem` (monthly)
- Remove or bypass obsolete V1 economy systems in V2 mode.

Done when:

- No duplicate processing path runs in V2.

## Phase 9: Telemetry and Debug Output

- Add per-tick telemetry:
  - total money supply
  - money split (population vs facilities)
  - money velocity
  - per-good price/supply/demand/trade volume
  - distressed facility counts and idle/active counts
- Update debug bridge dump format for V2 fields.

Done when:

- A single debug snapshot can explain why a county/facility is failing or prospering.

## Test Plan

- Unit tests:
  - affordability capping with effective buyer price
  - FIFO lot consumption and seller payout routing
  - one-day lag enforcement (`DayPosted`, `DayListed`)
  - subsistence conversion and stockpile deduction
  - wage clamp and EMA behavior
- Integration tests:
  - day-1 prefill produces trade
  - 30-day run maintains non-negative treasuries except documented distressed cases
  - no money creation/destruction outside documented mechanics
  - off-map prices remain fixed while legitimate prices move

## Rollout

1. Land Phases 1-3 behind V2 flag.
2. Land Phases 4-7 and run balancing pass.
3. Land Phases 8-9, enable V2 in test scene.
4. Compare 90-day telemetry runs against expected stability targets.

## Stability Targets (Initial)

- Price bounds respected at all times.
- Distressed facilities < 25% after month 2 (unless severe map scarcity).
- No sustained money-supply collapse in normal maps.
- Bread unmet demand trends downward after bootstrap period.
