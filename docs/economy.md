# Economy Design

## Philosophy

Build the economy in layers, each adding one mechanism. Every layer must run and produce observable data before the next begins. No speculative infrastructure.

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

## Layer 2: Feudal Tax Redistribution (current)

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

**Observed results** (12mo, 433 counties, 5 realms, 31 provinces):

| Model | Starving | Unmet Need |
|---|---|---|
| Autarky | 208 (48%) | 42,934 |
| Duke + King | 66 (15%) | 12,126 |

### Files

- `Economy/CountyEconomy.cs` — adds TaxPaid, Relief per county
- `Economy/ProvinceEconomy.cs` — Stockpile, TaxCollected, ReliefGiven per province
- `Economy/RealmEconomy.cs` — Stockpile, TaxCollected, ReliefGiven per realm
- `Economy/EconomyState.cs` — adds Provinces[], Realms[] arrays
- `Economy/EconomySnapshot.cs` — adds ducal/royal tax/relief/stockpile aggregates
- `Economy/TradeSystem.cs` — ITickSystem (daily): builds province/realm mappings at init, 4-phase tick

## Layer 3: Multiple Goods

Split "Goods" into distinct types (food, timber, ore). Each biome produces different goods at different rates. Counties now have comparative advantage.

- Biome productivity table expands: each biome produces a mix of goods
- Consumption requires specific goods (food is essential, timber/ore are optional)
- Feudal redistribution applies per good type
- Trade becomes interesting — a forest county exports timber, imports food

## Layer 4: Inter-Realm Trade

Price-driven trade between realms at market towns. This is where prices emerge — feudal redistribution is administrative, but cross-border exchange requires negotiation.

- Market placement by geographic suitability (rivers, coast, centrality)
- Counties assigned to nearest market by transport cost
- Market clears buy/sell orders, sets prices
- Inter-market trade for goods not locally available
- Transport cost reduces goods in transit

## Layer 5: Production Chains

Raw goods → refined goods → finished goods. Facilities transform inputs to outputs.

- Facilities placed by geography (sawmill near forest, smelter near ore)
- Labor requirements from county population
- Processing takes time (input consumed, output buffered)

## Later Layers (unordered)

- **Population dynamics** — growth from surplus food, decline from starvation, migration toward prosperity
- **Labor specialization** — unskilled vs craftsman, skill acquisition from employment
- **Decay/spoilage** — perishable goods lose value over time
- **Road emergence** — traffic volume builds paths → roads, reducing transport cost
- **Political effects** — tariffs at realm borders, trade agreements
- **Black markets** — theft, smuggling, informal economy
