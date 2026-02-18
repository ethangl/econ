# Economy Design

## Philosophy

Build the economy in layers, each adding one mechanism. Every layer must run and produce observable data before the next begins. No speculative infrastructure.

## Layer 1: Autarky (current)

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

### Observed Behavior (12 months, 651 counties)

- Avg productivity 1.03, range 0.57–1.34
- 360 surplus counties (productivity > 1.0), 291 starving (productivity < 1.0)
- Steady state reached immediately — surplus counties accumulate linearly, starving counties sit at zero stock with constant unmet need
- Economy system: ~0.16ms/tick

## Layer 2: Local Trade

Counties within a realm can transfer surplus to deficit counties. No prices yet — just redistribution.

- Surplus counties export excess stock to the nearest deficit county (by transport cost)
- Transport cost reduces goods in transit (existing `TransportGraph` efficiency formula)
- Snapshot adds: total traded, total transport loss

**Goal:** Starving counties get partially fed. Total unmet need drops but doesn't hit zero (transport losses, geography).

## Layer 3: Prices

Goods get a price. Supply/demand imbalance at each county drives price up or down.

- Counties with surplus sell at lower prices, deficit counties bid higher
- Trade becomes directional: goods flow from low-price to high-price counties
- Price acts as a signal — no central planner, just local price gradients

## Layer 4: Multiple Goods

Split "Goods" into distinct types (food, timber, ore). Each biome produces different goods at different rates. Counties now have comparative advantage.

- Biome productivity table expands: each biome produces a mix of goods
- Consumption requires specific goods (food is essential, timber/ore are optional)
- Trade becomes interesting — a forest county exports timber, imports food

## Layer 5: Production Chains

Raw goods → refined goods → finished goods. Facilities transform inputs to outputs.

- Facilities placed by geography (sawmill near forest, smelter near ore)
- Labor requirements from county population
- Processing takes time (input consumed, output buffered)

## Layer 6: Markets

Physical market towns aggregate regional supply/demand. Prices discovered at markets rather than per-county.

- Market placement by geographic suitability (rivers, coast, centrality)
- Counties assigned to nearest market by transport cost
- Market clears buy/sell orders, sets prices
- Inter-market trade for goods not locally available

## Later Layers (unordered)

- **Population dynamics** — growth from surplus food, decline from starvation, migration toward prosperity
- **Labor specialization** — unskilled vs craftsman, skill acquisition from employment
- **Decay/spoilage** — perishable goods lose value over time
- **Road emergence** — traffic volume builds paths → roads, reducing transport cost
- **Political effects** — tariffs at realm borders, trade agreements
- **Black markets** — theft, smuggling, informal economy
