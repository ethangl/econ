# Economy v4 — Implementation Phases

Incremental build plan for the [economy v4 design](economy-v4.md). Each phase is validated via the EconDebugBridge + analyzer before moving on. V3 code stays intact until Phase 6.

## Phase 0: Foundation (Data Model + Skeleton)

**Goal:** New data types, reduced goods list, new tick pipeline shell — compiles but does nothing.

### Work

- New `GoodDefV4` with 20 goods (14 biome + 5 facility + 1 special), each with `value_g` and `bulk_g`
- New biome yield table for v4 goods (including collapsed goods: Leather, Wine, Candles, Silk, Spices)
- New `FacilityDefV4` with 5 facilities (Bakery, Brewery, Smithy, Weaver, Carpenter)
- Every county gets all 5 facilities; market viability determines which actually operate
- New `Order` struct (buyer/seller ID, good, quantity, max bid)
- New `CountyEconomyV4` per-county state:
  - Lower/upper commoner population
  - Five coin pools: upper noble treasury, lower noble treasury, upper clergy treasury, lower clergy coin, upper commoner coin (M)
- New `MarketStateV4` — order book per good, price level, last clearing prices
- New `EconomyStateV4` container wiring counties to markets
- New `EconomyTickV4` shell with 4 empty phases: GenerateOrders → ResolveMarkets → UpdateMoney → UpdateSatisfaction
- V3/V4 toggle — both coexist, v4 selected via flag on GameManager or SimulationRunner

### Validation

- Compiles in Unity
- EconDebugBridge can instantiate v4 state alongside map
- Analyzer confirms v4 state is initialized (county count, market count, good count)

---

## Phase 1: Subsistence Economy (No Money)

**Goal:** Lower commoners produce and eat. Lords collect surplus. No coin, no markets yet.

### Work

- Biome extraction: lower commoner pop × biome yield per good → raw production per county
- Subsistence consumption: peasants consume from own production (staples + salt + timber)
- Surplus calculation: production − consumption → surplus available for sell orders
- Lord's treasury tracks surplus in goods (converted to coin later in Phase 2)
- Satisfaction: survival component only — `(local staple production + lord-provided food) / staple need`
- Deficit tracking: counties that can't feed themselves are flagged

### Validation

Run 12 months via EconDebugBridge. Analyzer checks:

- Per-county: production, consumption, surplus per good
- Peasant survival satisfaction (0–1)
- Food-producing counties (grassland, coast) have surplus >0
- Specialized counties (mountain/mining) have food deficit
- No crashes, no NaN, population stable (no births/deaths yet)

---

## Phase 2: Local Market Resolution (Single Market)

**Goal:** Buy/sell orders resolve within a single market. Coin enters the system.

### Work

- Sell orders from biome surplus (revenue → upper noble treasury as coin)
- Upper noble buy orders: serf feeding (cover deficit), then household staples/basics/comforts/luxuries
- Lower noble buy orders: staples, basics, comforts, luxuries (funded by stipend from upper noble)
- Upper clergy buy orders: lower clergy wages, candles, wine, comforts (funded by tithe — will be inactive until Phase 3 since tithe requires upper commoner transactions)
- Lower clergy buy orders: staples, basics, candles (funded by wages — inactive until Phase 3)
- Clearing price formula: `value_g × (1 + 0.75 × scarcity) × price_level`
- Price level from quantity theory: `max((M × V) / Q, 1.0)` — floor of 1.0 ensures meaningful prices when M ≈ 0
- Fill buy orders by descending max bid until supply exhausted
- Tax skim on upper commoner buys → upper noble treasury (lower clergy exempt)
- Tithe skim on upper commoner buys → upper clergy treasury (lower clergy exempt)
- Gold minting: gold production → new coin into upper noble treasury
- Stipend transfer: upper noble → lower noble treasury (fixed per tick)
- Clergy wage transfer: upper clergy → lower clergy coin (fixed per tick)
- Coin wear: small constant drain on M

### Validation

Run 12 months. Analyzer checks:

- Per-market: clearing prices per good, price level (should be 1.0 everywhere — M is still zero)
- Per-county: upper noble treasury balance, coin inflow/outflow between lord treasuries
- Gold-producing counties have nonzero lord treasuries (minting → lord treasury)
- Serf deficit counties receive food via lord buy orders
- Coin circulates between lord treasuries (surplus sellers earn, deficit buyers spend)
- Clergy economy is inactive (tithe = 0 without upper commoner transactions — this is expected, clergy activates in Phase 3)
- Lower nobility receives stipends and spends on goods

---

## Phase 3: Upper Commoners + Facilities

**Goal:** Artisans run facilities, earn and spend coin. Full domestic economy.

### Work

- Facility production: buy inputs (buy orders) → produce outputs (sell orders, 1-tick lag)
- Facility sell orders based on last tick's input fill rate
- Upper commoner buy orders from coin balance (staples → basics → comforts → luxuries)
- Upper commoner coin pool: earned from facility sales, spent on goods + taxed
- Upper commoner budget allocation by tier (percentages from design doc)
- Clergy economy now active: upper commoner transactions generate tithe → clergy treasury → wages → lower clergy spending
- Facility input buy orders at full capacity (capped by coin budget)
- Value-add validation: output value > input cost for all 5 facilities

### Validation

Run 12 months. Analyzer checks:

- Per-facility: input fill rate, output volume, profitability
- Upper commoner income vs spending vs savings
- M supply trajectory (growing? stable? draining?)
- Elite treasury flows: tax in, spending out, net balance
- Counties with active facilities have higher upper commoner satisfaction
- Counties where facilities are idle (can't source inputs) still function via subsistence + elite spending
- Budget allocation percentages produce reasonable spending patterns

---

## Phase 4: Cross-Market Trade

**Goal:** Markets connected by trade merchants. Goods and coin flow between markets.

### Work

- Price differential: `last_price_B − last_price_A`
- Transport cost: `distance(A, B) × bulk_g × transport_rate`
- Profit margin: `(price_diff − transport_cost) / last_price_A`
- Trade volume: `min(surplus_A, deficit_B) × clamp(margin, 0, 1)`
- Trade orders posted to both markets (sell in B, buy in A)
- Coin flow: importers lose coin, exporters gain
- Tariffs: lord skims percentage of cross-market trade value → upper noble treasury

### Validation

Run 12 months. Analyzer checks:

- Trade flows per good between market pairs (volume + direction)
- Price convergence: same good in adjacent markets should have prices within transport cost
- Luxury goods (low bulk) trade long-distance; staples (high bulk) trade short-distance
- Net coin movement between markets (gold-rich markets should be net importers)
- Tariff revenue in lord treasuries
- No market isolation (every market with surplus trades if margins exist)

---

## Phase 5: Population Dynamics + Satisfaction

**Goal:** Full satisfaction model drives births, deaths, migration. Economy shapes geography.

### Work

- Per-class satisfaction with all components:
  - Survival (heaviest) — subsistence/staple fulfillment
  - Religion (heavy) — clergy buy order fulfillment (candles, wine)
  - Stability (heavy) — peace/war status (placeholder: always peaceful for now)
  - Economic (moderate) — comfort/luxury buy order fulfillment
  - Governance (light) — tax burden fairness (placeholder)
- Birth rate: `base_birth × (1 + satisfaction_modifier × satisfaction)`
- Death rate: `base_death × (1 + satisfaction_modifier × (1 − satisfaction))`
- Upper commoner migration: flow toward higher satisfaction, rate proportional to gap
- Lower commoner migration: very slow, only under extreme dissatisfaction
- Elite estates: no migration (tied to land)

### Validation

Run 60 months. Analyzer checks:

- Population trajectory per county per class (growing/shrinking/stable)
- Upper commoner migration patterns (toward high-satisfaction counties)
- Urban formation: counties with facilities + gold attract population
- Rural counties: stable lower commoner base, slow change
- Satisfaction breakdown per class per county (which components dominate)
- No population explosion or extinction
- Gold-rich counties become economic centers

---

## Phase 6: Cleanup + Tooling Overhaul

**Goal:** Remove v3 code, finalize tooling.

### Work

- Remove v3 economy systems: ProductionSystem, ConsumptionSystem, FiscalSystem, TradeSystem, InterRealmTradeSystem, TheftSystem, SpoilageSystem, TitheSystem
- Remove v3 data: old GoodType (48 goods), old FacilityType (25 facilities), old EconomyState, CountyEconomy, ProvinceEconomy, RealmEconomy
- Rename v4 types (drop "V4" suffix)
- Overhaul `analyze_econ.py` for final v4 data model
- Overhaul EconDebugBridge dump format
- Update SelectionPanel UI for v4 economy display

### Validation

- Full regression: 60-month run produces healthy economy
- Analyzer output is clean and comprehensive
- No references to v3 types remain
- Unity compiles clean, no warnings

---

## Analyzer Evolution

The analyzer (`scripts/analyze_econ.py`) and EconDebugBridge dump format grow with each phase:

| Phase | Analyzer Additions                                                        |
| ----- | ------------------------------------------------------------------------- |
| 0     | County count, market count, good list, initial state                      |
| 1     | Production, consumption, surplus per good per county, survival satisfaction |
| 2     | Clearing prices, price level, lord treasury, M supply, coin flows         |
| 3     | Facility throughput, upper commoner income/spending, elite treasury flows  |
| 4     | Trade flows, price convergence, coin movement, tariff revenue             |
| 5     | Population change, migration, satisfaction breakdown by component          |
| 6     | Full overhaul — clean v4-native output                                    |

## Key Decisions

1. **V3/V4 coexistence:** V3 stays alive until Phase 5 validates. V4 is a parallel code path toggled at startup.
2. **Good lists are separate:** V4 defines its own 20-good enum/registry. V3's 48-good system is untouched until Phase 6.
3. **6 estates preserved:** The existing `Estate` enum (LowerCommoner, UpperCommoner, LowerNobility, UpperNobility, LowerClergy, UpperClergy) carries forward into v4. Five coin pools, not four.
