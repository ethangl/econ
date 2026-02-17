# Economy V2: Market-Mediated Commodity Economy

## Goals

Replace the supply-push barter system with a demand-driven monetary economy where:

1. All goods flow through markets (with a small local subsistence allowance)
2. Production chains have temporal depth (wheat today → flour tomorrow → bread the day after)
3. Prices emerge from supply and demand, and facilities respond to price signals
4. Workers earn wages and move toward higher-paying facilities
5. Crowns are the unit of account, minted from mined gold

## Core Principles

- **Demand drives supply.** Population needs create market demand. High demand raises prices. High prices make production profitable. Profitable facilities attract workers. More workers increase supply. Supply meets demand. Prices stabilize.
- **Money circulates.** Facilities pay wages → population spends at markets → markets pay facilities for goods → facilities pay wages. Gold mining is the only source of new money.
- **Time matters.** Each production step takes a day. A facility can only use inputs it acquired yesterday. Multi-stage chains take multiple days to flow through.
- **Markets intermediate.** Facilities sell output to markets. Facilities and population buy from markets. No direct facility-to-facility transfers (except local subsistence).

## Production Chains (Reference)

```
FOOD
  wheat/rye/barley → mill → flour → bakery → bread
  rice_grain → rice_mill → bread (direct)
  goats → dairy → milk → creamery → cheese

TEXTILES
  sheep → shearing_shed → wool → spinning_mill → cloth → tailor → clothes
  hides → tannery → leather → cobbler → shoes

METALS & TOOLS
  iron_ore → smelter → iron → smithy → tools
  copper_ore → copper_smelter → copper → coppersmith → cookware

WOOD
  timber → sawmill → lumber → workshop → furniture

LUXURY
  gold_ore → refinery → gold → jeweler → jewelry
  spice_plants → spice_house → spices
  sugarcane → sugar_press → cane_juice → sugar_refinery → sugar
```

## Money

### Unit

Crown. One Crown is defined as `0.001 kg` of gold (1 gram). Derived from the gold production chain: gold_ore → refinery → gold. All prices are denominated in `Crowns/kg`.

### Initial Endowment

The simulation bootstraps with money already in circulation:

- **Population**: each person starts with enough Crowns to cover ~30 days of basic consumption at base prices
- **Facilities**: each starts with operating capital for ~7 days (input costs + wages) in Crowns
- **Markets**: start with initial inventory at base prices (small buffer to prevent day-1 stockouts)

These represent a pre-existing economy. The simulation models ongoing dynamics, not the founding of civilization.

### Money Supply

- **Source**: gold mining → refining → Crown-denominated money entering circulation
- **Sinks**: gold consumed by jewelers (luxury vs monetary tension)
- **No central bank.** Money supply is determined by geology and gold facility activity.

Transport costs are **not** a monetary sink. The monetary component of transport is a **transfer** from trader to the origin county's population (abstracting teamsters/haulers). Money stays in circulation.

### Gold as Good and Money

Gold has dual nature: it's a tradeable refined good and the monetary backing for Crowns (`1 Crown = 0.001 kg gold`). The jeweler consumes gold to make jewelry (removing monetary backing from circulation). Gold miners add monetary backing. This creates natural tension between monetary and luxury uses.

**Deflation risk**: if jewelers consume gold faster than miners produce it, the money supply contracts. Safeguards:

- Jewelry is annual/luxury consumption — tiny demand relative to gold output
- Jewelers are few (only in gold-producing regions) and subject to the same profitability checks as other facilities
- If gold becomes scarce (deflation), gold's purchasing power rises, making gold mining more profitable, attracting more miners — self-correcting
- **Monitoring**: track per-tick telemetry:
  - Total money supply (sum of all population + facility treasuries)
  - Money distribution: % in population vs % in facilities
  - Money velocity: total daily spend / total money supply
  - If sustained contraction or velocity drop observed, tune gold mine throughput or jewelry consumption rate

## Data Model

### New: Treasury

Added to `CountyPopulation` and `Facility`:

```csharp
// On CountyPopulation
float Treasury;  // Crowns held by population of this county

// On Facility
float Treasury;       // operating capital (Crowns)
float WageRate;       // Crowns per worker per day
bool IsActive;        // true if facility is operating (existing field, new semantics)
```

### Modified: GoodDef

No new enum needed. Consumption is always daily. The `BaseConsumption` rate already encodes how frequently a good is needed — bread has a high daily rate, furniture has a tiny one.

```csharp
// BaseConsumption (existing field) — units per person per day
// Examples:
//   bread:     0.5    (half a loaf per day)
//   cheese:    0.1    (a bit each day)
//   clothes:   0.003  (~1 per year)
//   shoes:     0.003  (~1 per year)
//   tools:     0.003  (~1 per year)
//   cookware:  0.003  (~1 per year)
//   furniture: 0.001  (~1 every 3 years)
//   jewelry:   0.0003 (~1 per decade)
//   spices:    0.01   (a pinch per day)
//   sugar:     0.01   (a pinch per day)
```

This eliminates the bulk-purchasing problem: no need for household inventory, no demand spikes on "purchase day." Population buys and consumes a small amount every day. Market sees smooth, predictable demand.

Non-consumer goods (raw, refined) have `BaseConsumption = 0` — they're facility inputs, not population needs.

### Modified: MarketGoodState

```csharp
float Supply;           // units available for sale
float Demand;           // units wanted by buyers
float Price;            // current posted price (Crowns/kg)
float BasePrice;        // reference price (Crowns/kg)
float LastTradeVolume;  // units actually traded last clearing
float Revenue;          // Crowns collected from sales last clearing
```

### Modified: CountyEconomy

```csharp
// Remove: ExportBuffer (all output goes to market via sell orders)
// Keep: Stockpile (local subsistence goods only)
// Keep: UnmetDemand (now drives price signals)
// Keep: Resources, FacilityIds
```

### New: Market Orders and Consignment

```csharp
struct BuyOrder {
    int BuyerId;          // facility ID or county ID (negative = county)
    string GoodId;
    float Quantity;
    float MaxSpend;       // treasury cap for this purchase
    float TransportCost;  // buyer's distance to market
    int DayPosted;        // enforces one-day lag: only cleared on DayPosted + 1
}

struct ConsignmentLot {
    int SellerId;         // facility ID, or synthetic seed seller ID (reserved negative range)
    string GoodId;
    float Quantity;
    int DayListed;        // for FIFO resolution and lag enforcement
}
```

These live on `Market`:

```csharp
// On Market
List<BuyOrder> PendingBuyOrders;           // posted by OrderSystem, cleared next day
List<ConsignmentLot> Inventory;            // goods available for sale (FIFO ordered)
```

**One-day lag enforcement**:

- Buy orders clear only when `DayPosted < currentDay` (posted yesterday or earlier).
- Consignment lots are sell-eligible only when `DayListed < currentDay` (listed yesterday or earlier).
- Orders/lots created today wait until tomorrow.

### New: Facility Tracking Fields

```csharp
// On Facility (in addition to Treasury, WageRate, IsActive)
float[] DailyRevenue;     // circular buffer, last 7 days
float[] DailyInputCost;   // circular buffer, last 7 days
float[] DailyWageBill;    // circular buffer, last 7 days
int ConsecutiveLossDays;  // for deactivation check
int GraceDaysRemaining;   // countdown from 14 after activation
int WageDebtDays;         // for distressed state (3+ = distressed)
```

### Unchanged

- `EconomyState` structure (registries, lookups, markets, roads)
- `County`, `Cell`, map data
- `TransportGraph`, `RoadState`
- `MarketPlacer`, market zone assignment

## Tick Architecture

One tick = one day. The core invariant: **output produced today is not available on the market until tomorrow.** This creates the temporal pipeline where wheat→flour→bread takes 3 days.

### Day Boundary Model

Each facility has two buffers:

- `InputBuffer`: goods acquired from yesterday's market clearing, ready to use today
- `OutputBuffer`: goods produced today, will be listed for sale tomorrow

The one-day lag comes from the ordering: market clearing delivers yesterday's purchases into InputBuffer _before_ production runs. Production output goes to OutputBuffer, which becomes tomorrow's sell orders.

### Tick Order (definitive)

```
1. MarketSystem      — clear yesterday's orders, deliver goods and money
2. ProductionSystem  — produce using today's inputs, post sell orders for tomorrow
3. OrderSystem       — facilities and population post buy orders for tomorrow
4. WageSystem        — facilities pay workers from treasury
5. PriceSystem       — adjust prices based on today's clearing results
6. LaborSystem       — workers reconsider employment (weekly only)
7. MigrationSystem   — population movement (monthly only)
```

### Pipeline Example

```
Day 1: Farm produces wheat → OutputBuffer
Day 2: MarketSystem sells wheat → Mill's InputBuffer
        Mill produces flour → OutputBuffer
Day 3: MarketSystem sells flour → Bakery's InputBuffer
        Bakery produces bread → OutputBuffer
Day 4: MarketSystem sells bread → Population consumes
```

### 1. MarketSystem (daily, runs first)

Clears all orders posted during the previous tick:

1. Resolve sell orders: move goods from sellers' OutputBuffers to market
2. Resolve buy orders: match buyers to available supply (see clearing algorithm)
3. Transfer goods to buyers' InputBuffers (facilities) or directly consumed (population)
4. Transfer money: buyers pay, sellers receive, transport fees to origin county population
5. Record clearing results (volume, revenue, unmet demand)

### 2. ProductionSystem (daily)

For each **active** facility with workers:

1. **Extraction**: produce from terrain resources, no inputs needed
   - Subsistence: retain 20% in county stockpile (see Subsistence Model below)
   - Remaining 80% → facility OutputBuffer (for tomorrow's market)
2. **Processing**: consume inputs from InputBuffer, produce to OutputBuffer
   - If InputBuffer has insufficient inputs, produce at reduced throughput (or not at all)
   - Throughput = `BaseThroughput × Efficiency(workers/required)`

**Activation check** (runs before production):

Active facilities use **realized** profit. Idle facilities use **hypothetical** profit (since they have no realized data).

```
IF active:
  rolling_profit = 7-day rolling average of (actual_revenue - actual_input_cost - actual_wage_bill)
  if rolling_profit < 0 for 7 consecutive days: deactivate

IF idle:
  // Hypothetical probe: "would this facility be profitable at current market prices?"
  // First, check if workers are available
  available = county.IdleWorkers(facility.LaborType)
  if available < facility.LaborRequired × 0.5: skip (not enough labor to bother)

  sell_efficiency = 1 / (1 + county_transport_cost × 0.01)  // goods loss on output
  hypothetical_revenue = market_price(output) × BaseThroughput × sell_efficiency
  hypothetical_sell_fee = BaseThroughput × market_price(output) × county_transport_cost × 0.005
  hypothetical_input_cost = sum(market_price(input) × input_qty × (1 + county_transport_cost × 0.005))  // incl. buyer transport on inputs
  hypothetical_wage_bill = subsistence_wage × labor_required
  hypothetical_profit = hypothetical_revenue - hypothetical_sell_fee - hypothetical_input_cost - hypothetical_wage_bill
  hypothetical_profit *= 0.7  // 30% haircut for rationing, partial fills, and other real-world friction

  if hypothetical_profit > 0: activate (with 14-day grace period before deactivation check applies)
```

Active facilities use backward-looking realized data — this accounts for transport losses, market rationing, and inventory effects. Idle facilities use a forward-looking price probe with a conservative friction discount — necessary because they have no trade history. The labor availability check prevents churn where facilities activate, fail to hire, and immediately idle again. The grace period gives a reactivated facility time to build real trade flow before judging it.

### 3. OrderSystem (daily)

Collects buy orders for tomorrow's market clearing:

**Facility input orders**: each active processing facility calculates input needs for tomorrow's production and posts buy orders. `max_spend` capped by facility treasury.

**Population consumption orders**: for each consumer good, each county posts a buy order:

```
raw_need = BaseConsumption × county_population
subsistence_covered = SubsistenceEquivalent(good, county.Stockpile)  // see Subsistence Model
market_need = max(0, raw_need - subsistence_covered)
quantity = market_need
max_spend = treasury allocation for this good (see purchasing priority)
```

Population consumes subsistence goods from stockpile first (free, no market transaction), then buys any remaining need from the market. This prevents double-counting.

**Facility sell orders** are implicit — anything in a facility's OutputBuffer is listed at the current market price.

### 4. WageSystem (daily)

For each active facility:

1. Pay workers: `wage_rate × assigned_workers` from facility treasury → county population treasury
2. If facility treasury < wage bill: pay what's available, remainder is wage debt
3. Accumulated wage debt for 3+ days → facility enters **distressed** state

### 5. PriceSystem (daily)

For each good at each **legitimate** market (skip OffMap markets — their prices are fixed at 2× base):

```
ratio = (demand + ε) / (supply + ε)       // ε = 0.1 to avoid division by zero
adjustment = clamp(ratio - 1, -0.5, 0.5)  // cap max single-day change
price *= (1 + adjustment_rate × adjustment)
price = clamp(price, 0.25 × base_price, 4 × base_price)
```

`adjustment_rate` = 0.1 (prices move slowly toward equilibrium).

### 6. LaborSystem (weekly)

Workers evaluate employment options and migrate:

1. For each county, rank all active non-distressed facilities by wage rate (descending)
2. Workers fill highest-wage facilities first
3. Friction: only ~15% of employed workforce reconsiders per week
4. Workers won't work below subsistence wage (configurable floor)
5. **Distressed facilities**: all workers leave with no friction (100% exit, not the normal 15% reconsideration). This happens during the weekly LaborSystem tick — "immediately" means "at the next LaborSystem run, bypassing friction"
6. Unemployed workers take any job paying ≥ subsistence wage

This replaces the current greedy first-come-first-served allocation entirely.

## Market Clearing Algorithm

Posted-price clearing with proportional rationing. Runs once per day per market.

### Input

- **Sell orders**: `[(seller_id, good_id, quantity, transport_cost)]`
  - From facility output buffers
  - Quantity reduced by transport goods-loss before reaching market
- **Buy orders**: `[(buyer_id, good_id, quantity, max_spend)]`
  - From facilities (inputs) and population (consumer goods)
  - `max_spend` = buyer's treasury cap for this purchase
  - **Quantity is capped by affordability at effective price**: `effective_price = price × (1 + buyer_cost × 0.005)`, so transport fees are accounted for before clearing, not after

### Algorithm

For each good at this market:

```
1. total_supply = sum of sell quantities (after transport loss)
2. total_demand = sum of buy quantities (capped by affordability at effective price incl. transport)
3. traded = min(total_supply, total_demand)

4. IF excess demand (demand > supply):
   - Each buyer gets: (their_demand / total_demand) × traded
   - All sellers sell everything
   - Unsatisfied demand recorded

5. IF excess supply (supply > demand):
   - Each seller sells: (their_supply / total_supply) × traded
   - All buyers get everything they wanted
   - Unsold goods stay on consignment (see inventory ownership below)

6. Money transfer:
   - Each buyer pays: quantity_received × effective_price (see affordability)
   - Each seller receives: quantity_sold × price (seller transport already paid at ship-time)
   - Buyer transport fees → buyer's county population treasury (hauler income)

7. Update MarketGoodState: Supply, Demand, LastTradeVolume, Revenue
```

### Inventory Ownership (Consignment)

Goods listed on the market remain **owned by the seller**. Each consignment lot tracks `(seller_id, good_id, quantity, day_listed)`.

- **Unsold goods** persist on the market as consignment stock. They are relisted automatically the next day at the new posted price. No return trip, no double transport cost.
- **When consignment stock sells**, revenue goes to the original `seller_id`.
- **Resolution order**: FIFO — oldest lots sell first. This prevents stale inventory from lingering indefinitely and ensures deterministic payout.
- **Withdrawal**: if a facility deactivates, its consignment stock is abandoned (decays on market). Facilities don't haul unsold goods back.
- **Decay** applies to consignment stock daily (same rate as the good's `DecayRate`).

This means `MarketGoodState.Supply` includes both fresh sell orders and consignment carryover. The market acts as a warehouse, not an owner.

### Transport Costs

Each party pays based on **their own** distance to the market:

- **Seller** ships goods to market (costs charged **at ship-time**, when goods are listed):
  - **Goods loss**: `arrived = shipped × efficiency` where `efficiency = 1 / (1 + seller_cost × 0.01)`
    - Loss happens during shipping. Market receives `arrived`, not `shipped`.
  - **Hauling fee**: `fee = shipped × price × seller_cost × 0.005`
    - Paid upfront from facility treasury when goods are listed (not when they sell)
    - Transferred to seller's county population (teamster income)
  - Consignment stock already on the market incurs no additional transport cost

- **Buyer** ships goods from market:
  - **Hauling fee**: `fee = quantity × price × buyer_cost × 0.005`
    - Added to buyer's cost
    - Transferred to buyer's county population (teamster income)
  - No additional goods loss on the buy side (loss already applied on sell side)

A county adjacent to the market pays near-zero fees on both sides. A distant county pays significant fees. Money always stays in circulation.

### Market Inventory Carryover

Unsold goods persist on the market between days (with decay applied). This means a market can accumulate inventory during oversupply, smoothing short-term fluctuations.

## Wage & Labor Model

### Wage Setting

Facilities set wages based on a 7-day rolling average of realized margins:

```
avg_revenue = rolling_7day_avg(actual_revenue_per_day)
avg_input_cost = rolling_7day_avg(actual_input_cost_per_day)
margin = avg_revenue - avg_input_cost

IF margin > 0:
  max_wage = margin / labor_required
  wage_rate = max_wage × 0.7                  // retain 30% as operating profit
  wage_rate = max(wage_rate, subsistence_wage) // never offer below subsistence

IF margin ≤ 0:
  // Facility is losing money. Pay subsistence from reserves if possible.
  wage_rate = subsistence_wage
  // Treasury drains. If it hits zero, facility enters distressed state.
  // Distressed facilities can't hire at next LaborSystem tick → deactivate.
```

The 0.7 factor retains 30% of margin as facility operating profit (builds treasury for bad days). Using rolling averages prevents wage whiplash from single-day price spikes.

### Subsistence Wage

Minimum wage workers will accept. Based on a smoothed basic-needs basket, not spot prices:

```
basic_basket_cost = sum(price(good) × BaseConsumption for each Basic need good)
smoothed_basket = EMA_30day(basic_basket_cost)  // 30-day exponential moving average
subsistence_wage = smoothed_basket × 1.2

// Daily change cap: subsistence_wage can't move more than 2% per day
subsistence_wage = clamp(subsistence_wage,
                         yesterday_subsistence × 0.98,
                         yesterday_subsistence × 1.02)
```

The 30-day EMA and 2% daily cap prevent pro-cyclical wage shocks: a bread price spike doesn't instantly force all facilities to pay more, which would cascade into distress and further supply drops. The wage tracks real costs over time but absorbs short-term volatility.

The 1.2 multiplier gives a small margin above bare survival. Workers won't accept less.

If a facility can't afford subsistence wage and has no treasury reserves, it enters distressed state → workers leave → deactivates.

### Labor Mobility

Workers don't teleport between counties. Labor reallocation happens within a county only:

1. Each week, ~15% of workers reconsider their employment
2. They compare current wage to best available alternative in the same county
3. If alternative pays >10% more, they switch
4. Unemployed workers take any job paying ≥ subsistence wage
5. Cross-county migration is handled by the existing MigrationSystem (separate from labor allocation)

### Skill Types

Keep current two-tier system:

- **Unskilled** (Landed + Laborers): extraction facilities
- **Skilled** (Artisans): processing facilities

Workers don't switch skill type. Skilled labor scarcity is a real constraint — the economy must work within it.

## Consumption Model

### Daily Rates

All consumption is daily. The `BaseConsumption` rate on each good encodes how much a person needs per day. High-frequency goods (bread) have large daily rates. Rare goods (furniture) have tiny daily rates.

No bulk purchasing, no household inventory. Population buys a small amount every day and consumes it immediately. The market sees smooth, predictable demand.

### Subsistence Model

Extraction facilities retain 20% of output in the county stockpile. This represents peasant self-sufficiency — farmers eating their own crops before selling the rest.

**Basic-food equivalence**: raw extraction goods map to the consumer good they eventually become:

| Stockpile Good                 | Satisfies Need For | Conversion Rate                            |
| ------------------------------ | ------------------ | ------------------------------------------ |
| wheat, rye, barley, rice_grain | bread              | 1 unit grain → 0.5 units bread-equivalent  |
| goats (milk→cheese chain)      | cheese             | 1 unit goats → 0.3 units cheese-equivalent |

Conversion rates approximate the production chain yield (grain→flour→bread loses ~50%).

Each tick, before posting market buy orders, the county consumes basic-food equivalents from its stockpile:

```
for each basic consumer good:
  raw_need = BaseConsumption × county_population
  equivalent_available = sum(stockpile[grain] × conversion_rate for matching grains)
  subsistence_covered = min(raw_need, equivalent_available)
  // Deduct consumed raw goods from stockpile proportionally
  market_need = raw_need - subsistence_covered
```

Only basic needs (bread, cheese) can be satisfied by subsistence. Comfort and luxury goods always require market purchase.

### Demand Quantity

Every tick, for each consumer good:

```
raw_demand = BaseConsumption × county_population
demand = raw_demand - subsistence_covered  // zero for non-basic goods
```

### Purchasing Priority

When population can't afford everything, they prioritize by NeedCategory:

1. **Basic** needs (bread, cheese) — buy first
2. **Comfort** needs (clothes, shoes, tools, cookware) — buy with remaining budget
3. **Luxury** needs (furniture, jewelry, spices, sugar) — buy last, only if flush

Within each tier, distribute budget proportionally across goods.

### Affordability

Population submits buy orders with a strict priority waterfall:

```
budget_basic = min(treasury, total_basic_cost)           // basics get first claim on ALL money
budget_comfort = min(treasury - basic_spent, total_comfort_cost)  // comfort from remainder
budget_luxury = treasury - basic_spent - comfort_spent            // luxury from whatever's left
```

Basics can consume 100% of treasury if needed — population will go hungry before buying furniture. When budget is insufficient within a tier, each good in that tier gets a pro-rata share of the available money.

If a good's price exceeds the per-unit budget allocation, the population buys fewer units (not zero — they buy what they can afford). This creates smooth demand curves rather than cliff edges.

## Facility Lifecycle (Abstracted)

For this version, facilities are **placed at initialization** (same heuristics as current system) and **cannot be constructed or destroyed**.

Dynamic behavior comes from **activation/deactivation**:

- **Active**: has workers, produces, trades. Costs money to operate.
- **Idle**: no workers, no production, no cost. Dormant.

A facility activates when market prices make it profitable. It deactivates after sustained losses. This gives us demand-responsive supply without building the full investment/construction system.

### Future: Construction & Destruction

When we add this later, the system is ready:

- Merchants/nobility accumulate capital from trade profits
- They observe sustained high prices (unmet demand) → invest in new facility
- Construction costs money + materials (lumber, iron, tools)
- Unprofitable facilities are demolished after extended idleness → reclaim some materials

## Bootstrap Procedure

### Step 1: Place Facilities

Same as current `EconomyInitializer`. All facility types placed based on county resources and population. All start **active**.

### Step 2: Seed Money

```
For each county:
  pop_endowment = population × 30 × daily_basic_cost_at_base_prices
  population.Treasury = pop_endowment

For each facility:
  weekly_input_cost = sum(input.quantity × input.base_price) × 7
  weekly_wage_bill = labor_required × subsistence_wage × 7
  facility.Treasury = weekly_input_cost + weekly_wage_bill
```

### Step 3: Seed Market Inventory

```
For each market:
  Create a synthetic seed seller (seller_id = -100000 - marketId)  // reserved negative ID range
  For each good with local production potential:
    Create consignment lot: (seller_id=seed_seller, good, quantity=estimated_weekly_demand × 2)
    price = base_price
```

Seed inventory is owned by a synthetic seller per market. Revenue from seed sales goes to the market hub's county population (the host city benefits from initial commerce). The synthetic seller never produces new goods — once seed inventory is exhausted or decayed, it's gone.

This gives markets a buffer so day-1 buyers find goods available.

### Step 4: Seed Facility Inputs

```
For each processing facility:
  For each input good:
    facility.InputBuffer.Add(good, 3 × daily_input_need)
```

Three days of inputs so processing facilities can produce immediately.

### Step 5: Prefill Day-1 Orders

Because MarketSystem runs first and clears _yesterday's_ orders, tick 1 would be a dead day (no orders exist yet). To avoid this:

```
For each market:
  For each processing facility in zone:
    Post buy orders for inputs (as if OrderSystem ran on day 0)
  For each county in zone:
    Post population buy orders for basic needs (as if OrderSystem ran on day 0)
  Set all prefilled orders' DayPosted = 0 so they clear on tick 1
```

This ensures tick 1 has real trade: seed inventory meets prefilled buy orders.

### Step 6: Run

Let the simulation find equilibrium naturally. Expect 1-2 in-game months of price oscillation before stabilization.

## Disabled Systems

### Black Market

Disabled for V2 initial implementation. Will revisit once legitimate markets are functioning correctly. The `Market` with `Type = Black` is not created. TheftSystem is not registered.

### Off-Map Markets

Keep but simplify. Off-map markets provide import-only supply of goods not producible locally (spices, sugar in northern climates). Prices fixed at 2× base. No export.

## What We Keep

| Component                                                     | Status                                 |
| ------------------------------------------------------------- | -------------------------------------- |
| GoodDef, FacilityDef registries                               | Keep (tune BaseConsumption rates)      |
| GoodRegistry, FacilityRegistry                                | Keep                                   |
| EconomyInitializer (resource assignment + facility placement) | Keep (modify bootstrap)                |
| Market, MarketPlacer                                          | Keep structure (modify clearing logic) |
| TransportGraph, RoadState                                     | Keep                                   |
| ITickSystem, SimulationRunner                                 | Keep                                   |
| County/Cell/MapData                                           | Keep                                   |
| Population structure (estates, cohorts)                       | Keep (add Treasury)                    |
| MigrationSystem                                               | Keep                                   |
| OffMapSupplySystem                                            | Keep (simplify)                        |
| EconDebugBridge                                               | Keep (update dump format)              |

## What We Rebuild

| Component         | Change                                                               |
| ----------------- | -------------------------------------------------------------------- |
| ProductionSystem  | Profit-aware activation, subsistence fraction, output→market         |
| TradeSystem       | → MarketSystem: clearing algorithm, transport costs, money transfers |
| ConsumptionSystem | → OrderSystem: buy orders for facilities + population                |
| Worker allocation | → LaborSystem: wage-based, weekly, with friction                     |
| Add: WageSystem   | Facility pays workers from treasury                                  |
| Add: PriceSystem  | Post-clearing price adjustment                                       |
| Facility.cs       | Add Treasury, WageRate fields                                        |
| CountyPopulation  | Add Treasury field                                                   |
| MarketGoodState   | Add Revenue field                                                    |
| CountyEconomy     | Remove ExportBuffer                                                  |

## System Registration Order

```csharp
runner.RegisterSystem(new MarketSystem());         // daily — clear yesterday's orders
runner.RegisterSystem(new ProductionSystem());     // daily — produce, post sell orders
runner.RegisterSystem(new OrderSystem());          // daily — facilities + population post buy orders
runner.RegisterSystem(new WageSystem());           // daily — pay workers
runner.RegisterSystem(new PriceSystem());          // daily — adjust prices
runner.RegisterSystem(new LaborSystem());          // weekly — worker reallocation
runner.RegisterSystem(new MigrationSystem());      // monthly — population movement
```

This is the same order as the Tick Architecture section. MarketSystem runs first to deliver yesterday's goods before today's production begins.
