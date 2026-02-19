# Economy Design

### Philosophy

Build the economy in layers, each adding one mechanism. Every layer must run and produce observable data before the next begins. No speculative infrastructure.

Update the econ debug bridge and `analyze_econ.py` with every change.

[Completed Layers](./economy-complete.md)

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

One market at a realm capital. Realms sell surplus goods for coin and buy deficit goods with coin. Market sets prices from supply and demand. Use Phase A data to calibrate minting ratio before building the market.

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
