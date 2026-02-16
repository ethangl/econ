# Economy V2 Implementation Plan

Implements the market-mediated monetary economy defined in `ECONOMY_V2.md`. Each issue is independently merge-able behind a feature flag, with unit tests verifiable before integration.

## Strategy

- **Feature flag**: `SimulationConfig.UseEconomyV2` gates system registration. V1 continues to work untouched.
- **Additive first**: new systems are added alongside V1 systems. V1 systems are only removed from the registration path, not deleted, until V2 is stable.
- **Test-per-issue**: each issue includes unit tests for its core logic, runnable without the full simulation.
- **Determinism first**: seed plumbing and RNG ownership land before behavior changes so V2 runs are replayable.
- **Data model lands first**: all struct/field additions go in before any behavioral changes.

## Dependency Graph

```
Issue 0 (Deterministic Seed Hooks)
  └── Issue 1 (Data Model + Flag)
        ├── Issue 2 (MarketSystem)
        ├── Issue 3 (ProductionSystem V2)
        ├── Issue 4 (OrderSystem)
        ├── Issue 5 (WageSystem)
        ├── Issue 6 (PriceSystem)
        └── Issue 7 (LaborSystem)
              └── all of 2-7 ──► Issue 7.5 (Interim Integration Harness)
                                    └── Issue 8 (Bootstrap V2)
                                          └── Issue 9 (Integration + Cutover)
                                                └── Issue 10 (Telemetry + Debug)
```

Issues 2-7 depend on Issues 0-1 and can be developed in any order (or in parallel). Issue 7.5 gates Issue 8 so core systems can be tested together before bootstrap lands. Issues 8-10 are sequential.

---

## Issue 0: Deterministic Seed Hooks

Define deterministic RNG ownership and replay checks for V2 runs. No economy behavior changes.

### Changes

**RNG ownership and sources**

- Add a dedicated economy RNG stream (`SimulationState.EconomyRng`) initialized once at simulation startup.
- Seed source order:
  1. `SimulationConfig.EconomySeed` when explicitly set.
  2. Fallback to world/map seed when `EconomySeed` is unset.
- Prohibit direct randomness from `new Random()`, `Random.Shared`, or `UnityEngine.Random` inside economy systems.

**Seed plumbing**

- Thread the chosen economy seed through initialization so every V2 system uses the same deterministic RNG context.
- Store/log the effective economy seed in simulation metadata and debug output so replays can be reproduced exactly.
- Add deterministic tie-break helpers (seeded ordering/shuffle) for any logic that would otherwise depend on unstable collection iteration order.

**Replay acceptance criteria**

- Same map seed + same economy seed + same config must produce identical day-by-day economy fingerprints for at least 30 days.
- Fingerprint must include, at minimum: market prices, inventory/order counts, county treasuries, facility treasuries, assigned workers, `InputBuffer`, `OutputBuffer`, and county stockpile totals.
- Changing only economy seed should change at least one daily fingerprint within the first 7 days (unless the scenario has no stochastic branches).

### Tests

- Unit: fixed-seed economy RNG stream yields stable number sequences.
- Unit: deterministic tie-break helper returns stable ordering for fixed seed.
- Integration: 30-day replay equality test with identical seeds/config.
- Integration: seed sensitivity test with different `EconomySeed` values.

### Done When

- Effective economy seed is visible in debug/telemetry output.
- No economy system uses non-deterministic/global RNG sources.
- Replay tests pass for deterministic and seed-sensitivity cases.

---

## Issue 1: Data Model + Feature Flag

Add all V2 fields and structs. No behavioral changes.

### Changes

**`SimulationConfig.cs`** — add feature flag:

```csharp
public static bool UseEconomyV2 = false;
```

**`Facility.cs`** — add fields:

```csharp
float Treasury;
float WageRate;
float[] DailyRevenue;      // circular buffer, size 7
float[] DailyInputCost;    // circular buffer, size 7
float[] DailyWageBill;     // circular buffer, size 7
int ConsecutiveLossDays;
int GraceDaysRemaining;
int WageDebtDays;
```

Initialize arrays in constructor: `new float[7]`. Add helper to record today's values and read rolling averages:

```csharp
void RecordDay(int dayIndex, float revenue, float inputCost, float wageBill);
float RollingAvgRevenue { get; }
float RollingAvgInputCost { get; }
float RollingAvgWageBill { get; }
float RollingProfit { get; }  // revenue - inputCost - wageBill
```

`dayIndex = currentDay % 7` for circular indexing.

**`Population.cs`** (`CountyPopulation`) — add field:

```csharp
float Treasury;
```

**New file: `MarketOrders.cs`** (in `Economy/`):

```csharp
struct BuyOrder {
    int BuyerId;          // facility ID (positive) or county ID (negative)
    string GoodId;
    float Quantity;
    float MaxSpend;
    float TransportCost;  // buyer's county-to-market cost
    int DayPosted;
}

struct ConsignmentLot {
    int SellerId;         // facility ID (positive) or synthetic seed ID (negative)
    string GoodId;
    float Quantity;
    int DayListed;
}
```

**`Market.cs`** — add fields:

```csharp
List<BuyOrder> PendingBuyOrders = new();
List<ConsignmentLot> Inventory = new();
```

**`MarketGoodState`** — add field:

```csharp
float Revenue;  // gold collected from sales last clearing
```

**`InitialData.cs`** — tune for V2:

- `cheese`: NeedCategory.Basic (was Comfort)
- `clothes`: NeedCategory.Comfort (was Basic)
- Update BaseConsumption rates to match V2 spec:
  - bread: 0.5, cheese: 0.1, clothes: 0.003, shoes: 0.003
  - tools: 0.003, cookware: 0.003, furniture: 0.001, jewelry: 0.0003
  - spices: 0.01, sugar: 0.01

These rate changes are gated by the V2 flag in the registration path (V1 keeps current values).

### Done When

- All new fields compile, default to zero/empty.
- V1 path still works identically (no behavioral change).
- `SimulationConfig.UseEconomyV2` exists and defaults to `false`.

---

## Issue 2: MarketSystem

The core clearing engine. Replaces `TradeSystem` in V2 mode.

### New File: `Systems/MarketSystem.cs`

**Class**: `MarketSystem : ITickSystem`, `TickInterval = 1`, `Name = "Market"`

**`Tick` method** — for each market (skip Black in V2):

1. **Apply decay** to consignment inventory (per-lot, using `GoodDef.DecayRate`). Remove lots with quantity < 0.01.

2. **For each good**, collect eligible supply and demand:
   - Eligible sell lots: `Inventory` where `DayListed < currentDay` and `GoodId == good`
   - Eligible buy orders: `PendingBuyOrders` where `DayPosted < currentDay` and `GoodId == good`

3. **Clear**: posted-price proportional rationing.

   ```
   total_supply = sum of eligible lot quantities
   total_demand = sum of eligible order quantities (already affordability-capped by OrderSystem)
   traded = min(total_supply, total_demand)

   IF demand > supply (excess demand):
     each buyer gets: (their_quantity / total_demand) × traded
     all sellers sell everything
     record unmet demand

   IF supply >= demand (excess supply):
     all buyers get everything
     each seller sells: (their_supply / total_supply) × traded
     unsold lots remain (reduced quantity)
   ```

4. **FIFO lot resolution**: within the supply side, consume oldest lots first (sort by `DayListed` ascending, then by position in list for same-day).

5. **Money transfers**:
   - Buyer pays: `quantity_received × price × (1 + buyer_transport_cost × 0.005)` from buyer treasury
   - Buyer transport fee: `quantity_received × price × buyer_transport_cost × 0.005` → buyer's county population treasury
   - Seller receives: `quantity_sold × price` → seller treasury (facility) or hub county population (for synthetic seed sellers)

6. **Deliver goods**:
   - Facility buyers: goods → `InputBuffer`
   - Population buyers: goods consumed immediately (no stockpile intermediation)

7. **Remove filled buy orders**. Partially filled orders are removed (no carry-over — fresh orders posted each day by OrderSystem).

8. **Update `MarketGoodState`**: Supply (remaining inventory total), Demand, LastTradeVolume, Revenue.

### Transport Integration

Uses `Market.ZoneCellCosts` (already computed by MarketPlacer) to look up county-to-market cost. The county seat cell ID comes from `MapData.CountyById[countyId].SeatCellId`.

Seller transport (goods loss + hauling fee) is applied when lots are created (in ProductionSystem V2), not in MarketSystem. MarketSystem sees post-loss quantities.

### Tests

- **Proportional rationing**: 2 buyers want 60 and 40, supply is 50 → get 30 and 20.
- **FIFO**: lots listed on day 1 sell before lots listed on day 2.
- **One-day lag**: orders posted today don't clear today.
- **Money conservation**: sum of all treasury changes = 0 (no creation/destruction).
- **Transport fees**: buyer at cost=10 pays 5% markup, fee goes to county population.

### Done When

- Unit tests pass for clearing algorithm in isolation.
- MarketSystem processes manually-created orders/lots and produces correct allocations.

---

## Issue 3: ProductionSystem V2

Modifies the existing `ProductionSystem` with V2-flagged behavior.

### Changes to `ProductionSystem.cs`

Add a V2 code path (check `SimulationConfig.UseEconomyV2` in `Tick`).

**V2 Tick** — no longer allocates workers (LaborSystem does that weekly). Instead:

1. **Activation check** (before production, for each facility):

   **Active facilities** — deactivation:

   ```
   rolling_profit = facility.RollingProfit  // 7-day average
   if rolling_profit < 0:
     facility.ConsecutiveLossDays++
   else:
     facility.ConsecutiveLossDays = 0
   if GraceDaysRemaining > 0:
     GraceDaysRemaining--
   elif ConsecutiveLossDays >= 7:
     deactivate: IsActive = false, AssignedWorkers = 0
   ```

   **Idle facilities** — activation probe:

   ```
   available_workers = county.Population.IdleWorkers(facility.LaborType)
   if available_workers < def.LaborRequired × 0.5: skip

   county_transport_cost = market.ZoneCellCosts[countySeatCell]
   sell_efficiency = 1 / (1 + county_transport_cost × 0.01)
   hypo_revenue = market_price(output) × BaseThroughput × sell_efficiency
   hypo_sell_fee = BaseThroughput × market_price(output) × county_transport_cost × 0.005
   hypo_input_cost = sum(market_price(input) × qty × (1 + county_transport_cost × 0.005))
   hypo_wage_bill = subsistence_wage × labor_required
   hypo_profit = (hypo_revenue - hypo_sell_fee - hypo_input_cost - hypo_wage_bill) × 0.7

   if hypo_profit > 0: activate with 14-day grace period
   ```

2. **Extraction** (for each active extraction facility with workers):

   ```
   produced = throughput × abundance
   subsistence = produced × 0.20  → county.Stockpile
   for_market = produced × 0.80  → facility.OutputBuffer
   ```

3. **Processing** (for each active processing facility with workers):

   ```
   inputs = def.InputOverrides ?? goodDef.Inputs
   // Consume from InputBuffer (not county stockpile)
   max_batches = min(throughput, limited_by_inputs_in_InputBuffer)
   consume inputs from InputBuffer
   produce to OutputBuffer
   ```

4. **Post sell orders**: for each facility with OutputBuffer goods:

   ```
   for each good in OutputBuffer:
     county_transport_cost = ...
     efficiency = 1 / (1 + cost × 0.01)
     arrived = quantity × efficiency
     hauling_fee = quantity × market_price × cost × 0.005
     facility.Treasury -= hauling_fee  // pay upfront
     county.Population.Treasury += hauling_fee  // teamster income
     market.Inventory.Add(new ConsignmentLot(facility.Id, good, arrived, currentDay))
     OutputBuffer.Remove(good, quantity)
   ```

5. **Record daily metrics**: `facility.RecordDay(dayIndex, revenue=0, inputCost, wageBill=0)`. Revenue is recorded by MarketSystem when lots sell. WageBill is recorded by WageSystem.

### Key Differences from V1

- Workers are NOT reset/reallocated each tick (LaborSystem handles this weekly).
- Processing reads from `InputBuffer` instead of county `Stockpile`.
- Output goes to `OutputBuffer` → market consignment, not to county stockpile/export buffer.
- Subsistence fraction goes to county stockpile (only for extraction).
- Activation/deactivation based on profitability.

### Tests

- Extraction splits output 20/80 between stockpile and OutputBuffer.
- Processing consumes InputBuffer, not county stockpile.
- Deactivation after 7 consecutive loss days (skipping grace period).
- Activation probe: profitable facility activates with 14-day grace.
- Sell order posting applies transport loss correctly.

### Done When

- V2 production path works with pre-seeded InputBuffers.
- Subsistence fraction observable in county stockpile.
- OutputBuffer → ConsignmentLot flow works.

---

## Issue 4: OrderSystem

New system that posts buy orders for tomorrow's market clearing.

### New File: `Systems/OrderSystem.cs`

**Class**: `OrderSystem : ITickSystem`, `TickInterval = 1`, `Name = "Orders"`

**Tick** — for each county:

1. **Subsistence consumption** (before ordering):

   ```
   for each basic consumer good (bread, cheese):
     raw_need = BaseConsumption × population
     // Check stockpile for raw equivalents
     equivalent = sum(stockpile[grain] × conversion_rate for matching grains)
     subsistence_covered = min(raw_need, equivalent)
     // Deduct raw goods from stockpile proportionally
     market_need = max(0, raw_need - subsistence_covered)
   ```

   Conversion table (hardcoded constants):
   | Stockpile Good | Satisfies | Rate |
   |---|---|---|
   | wheat, rye, barley, rice_grain | bread | 0.5 |
   | goats | cheese | 0.3 |

2. **Population buy orders** — priority waterfall:

   ```
   budget = county.Population.Treasury

   // Tier 1: Basic (bread, cheese)
   basic_goods = consumer goods where NeedCategory == Basic
   total_basic_cost = sum(market_need[g] × effective_price[g] for g in basic_goods)
   budget_basic = min(budget, total_basic_cost)
   // Post orders for each basic good (pro-rata if budget < total_basic_cost)
   basic_spent = post_orders(basic_goods, budget_basic)

   // Tier 2: Comfort (clothes, shoes, tools, cookware)
   comfort_goods = consumer goods where NeedCategory == Comfort
   total_comfort_cost = sum(demand[g] × effective_price[g] for g in comfort_goods)
   budget_comfort = min(budget - basic_spent, total_comfort_cost)
   comfort_spent = post_orders(comfort_goods, budget_comfort)

   // Tier 3: Luxury (furniture, jewelry, spices, sugar)
   luxury_goods = consumer goods where NeedCategory == Luxury
   budget_luxury = budget - basic_spent - comfort_spent
   post_orders(luxury_goods, budget_luxury)
   ```

   `effective_price = price × (1 + buyer_transport_cost × 0.005)`

   Within a tier, if budget is insufficient, each good gets pro-rata share of available budget. Quantity is `budget_share / effective_price` (buy what you can afford).

3. **Facility input orders** — for each active processing facility:

   ```
   for each input good:
     needed = input.Quantity × def.BaseThroughput  // one day's worth
     have = facility.InputBuffer.Get(input.GoodId)
     to_buy = max(0, needed - have)
     effective_price = market_price × (1 + transport_cost × 0.005)
     max_spend = min(facility.Treasury, to_buy × effective_price)
     quantity = max_spend / effective_price

     market.PendingBuyOrders.Add(new BuyOrder {
       BuyerId = facility.Id,
       GoodId = input.GoodId,
       Quantity = quantity,
       MaxSpend = max_spend,
       TransportCost = county_transport_cost,
       DayPosted = currentDay
     })
   ```

### Tests

- Subsistence: county with wheat in stockpile reduces bread market demand.
- Priority waterfall: with limited budget, basics get funded first.
- Affordability: order quantity capped by treasury.
- Effective price includes transport markup.

### Done When

- OrderSystem creates correct buy orders with treasury caps.
- Subsistence deduction prevents double-counting.

---

## Issue 5: WageSystem

New system that handles facility wage payments.

### New File: `Systems/WageSystem.cs`

**Class**: `WageSystem : ITickSystem`, `TickInterval = 1`, `Name = "Wages"`

**Tick**:

1. **Update subsistence wage** (once per tick, shared across all facilities):

   ```
   basic_basket_cost = sum(market_price(good) × BaseConsumption for each Basic-need good)
   // Use average across all legitimate markets, weighted by zone population
   smoothed_basket = EMA_30day(basic_basket_cost)  // α = 2/(30+1) ≈ 0.0645
   raw_subsistence = smoothed_basket × 1.2
   subsistence_wage = clamp(raw_subsistence,
     yesterday_subsistence × 0.98,
     yesterday_subsistence × 1.02)
   ```

   Store `subsistence_wage` on `SimulationState` or as a static on WageSystem (reset each tick).

2. **For each active facility**:

   ```
   // Set wage rate from rolling margins
   margin = facility.RollingAvgRevenue - facility.RollingAvgInputCost
   if margin > 0:
     max_wage = margin / def.LaborRequired
     wage_rate = max(max_wage × 0.7, subsistence_wage)
   else:
     wage_rate = subsistence_wage
   facility.WageRate = wage_rate

   // Pay workers
   wage_bill = wage_rate × facility.AssignedWorkers
   paid = min(facility.Treasury, wage_bill)
   facility.Treasury -= paid
   county.Population.Treasury += paid

   // Track debt
   if paid < wage_bill:
     facility.WageDebtDays++
   else:
     facility.WageDebtDays = 0

   // Record daily wage bill for rolling average
   facility.DailyWageBill[currentDay % 7] = paid
   ```

3. **Distressed state**: facilities with `WageDebtDays >= 3` are flagged as distressed. LaborSystem uses this for immediate worker exit.

### Subsistence Wage Storage

Add to `SimulationState`:

```csharp
float SubsistenceWage;       // current subsistence wage
float SmoothedBasketCost;    // 30-day EMA of basic basket
```

### Tests

- Wage setting: profitable facility pays above subsistence.
- Wage setting: unprofitable facility pays subsistence from reserves.
- Subsistence EMA: basket cost spike takes many days to propagate.
- 2% daily cap: large basket change results in capped wage movement.
- Wage debt: 3 consecutive underpayment days → distressed.

### Done When

- Wages flow from facility treasury to population treasury.
- Subsistence wage tracks basket cost with damping.

---

## Issue 6: PriceSystem

New system that adjusts market prices after each day's clearing.

### New File: `Systems/PriceSystem.cs`

**Class**: `PriceSystem : ITickSystem`, `TickInterval = 1`, `Name = "Prices"`

**Tick** — for each legitimate market (skip OffMap, skip Black):

```
for each good:
  supply = marketGoodState.Supply  // remaining after clearing
  demand = marketGoodState.Demand  // total requested

  ratio = (demand + 0.1) / (supply + 0.1)
  adjustment = clamp(ratio - 1, -0.5, 0.5)
  price *= (1 + 0.1 × adjustment)
  price = clamp(price, 0.25 × base_price, 4 × base_price)
```

OffMap market prices stay fixed at `OffMapPriceMultiplier × base_price` (currently 2×). They are never adjusted.

### Changes to Existing Code

The current `TradeSystem.UpdateMarketPrices()` does price adjustment inline. In V2 mode, `TradeSystem` is not registered, and `PriceSystem` handles prices separately. No modification to `TradeSystem` needed — it's simply not registered.

### Tests

- Excess demand (ratio=2): price increases.
- Excess supply (ratio=0.5): price decreases.
- Price stays within [0.25×, 4×] bounds.
- OffMap prices remain unchanged across ticks.
- No activity: price stays put (0.1 epsilon prevents drift).

### Done When

- Prices move in response to supply/demand.
- Bounds enforced.
- OffMap prices immutable.

---

## Issue 7: LaborSystem

New weekly system that replaces the per-tick greedy worker allocation.

### New File: `Systems/LaborSystem.cs`

**Class**: `LaborSystem : ITickSystem`, `TickInterval = 7`, `Name = "Labor"`

**Tick** — for each county:

1. **Handle distressed facilities** (no friction):

   ```
   for each facility in county where WageDebtDays >= 3:
     facility.AssignedWorkers = 0  // all workers leave immediately
   ```

2. **Rank active, non-distressed facilities** by `WageRate` descending.

3. **Reconsideration pool** — 15% of employed workers reconsider:

   ```
   reconsidering_unskilled = (int)(employed_unskilled × 0.15)
   reconsidering_skilled = (int)(employed_skilled × 0.15)
   // Remove reconsidering workers from their current facilities
   // (proportional removal across all facilities of matching type)
   ```

   Plus all currently unemployed workers.

4. **Fill facilities** from the reconsideration + unemployed pool:

   ```
   for each facility (sorted by WageRate descending):
     if facility.WageRate < subsistence_wage: skip
     type = def.LaborType
     needed = def.LaborRequired - facility.AssignedWorkers
     available = idle pool of matching type
     allocated = min(needed, available)
     facility.AssignedWorkers += allocated
     // update employment tracking
   ```

5. **Switching threshold**: employed workers only switch if alternative pays >10% more than current.

### Key Differences from V1

- V1: resets ALL employment every tick, greedy first-come-first-served from dictionary iteration order.
- V2: weekly, persistent assignments, wage-ranked, friction-based.
- V1's `ProductionSystem` handles allocation. V2's `LaborSystem` is a separate system.
- V1's `CountyPopulation.ResetEmployment()` is NOT called in V2 (workers keep assignments).

### Tests

- Highest-wage facility fills first.
- 15% friction: most workers stay put.
- Distressed facility: 100% worker exit.
- Workers won't work below subsistence wage.
- 10% threshold: worker won't switch for a 5% raise.

### Done When

- Workers flow to highest-wage facilities.
- Distressed facilities reliably empty.
- Labor allocation is sticky between weeks.

---

## Issue 7.5: Interim Integration Harness

Add a fixture-based integration harness to validate Issues 2-7 together before bootstrap is implemented.

### New Test Harness Files

- `Tests/Fixtures/EconomyV2FixtureBuilder.cs`
- `Tests/Integration/EconomyV2InterimHarnessTests.cs`

### Harness Scope

- Build deterministic, minimal economies directly from fixtures (no bootstrap path) with:
  - seeded county/facility treasuries
  - seeded market `PendingBuyOrders`
  - seeded market `Inventory` consignment lots
  - seeded facility `InputBuffer` and `OutputBuffer`
  - fixed worker assignments and wage rates when needed
- Support table-driven fixture inputs so scenarios are concise and comparable across test runs.
- Use Issue 0 deterministic seed plumbing so harness runs are replayable.

### Required Scenarios

1. **Clearing + pricing loop**: orders/lots clear, prices adjust, no treasury violations.
2. **Production -> consignment -> market**: processing/extraction produce expected lots and trades across multiple days.
3. **Wages + labor friction**: wage debt produces distressed exits; weekly labor reallocation responds to wage ranking.
4. **Subsistence + order waterfall**: stockpile conversion reduces basic demand and preserves budget priority.
5. **OffMap invariants in V2 registration context**: OffMap pricing stays fixed while legitimate markets move.

### Acceptance Criteria

- A 14-day harness run executes Issues 2-7 in production tick order without bootstrap and without exceptions.
- Core invariants hold in every scenario:
  - no negative market inventory quantities
  - no negative treasuries except explicitly asserted wage-debt edge cases
  - price bounds remain within `[0.25x, 4x]` for legitimate markets
  - one-day lag rules (`DayPosted`, `DayListed`) are always respected
- Harness output includes compact per-day snapshots (price, treasury split, demand/supply/trade) to speed triage before Issue 8.

### Done When

- CI runs the interim harness suite independently of bootstrap tests.
- Regressions in Issues 2-7 can be diagnosed from harness snapshots without requiring full-world initialization.

---

## Issue 8: Bootstrap V2

Seeds the economy with money, inventory, and orders so tick 1 has real trade.

### Changes to `EconomyInitializer.cs`

Add V2 bootstrap path called after `PlaceInitialFacilities`:

```csharp
if (SimulationConfig.UseEconomyV2)
    BootstrapV2(economy, mapData);
```

**`BootstrapV2` method**:

1. **Compute initial subsistence wage**:

   ```
   basic_basket = sum(good.BasePrice × good.BaseConsumption for Basic goods)
   initial_subsistence_wage = basic_basket × 1.2
   ```

2. **Seed population treasuries**:

   ```
   for each county:
     daily_basic_cost = sum(good.BasePrice × good.BaseConsumption × population for Basic goods)
     county.Population.Treasury = daily_basic_cost × 30
   ```

3. **Seed facility treasuries**:

   ```
   for each facility:
     def = FacilityDefs.Get(facility.TypeId)
     goodDef = Goods.Get(def.OutputGoodId)
     inputs = def.InputOverrides ?? goodDef.Inputs ?? empty
     weekly_input_cost = sum(input.Quantity × Goods.Get(input.GoodId).BasePrice) × 7 × def.BaseThroughput
     weekly_wage_bill = def.LaborRequired × initial_subsistence_wage × 7
     facility.Treasury = weekly_input_cost + weekly_wage_bill
     facility.WageRate = initial_subsistence_wage
   ```

4. **Seed market inventory** (consignment lots):

   ```
   for each legitimate market:
     synthetic_seller_id = -100000 - market.Id
     for each good with local production potential:
       // Estimate weekly demand from zone population
       weekly_demand = sum(BaseConsumption × county_pop for counties in zone) × 7
       market.Inventory.Add(new ConsignmentLot {
         SellerId = synthetic_seller_id,
         GoodId = good.Id,
         Quantity = weekly_demand × 2,
         DayListed = 0  // eligible on day 1
       })
   ```

   Revenue from seed lot sales → hub county population treasury.

5. **Seed facility input buffers**:

   ```
   for each processing facility:
     for each input:
       facility.InputBuffer.Add(input.GoodId, input.Quantity × def.BaseThroughput × 3)
   ```

6. **Prefill day-1 buy orders** (DayPosted = 0):
   ```
   for each market:
     for each processing facility in zone:
       post input buy orders (same logic as OrderSystem, with DayPosted = 0)
     for each county in zone:
       post population buy orders for basic needs (DayPosted = 0)
   ```

### Tests

- After bootstrap, every county population has Treasury > 0.
- Every facility has Treasury > 0.
- Every market has inventory for locally-produced goods.
- Day-1 prefill orders exist with DayPosted = 0.
- Processing facilities have 3 days of input stock.

### Done When

- Tick 1 produces non-zero trade volume.
- No entity starts with zero treasury (unless zero population).

---

## Issue 9: Integration + Cutover

Wire everything together and enable V2.

### Changes to `SimulationRunner.cs`

Replace the hardcoded system registration with a branching path:

```csharp
if (SimulationConfig.UseEconomyV2)
{
    // V2 systems in tick order
    RegisterSystem(new MarketSystem());
    RegisterSystem(new ProductionSystem());  // V2 path internally
    RegisterSystem(new OrderSystem());
    RegisterSystem(new WageSystem());
    RegisterSystem(new PriceSystem());
    RegisterSystem(new LaborSystem());
    RegisterSystem(new OffMapSupplySystem());  // simplified for V2
    RegisterSystem(new MigrationSystem());
}
else
{
    // V1 systems (unchanged)
    RegisterSystem(new ProductionSystem());
    RegisterSystem(new ConsumptionSystem());
    RegisterSystem(new OffMapSupplySystem());
    RegisterSystem(new TradeSystem());
    RegisterSystem(new TheftSystem());
    RegisterSystem(new MigrationSystem());
}
```

### V2 Behavioral Changes in Existing Systems

**`ProductionSystem`**: already branched internally (Issue 3). Key difference: does NOT call `ResetEmployment()` or `AllocateWorkers()` in V2.

**`OffMapSupplySystem`**: in V2, creates `ConsignmentLot` entries instead of directly setting `MarketGoodState.Supply`. Prices remain fixed at 2× base (PriceSystem skips OffMap).

**`MigrationSystem`**: no changes needed. Treasury doesn't migrate with population (simplification for now — revisit if needed).

### Black Market

In V2 mode, `InitializeBlackMarket()` is NOT called. No `Market` with `Type = Black` exists. `TheftSystem` is not registered.

### CountyEconomy.ExportBuffer

In V2, `ExportBuffer` is unused (output goes through facility OutputBuffer → market). Don't remove the field yet — V1 still uses it. Just don't populate it in V2 mode.

### Bootstrap Cache

Increment `BootstrapCacheVersion` to force re-generation (V2 markets don't include Black market).

### Verification Checklist

Run with `UseEconomyV2 = true`, seed 12345, 10k cells, Continents template:

- [ ] Tick 1 has trade volume > 0
- [ ] No `NullReferenceException` or `KeyNotFoundException` through 30 days
- [ ] Money supply (sum of all treasuries) is stable ±5% over 30 days
- [ ] Bread unmet demand decreases from day 1 to day 30
- [ ] No market price at 0 or infinity
- [ ] All prices within [0.25×, 4×] bounds
- [ ] At least 50% of facilities active after day 7
- [ ] Worker allocation changes weekly (not daily)

### Done When

- V2 simulation runs 30+ days without crashes.
- V1 still works identically when flag is off.

---

## Issue 10: Telemetry + Debug

Instrumentation for monitoring and tuning.

### New: Economy Telemetry

Add to `SimulationState` or new `EconomyTelemetry` class:

```csharp
float TotalMoneySupply;          // sum of all pop + facility treasuries
float MoneyInPopulation;         // sum of pop treasuries
float MoneyInFacilities;         // sum of facility treasuries
float MoneyVelocity;             // total daily spend / total money supply
int ActiveFacilityCount;
int IdleFacilityCount;
int DistressedFacilityCount;
Dictionary<string, GoodTelemetry> GoodMetrics;  // per-good snapshot
```

```csharp
struct GoodTelemetry {
    float AvgPrice;        // average across legitimate markets
    float TotalSupply;
    float TotalDemand;
    float TotalTradeVolume;
    float UnmetDemand;
}
```

Compute at end of each tick (after all systems run). Could be a lightweight `TelemetrySystem` registered last, or computed inline in `SimulationRunner.ProcessTick()`.

### Update `EconDebugBridge.cs`

Extend dump format for V2:

- **Summary**: add money supply, velocity, pop/facility treasury split, subsistence wage.
- **Markets**: add consignment lot count, pending order count per market.
- **Counties**: add population treasury, per-facility treasury + wage rate + active/distressed status.
- **Facilities**: new section with top-N facilities by revenue, plus distressed list.

### Tests

- Money conservation: `TotalMoneySupply` at tick N equals initial seed minus gold consumed by jewelers plus gold mined.
- No negative treasuries (except documented edge cases during wage debt).

### Done When

- Single debug dump explains the state of any county/facility.
- Money supply is trackable across a 90-day run.

---

## Stability Targets

After a 90-day run on a standard Continents map (seed 12345, 10k cells):

| Metric             | Target                                       |
| ------------------ | -------------------------------------------- |
| Price bounds       | All prices within [0.25×, 4×] at all times   |
| Facility health    | < 25% distressed after month 2               |
| Money supply       | No sustained contraction (> 5% over 30 days) |
| Bread satisfaction | > 80% of demand met by month 3               |
| Worker utilization | > 60% of working-age employed                |
| Money velocity     | 0.05 - 0.5 range (healthy circulation)       |

---

## File Summary

### New Files

| File                                 | Issue |
| ------------------------------------ | ----- |
| `Economy/MarketOrders.cs`            | 1     |
| `Simulation/Systems/MarketSystem.cs` | 2     |
| `Simulation/Systems/OrderSystem.cs`  | 4     |
| `Simulation/Systems/WageSystem.cs`   | 5     |
| `Simulation/Systems/PriceSystem.cs`  | 6     |
| `Simulation/Systems/LaborSystem.cs`  | 7     |
| `Tests/Fixtures/EconomyV2FixtureBuilder.cs` | 7.5   |
| `Tests/Integration/EconomyV2InterimHarnessTests.cs` | 7.5   |

### Modified Files

| File                             | Issues | Changes                               |
| -------------------------------- | ------ | ------------------------------------- |
| `SimulationConfig.cs`            | 0, 1   | Add `EconomySeed` controls + `UseEconomyV2` flag |
| `Facility.cs`                    | 1      | Treasury, wage, tracking fields       |
| `Population.cs`                  | 1      | Treasury field                        |
| `Market.cs`                      | 1      | PendingBuyOrders, Inventory lists     |
| `MarketGoodState` (in Market.cs) | 1      | Revenue field                         |
| `InitialData.cs`                 | 1      | NeedCategory + BaseConsumption tuning |
| `ProductionSystem.cs`            | 3      | V2 code path                          |
| `EconomyInitializer.cs`          | 8      | V2 bootstrap                          |
| `SimulationRunner.cs`            | 0, 9   | Economy seed initialization + V2 system registration |
| `SimulationState.cs`             | 0, 5, 10 | EconomyRng/seed metadata, subsistence wage, telemetry |
| `OffMapSupplySystem.cs`          | 9      | ConsignmentLot creation               |
| `EconDebugBridge.cs`             | 0, 10  | Seed metadata + V2 dump format        |

### Untouched V1 Files (kept for V1 mode)

- `TradeSystem.cs`
- `ConsumptionSystem.cs`
- `TheftSystem.cs`
- `CountyEconomy.cs` (ExportBuffer stays, unused in V2)
