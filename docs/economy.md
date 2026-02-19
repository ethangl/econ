# Economy Design

### Philosophy

Build the economy in layers, each adding one mechanism. Every layer must run and produce observable data before the next begins. No speculative infrastructure.

Update the econ debug bridge and `analyze_econ.py` with every change.

[Completed Layers](./economy-complete.md)

## Layer 4: Inter-Realm Trade

Price-driven trade between realms at market towns. This is where prices emerge — feudal redistribution is administrative, but cross-border exchange requires negotiation.

### Phase A: Resource Expansion + Gold Regal Rights

Split ore into iron and gold, add salt and wool. Create geographic resource diversity so realms have distinct surpluses and deficits. Gold ore is crown property (regal right).

**GoodType:** Food, Timber, IronOre, GoldOre, Salt, Wool (6 goods)

**Consumption (kg/person/day):** Food 1.0 (staple), Timber 0.2, Wool 0.1, Salt 0.05, IronOre 0.01, GoldOre 0.0 (not consumed, minted)

**Feudal redistribution:** Gold ore taxed at 100% (regal right). All other goods remain at 20%.

**Biome productivity (kg/person/day):**

| Id  | Biome               | Food | Timber | Iron | Gold | Salt | Wool |
| --- | ------------------- | ---- | ------ | ---- | ---- | ---- | ---- |
| 0   | Glacier             | 0.0  | 0.0    | 0.0  | 0.0  | 0.0  | 0.0  |
| 1   | Tundra              | 0.2  | 0.0    | 0.0  | 0.0  | 0.0  | 0.0  |
| 2   | Salt Flat           | 0.1  | 0.0    | 0.0  | 0.0  | 0.4  | 0.0  |
| 3   | Coastal Marsh       | 0.7  | 0.0    | 0.0  | 0.0  | 0.3  | 0.0  |
| 4   | Alpine Barren       | 0.2  | 0.0    | 0.3  | 0.02 | 0.0  | 0.0  |
| 5   | Mountain Shrub      | 0.4  | 0.1    | 0.2  | 0.01 | 0.0  | 0.0  |
| 6   | Floodplain          | 1.4  | 0.0    | 0.0  | 0.0  | 0.0  | 0.0  |
| 7   | Wetland             | 0.7  | 0.1    | 0.0  | 0.0  | 0.05 | 0.0  |
| 8   | Hot Desert          | 0.3  | 0.0    | 0.2  | 0.0  | 0.0  | 0.0  |
| 9   | Cold Desert         | 0.3  | 0.0    | 0.2  | 0.0  | 0.0  | 0.0  |
| 10  | Scrubland           | 0.5  | 0.1    | 0.1  | 0.0  | 0.0  | 0.1  |
| 11  | Tropical Rainforest | 0.8  | 0.5    | 0.0  | 0.0  | 0.0  | 0.0  |
| 12  | Tropical Dry Forest | 0.9  | 0.4    | 0.0  | 0.0  | 0.0  | 0.0  |
| 13  | Savanna             | 1.1  | 0.1    | 0.0  | 0.0  | 0.0  | 0.2  |
| 14  | Boreal Forest       | 0.6  | 0.5    | 0.0  | 0.0  | 0.0  | 0.0  |
| 15  | Temperate Forest    | 0.9  | 0.5    | 0.0  | 0.0  | 0.0  | 0.0  |
| 16  | Grassland           | 1.3  | 0.0    | 0.0  | 0.0  | 0.0  | 0.3  |
| 17  | Woodland            | 1.0  | 0.3    | 0.0  | 0.0  | 0.0  | 0.05 |
| 18  | Lake                | 0.0  | 0.0    | 0.0  | 0.0  | 0.0  | 0.0  |

### Phase B: Minting + Single Market, Inter-Realm Trade

Realms mint gold ore into coins (treasury). One market at a realm capital. Realms sell surplus goods for coin and buy deficit goods with coin. Market sets prices from supply and demand. Use Phase A data to calibrate minting ratio before building the market.

- Treasury tracked on RealmEconomy
- Realm mints gold ore into coins (gold ore consumed)
- Realms bring surplus to market, buy deficit goods with coin
- Market clears orders, prices emerge from supply/demand

## Layer 5: Production Chains

Raw goods → refined goods → finished goods. Facilities transform inputs to outputs.

- Facilities placed by geography (sawmill near forest, smelter near ore)
- Labor requirements from county population
- Processing takes time (input consumed, output buffered)
- Refactor goods from enum + parallel arrays to `GoodDef` registry — each good needs metadata (inputs, outputs, processing time, facility type, category, tradeability)

## Later Layers (unordered)

- **Population dynamics** — growth from surplus food, decline from starvation, migration toward prosperity
- **Labor specialization** — unskilled vs craftsman, skill acquisition from employment
- **Decay/spoilage** — perishable goods lose value over time
- **Road emergence** — traffic volume builds paths → roads, reducing transport cost
- **Political effects** — tariffs at realm borders, trade agreements
- **Black markets** — theft, smuggling, informal economy
