# Economy Design.

## Layer 1: Autarky

Single abstract good called "Goods". Each county produces and consumes independently. No trade, no prices, no markets.

**Production:** `county.Population * county.Productivity` per day. Productivity is the average biome productivity of the county's land cells (0.0 for glacier/lake, up to 1.4 for floodplain).

**Consumption:** `county.Population * 1.0` per day. Consumed from stock; shortfall recorded as unmet need.

**Stock:** Accumulates without limit or decay. Counties with productivity > 1.0 build surplus indefinitely; counties below 1.0 deplete to zero and stay there.

**Snapshot:** Aggregated each tick — total stock/production/consumption/unmet need, surplus/deficit/starving county counts, min/max stock, median productivity.

### Files

- `Economy/CountyEconomy.cs` — per-county state (Stock, Population, Productivity, Production, Consumption, UnmetNeed)
- `Economy/EconomyState.cs` — county array + time series + cached median productivity
- `Economy/EconomySnapshot.cs` — aggregate data point
- `Economy/BiomeProductivity.cs` — biome ID → productivity lookup
- `Economy/EconomySystem.cs` — ITickSystem (daily): init from MapData, tick loop, snapshot

### Biome Productivity Table

| Id  | Biome          | Prod |     | Id  | Biome               | Prod |
| --- | -------------- | ---- | --- | --- | ------------------- | ---- |
| 0   | Glacier        | 0.0  |     | 10  | Scrubland           | 0.5  |
| 1   | Tundra         | 0.2  |     | 11  | Tropical Rainforest | 0.8  |
| 2   | Salt Flat      | 0.1  |     | 12  | Tropical Dry Forest | 0.9  |
| 3   | Coastal Marsh  | 0.7  |     | 13  | Savanna             | 1.1  |
| 4   | Alpine Barren  | 0.2  |     | 14  | Boreal Forest       | 0.6  |
| 5   | Mountain Shrub | 0.4  |     | 15  | Temperate Forest    | 0.9  |
| 6   | Floodplain     | 1.4  |     | 16  | Grassland           | 1.3  |
| 7   | Wetland        | 0.7  |     | 17  | Woodland            | 1.0  |
| 8   | Hot Desert     | 0.3  |     | 18  | Lake                | 0.0  |
| 9   | Cold Desert    | 0.3  |     |     |                     |      |

## Layer 2: Feudal Tax Redistribution

Two-tier feudal redistribution replaces autarky isolation. Goods flow through the political hierarchy: counts pay dukes, dukes pay kings, and relief flows back down. No prices or markets — this is administrative fiat within a realm.

**Hierarchy:** County (count) → Province (duke) → Realm (king). Each tier taxes the one below and redistributes downward.

**Tick order** (runs daily after EconomySystem production/consumption):

1. **Duke taxes surplus counties.** For each county with stock above daily need, the duke takes `DucalTaxRate` (20%) of the surplus into the provincial stockpile.
2. **King taxes surplus provinces.** For each province with stockpile > 0, the king takes `RoyalTaxRate` (20%) into the royal stockpile.
3. **King distributes to deficit provinces.** Royal stockpile allocated proportionally to each province's unmet county need (net of local stockpile). A province that can cover its own deficit locally gets nothing from the king.
4. **Duke distributes to deficit counties.** Provincial stockpile (now topped up by the king) allocated proportionally to each county's shortfall (dailyNeed - stock).

Goods flow county → province → realm → province → county in a single tick.

**Constraints:** Redistribution is realm-internal. Realm borders are hard walls — no inter-realm trade at this layer.

**Steady-state behavior:** Royal stockpiles drain to zero each tick (kings fully distribute). Provincial stockpiles stabilize at a small positive level for surplus provinces. ~15% of counties still starve because total map production is structurally below total consumption need — no amount of redistribution can close that gap.

### Files

- `Economy/CountyEconomy.cs` — adds TaxPaid, Relief per county
- `Economy/ProvinceEconomy.cs` — Stockpile, TaxCollected, ReliefGiven per province
- `Economy/RealmEconomy.cs` — Stockpile, TaxCollected, ReliefGiven per realm
- `Economy/EconomyState.cs` — adds Provinces[], Realms[] arrays
- `Economy/EconomySnapshot.cs` — adds ducal/royal tax/relief/stockpile aggregates
- `Economy/TradeSystem.cs` — ITickSystem (daily): builds province/realm mappings at init, 4-phase tick

## Layer 3: Multiple Goods

Split "Goods" into distinct types (food, timber, ore). Each biome produces different goods at different rates. Counties now have comparative advantage. Units: **kg** per person per day for all goods.

**GoodType enum:** Food=0, Timber=1, Ore=2. `Goods.Count = 3`.

### Phase A: Multi-Good Data Model + Production

Per-good production with food-only consumption and redistribution. Timber/Ore accumulate in county stockpiles as observable data. Food production = unchanged Layer 2 values. Food balance and starvation rates identical to Layer 2.

- `CountyEconomy` fields → `float[Goods.Count]` arrays (Stock, Productivity, Production, Consumption, UnmetNeed, TaxPaid, Relief)
- `ProvinceEconomy` / `RealmEconomy` fields → `float[Goods.Count]` arrays (Stockpile, TaxCollected, ReliefGiven)
- `BiomeProductivity` → 2D table (biomeId × goodType)
- Production loop produces all goods; consumption and redistribution operate on food index only

### Phase B: Multi-Good Consumption

All goods consumed daily. Food is a staple (shortfall = starvation). Timber/Ore are comfort goods (shortfall = unmet need, no starvation).

**Consumption rates (kg/person/day):** Food 1.0, Timber 0.2, Ore 0.01.

**Behavior:** Each good consumed independently from county stock. Unmet need tracked per good type. `StarvingCounties` count still based on food only — comfort shortfall has no gameplay consequence yet (observable data for future welfare mechanics).

### Phase C: Per-Good Feudal Redistribution

Tax and relief flows operate per good type. Timber/Ore flow through the feudal hierarchy alongside food. Same tax rates (20% ducal, 20% royal) and proportional allocation logic per good. Surplus threshold per good = `Population * ConsumptionPerPop[g]`.

### Biome Productivity Table (kg/person/day)

| Id  | Biome               | Food | Timber | Ore |
| --- | ------------------- | ---- | ------ | --- |
| 0   | Glacier             | 0.0  | 0.0    | 0.0 |
| 1   | Tundra              | 0.2  | 0.0    | 0.0 |
| 2   | Salt Flat           | 0.1  | 0.0    | 0.1 |
| 3   | Coastal Marsh       | 0.7  | 0.0    | 0.0 |
| 4   | Alpine Barren       | 0.2  | 0.0    | 0.4 |
| 5   | Mountain Shrub      | 0.4  | 0.1    | 0.3 |
| 6   | Floodplain          | 1.4  | 0.0    | 0.0 |
| 7   | Wetland             | 0.7  | 0.1    | 0.0 |
| 8   | Hot Desert          | 0.3  | 0.0    | 0.2 |
| 9   | Cold Desert         | 0.3  | 0.0    | 0.2 |
| 10  | Scrubland           | 0.5  | 0.1    | 0.1 |
| 11  | Tropical Rainforest | 0.8  | 0.5    | 0.0 |
| 12  | Tropical Dry Forest | 0.9  | 0.4    | 0.0 |
| 13  | Savanna             | 1.1  | 0.1    | 0.0 |
| 14  | Boreal Forest       | 0.6  | 0.5    | 0.0 |
| 15  | Temperate Forest    | 0.9  | 0.5    | 0.0 |
| 16  | Grassland           | 1.3  | 0.0    | 0.0 |
| 17  | Woodland            | 1.0  | 0.3    | 0.0 |
| 18  | Lake                | 0.0  | 0.0    | 0.0 |

Design: forests produce timber, mountains/deserts produce ore, food unchanged from Layer 1.

## Layer 4: Inter-Realm Trade

Price-driven trade between realms at market towns. This is where prices emerge — feudal redistribution is administrative, but cross-border exchange requires negotiation.

### Phase A: Resource Expansion + Precious Metal Regal Rights

Split ore into iron, gold, and silver. Add salt and wool. Create geographic resource diversity so realms have distinct surpluses and deficits. Precious metals (gold and silver ore) are crown property (regal right).

**GoodType:** Food, Timber, IronOre, GoldOre, SilverOre, Salt, Wool (7 goods)

**Population consumption (kg/person/day):** Food 1.0 (staple), Timber 0.2, Wool 0.1, Salt 0.05, IronOre 0.005, GoldOre 0.0, SilverOre 0.0 (precious metals not consumed, minted)

**Administrative consumption (kg/capita/day):** Each tier of the feudal hierarchy consumes goods for upkeep before taxing or redistributing. Rates are per capita of the tier's total population.

| Tier     | Food | Timber | Iron  | Wool  | Purpose         |
| -------- | ---- | ------ | ----- | ----- | --------------- |
| County   | —    | 0.02   | —     | —     | Building upkeep |
| Province | —    | 0.01   | 0.001 | —     | Infrastructure  |
| Realm    | 0.02 | 0.01   | 0.003 | 0.005 | Military upkeep |

**Feudal tick order:** County admin → ducal tax → provincial admin → royal tax → royal admin → king distributes → duke distributes. Each tier feeds itself before passing surplus up or relief down.

**Feudal redistribution:** Precious metals (gold, silver) taxed at 100% (regal right). All other goods remain at 20%.

**Biome productivity (kg/person/day):**

| Id  | Biome               | Food | Timber | Iron | Gold | Silver | Salt | Wool |
| --- | ------------------- | ---- | ------ | ---- | ---- | ------ | ---- | ---- |
| 0   | Glacier             | 0.0  | 0.0    | 0.0  | 0.0  | 0.0    | 0.0  | 0.0  |
| 1   | Tundra              | 0.2  | 0.0    | 0.0  | 0.0  | 0.0    | 0.0  | 0.0  |
| 2   | Salt Flat           | 0.1  | 0.0    | 0.0  | 0.0  | 0.0    | 0.4  | 0.0  |
| 3   | Coastal Marsh       | 0.7  | 0.0    | 0.0  | 0.0  | 0.0    | 0.3  | 0.0  |
| 4   | Alpine Barren       | 0.2  | 0.0    | 0.3  | 0.02 | 0.03   | 0.0  | 0.0  |
| 5   | Mountain Shrub      | 0.4  | 0.1    | 0.2  | 0.01 | 0.02   | 0.0  | 0.0  |
| 6   | Floodplain          | 1.4  | 0.0    | 0.0  | 0.0  | 0.0    | 0.0  | 0.0  |
| 7   | Wetland             | 0.7  | 0.1    | 0.0  | 0.0  | 0.0    | 0.05 | 0.0  |
| 8   | Hot Desert          | 0.3  | 0.0    | 0.2  | 0.0  | 0.01   | 0.0  | 0.0  |
| 9   | Cold Desert         | 0.3  | 0.0    | 0.2  | 0.0  | 0.01   | 0.0  | 0.0  |
| 10  | Scrubland           | 0.5  | 0.1    | 0.1  | 0.0  | 0.005  | 0.0  | 0.1  |
| 11  | Tropical Rainforest | 0.8  | 0.5    | 0.0  | 0.0  | 0.0    | 0.0  | 0.0  |
| 12  | Tropical Dry Forest | 0.9  | 0.4    | 0.0  | 0.0  | 0.0    | 0.0  | 0.0  |
| 13  | Savanna             | 1.1  | 0.1    | 0.0  | 0.0  | 0.0    | 0.0  | 0.2  |
| 14  | Boreal Forest       | 0.6  | 0.5    | 0.0  | 0.0  | 0.0    | 0.0  | 0.0  |
| 15  | Temperate Forest    | 0.9  | 0.5    | 0.0  | 0.0  | 0.0    | 0.0  | 0.0  |
| 16  | Grassland           | 1.3  | 0.0    | 0.0  | 0.0  | 0.0    | 0.0  | 0.3  |
| 17  | Woodland            | 1.0  | 0.3    | 0.0  | 0.0  | 0.0    | 0.0  | 0.05 |
| 18  | Lake                | 0.0  | 0.0    | 0.0  | 0.0  | 0.0    | 0.0  | 0.0  |

### Phase B: Minting

- Treasury tracked on RealmEconomy
- Realm mints gold and silver ore into coins (ore consumed, different conversion rates)

### Phase C: Single Market, Inter-Realm Trade

One market at a realm capital. Realms sell surplus goods for coin and buy deficit goods with coin. Market sets prices from supply and demand.

- Realms bring surplus to market, buy deficit goods with coin
- Market clears orders, prices emerge from supply/demand

## Layer 5: Population Dynamics

Static population → dynamic. Growth from food surplus, decline from starvation, migration toward prosperity. Monthly tick evaluates conditions over ~30 daily economic cycles.

- Growth/decline driven by sustained food satisfaction (not single-day spikes)
- Migration toward counties with better conditions
- Monthly update reads back over daily TimeSeries for averaging
- Observable: growing vs shrinking counties, migration flows, carrying capacity emergence

## Layer 6: Mature Goods Model + Spoilage

Centralize per-good metadata into a single `GoodDef` struct. Add Stone and Ale. Introduce spoilage to constrain stockpile hoarding.

### GoodDef Refactor

All per-good data lives in `GoodDef` readonly struct. `Goods.Defs[]` is the single source of truth; flat arrays (`ConsumptionPerPop`, `BasePrice`, `MonthlyRetention`, etc.) are extracted once in the static constructor for hot-path access.

**GoodDef fields:** Type, Name, Category, NeedCategory, ConsumptionPerPop, CountyAdminPerPop, ProvinceAdminPerPop, RealmAdminPerPop, BasePrice, MinPrice, MaxPrice, IsTradeable, IsPreciousMetal, SpoilageRate.

**GoodType (9 goods):** Food, Timber, IronOre, GoldOre, SilverOre, Salt, Wool, Stone, Ale.

**Stone:** Admin-only good — not consumed by population, only by feudal tiers (county 0.005, province 0.008, realm 0.012 kg/capita/day). Represents construction materials for buildings, roads, fortifications. Traded on the inter-realm market.

**Ale:** Second staple (Basic need). 0.5 kg/person/day — represents the grain-equivalent of a daily ale ration. Medieval ale was unhopped, brewed frequently in small batches because it spoiled within days. Produced in grain-growing biomes (floodplain, grassland, savanna, woodland, temperate forest). Highly perishable (5%/day). Not yet wired into FoodSatisfaction — ale shortfall is tracked as unmet need but doesn't affect birth/death/migration.

**Need categories:** Basic (food, ale, salt — shortfall is a staple deprivation), Comfort (timber, iron, wool — shortfall tracked but no death), None (gold, silver, stone — no pop consumption).

**Price bands (Crowns/kg):**

| Good    | Base | Min  | Max  |
| ------- | ---- | ---- | ---- |
| Food    | 1.0  | 0.1  | 10.0 |
| Timber  | 0.5  | 0.05 | 5.0  |
| IronOre | 5.0  | 0.5  | 50.0 |
| Salt    | 3.0  | 0.3  | 30.0 |
| Wool    | 2.0  | 0.2  | 20.0 |
| Stone   | 0.3  | 0.03 | 3.0  |
| Ale     | 0.8  | 0.08 | 8.0  |

Gold/Silver: not traded (price = 0), minted into Crowns.

**Buy priority:** Food > Ale > Iron > Salt > Wool > Timber > Stone. Staples first, infrastructure last.

### Spoilage

Perishable goods decay monthly. `SpoilageRate` is the daily fractional loss. Monthly retention is precomputed as `pow(1 - spoilageRate, 30)` and stored in `Goods.MonthlyRetention[]`.

| Good   | Daily Rate | Monthly Retention | Character                    |
| ------ | ---------- | ----------------- | ---------------------------- |
| Ale    | 5.0%       | ~21%              | Highly perishable (unhopped) |
| Food   | 3.0%       | ~40%              | Perishable                   |
| Timber | 0.1%       | ~97%              | Slow rot/weathering          |
| Wool   | 0.1%       | ~97%              | Moth damage, mildew          |
| Others | 0%         | 100%              | Inert (metal, stone, salt)   |

**SpoilageSystem** (`ITickSystem`, monthly): iterates counties, provinces, and realms. For each perishable good, multiplies `Stock` / `Stockpile` by `MonthlyRetention[g]`.

**Effect:** Ale and food cannot hoard up the feudal chain — royal and provincial stockpiles decay to zero because monthly spoilage exceeds inflow at those tiers. Ale is the most aggressive (~79% monthly loss), creating constant brewing pressure. Counties maintain small working buffers. Timber and wool experience mild decay (~3%/month), enough to prevent infinite accumulation but not enough to destabilize supply. Inert goods (iron, stone, salt, precious metals) are unaffected.

### Files

- `Economy/GoodDef.cs` — readonly struct with all per-good metadata
- `Economy/GoodType.cs` — enum + `Goods` static class (Defs[], extracted arrays, minting constants)
- `Economy/SpoilageSystem.cs` — ITickSystem (monthly): multiplicative decay on all stockpile tiers
- `Economy/BiomeProductivity.cs` — column count validated against `Goods.Count` at static init

## Layer 7: Production Chains (Phases A–C)

Raw goods → refined goods via facilities. Proved with Clay → Pottery chain.

### Phase A: Data Model

- `FacilityDef` readonly struct: Type, Name, input/output good + amount, LaborPerUnit, PlacementMinProductivity
- `FacilityType` enum + `Facilities.Defs[]` registry
- `Facility` runtime class: Type, CountyId, CellId, Workforce
- New goods: Clay (raw, extracted from biome, comfort, inert) and Pottery (refined, facility-only, comfort, inert)
- Kiln facility: 2 Clay → 1 Pottery, 3 workers, placed in counties with Clay productivity ≥ 0.05

### Phase B: Placement + Processing

- `FacilityProductionSystem.Initialize()` scans counties, places kilns where Clay productivity meets threshold
- `EconomyState.Facilities[]` + `CountyFacilityIndices` (per-county facility lookup)
- Labor allocation: facility workers subtracted from extraction workforce (proportional reduction)

### Phase C: Economic Integration

- Facility processing inlined into `EconomySystem.Tick()` between extraction and consumption
  - Fixes production tracking (ce.Production[Pottery] populated)
  - Fixes consumption timing (Pottery available same-day)
- `FacilityProductionSystem.Tick()` emptied (placement logic retained in Initialize)
- Full economic loop: Clay extraction → kiln processing → Pottery consumption/trade/tax/spoilage
- All existing systems (TradeSystem, InterRealmTradeSystem, SpoilageSystem) process Pottery generically
- EconDebugBridge facility dump expanded with recipe details and throughput
- `analyze_econ.py` production chain reporting section

### Files

- `Economy/FacilityDef.cs` — FacilityType enum, FacilityDef struct, Facilities registry
- `Economy/Facility.cs` — runtime facility instance
- `Economy/FacilityProductionSystem.cs` — ITickSystem: placement at init, processing moved to EconomySystem
- `Economy/EconomySystem.cs` — inline facility processing between extraction and consumption
- `Economy/GoodType.cs` — Clay and Pottery added to GoodType enum and Goods.Defs[]
- `Economy/BiomeProductivity.cs` — Clay column added

### Phase D: Demand-Driven Extraction + Tax Exemption + Two-Tier Quotas

Pure facility inputs — goods with no population consumption and no administrative demand (currently just Clay) — are exempt from feudal taxation and extracted on-demand rather than at full capacity.

**`Goods.HasDirectDemand[]`:** Static boolean array. True if a good has `ConsumptionPerPop > 0` or any admin rate > 0. Goods where `!HasDirectDemand && !IsPreciousMetal` are pure facility inputs.

| Good                                   | HasDirectDemand | IsPreciousMetal | Effect                           |
| -------------------------------------- | --------------- | --------------- | -------------------------------- |
| Food, Timber, IronOre, Salt, Wool, Ale | true            | false           | Normal extraction + tax          |
| Stone                                  | true            | false           | Normal (has admin demand)        |
| Pottery                                | true            | false           | Normal (has consumption + admin) |
| Clay                                   | false           | false           | Demand-driven extraction, no tax |
| Gold/Silver                            | false           | true            | Normal extraction, 100% mint tax |

**Tax exemption:** Phases 2 (ducal tax) and 4 (royal tax) skip goods where `!HasDirectDemand && !IsPreciousMetal`. Other phases already no-op for these goods (zero admin rates, zero consumption, zero deficit).

**Demand-driven extraction:** Counties extract pure facility inputs only to match local facility demand. `ComputeFacilityInputDemand()` sums the input needed by each local facility based on its production target (quota or baseline). Counties without relevant facilities extract zero. Capped by `pop * productivity` as usual.

**Two-tier facility quotas:** Phase 9 split into provincial and realm tiers:

- **Phase 9a (Provincial):** Duke computes province need (`consumption + countyAdmin + provinceAdmin`) and distributes quotas to producing counties within the province. Provinces with facilities are self-sufficient for local demand.
- **Phase 9b (Realm):** King computes realm-specific needs — realm admin consumption plus the full need of provinces that lack local facilities for that good. Distributed to producing counties across the realm, additive to provincial quotas.

Both tiers independently drive production. A kiln county receives its provincial quota share plus any realm quota share. Demand-driven extraction scales automatically via the combined quota.

### Phase E: Chain Migration

With the mechanism proven, add more production chains. Each is a data-driven addition using the same FacilityDef/FacilityProductionSystem infrastructure.

**Completed chains (24 goods, 11 facility types):**

- **Furniture** (Carpenter): 3 timber → 2 furniture. Durable comfort good.
- **Iron** (Smelter): 3 ironOre + 0.4 charcoal → 2 iron. Intermediate for tools.
- **Tools** (Smithy): 2 iron + 0.2 charcoal → 1 tools. Durable comfort good. Placed near iron ore.
- **Charcoal** (CharcoalBurner): 5 timber → 1 charcoal. Fuel for smelters and smithies.
- **Clothes** (Weaver): 3 wool → 2 clothes. Durable comfort good.
- **Sausage** (Butcher): 1 pork + 0.2 salt → 3 sausage. Staple (preserved meat).
- **Bacon** (Smokehouse): 2 pork + 1 timber → 2 bacon. Comfort good (smoked meat).
- **Cheese** (Cheesemaker): 3 milk + 0.3 salt → 1 cheese. Staple (preserved dairy).
- **SaltedFish** (Salter): 1 fish + 0.5 salt → 2 saltedFish. Staple (premium preserved fish).
- **Stockfish** (DryingRack): 2 fish → 2 stockfish. Staple (cheap air-dried fish, no salt needed).

**Staple pooling:** People eat 1.0 kg/day from any combination of staples (bread, sausage, cheese, saltedFish, stockfish). Preference weights: bread 0.50, sausage 0.21, saltedFish 0.14, stockfish 0.08, cheese 0.07. Shortfall across the pool = starvation.

**Durable goods:** Pottery, furniture, tools, clothes have target stock per capita. Wear (spoilage) creates replacement demand rather than daily consumption. Deficit is a trade signal.

**Fish productivity:** Coast-proximity-based extraction (not purely biome). Coastal cells +0.30, distance 1 +0.15, distance 2 +0.05, plus biome values for freshwater biomes (floodplain 0.10, coastal marsh 0.08, wetland 0.05).

**Raw inputs not directly consumed:** Pork, milk, fish, clay, wool, charcoal, iron — extracted on-demand by local facilities or at full capacity if tradeable with direct demand. Tax-exempt when purely facility inputs.

## Layer 8: Domestic Cash Flow

### Layer 9: County Market Access

Currently the king intermediates all trade (inter-realm only) and counties receive goods solely through feudal relief. This layer makes counties the trading agents, opening markets progressively with tolls replacing in-kind taxation at each political boundary.

Feudal redistribution (FiscalSystem) continues to run — taxes and relief still flow through the hierarchy. County trade is an additional channel that lets counties buy/sell surplus directly, bypassing the feudal pipeline for goods they can afford.

**Phase A: Intra-Province Trade**
Counties within the same province trade directly at market prices. Untaxed (or negligible flat fee). The duke's province is a free-trade zone — counties with surplus sell to neighbors with deficit, routed via transport graph.

**Phase B: Cross-Province / Intra-Realm Trade**
Counties trade across province boundaries within the same realm. The buying county pays its own duke a 5% toll on goods entering the province. Creates incentive for provincial self-sufficiency while allowing inter-provincial specialization.

**Phase C: Cross-Realm Trade**
Counties trade across realm borders. Buyers pay 5% ducal toll + 10% royal tariff (price × 1.15). Global county pool — all counties participate regardless of realm. Sequential pass ordering ensures Phase C only handles residual surplus/deficit not cleared within a realm by Phase A+B. InterRealmTradeSystem retains deficit scan and price discovery (no trade execution).
