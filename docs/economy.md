# Economy Design

### Philosophy

Build the economy in layers, each adding one mechanism. Every layer must run and produce observable data before the next begins. No speculative infrastructure.

Update the econ debug bridge and `analyze_econ.py` with every change.

### [Completed Layers](./economy-complete.md)

1. Autarky
2. Feudal Tax Redistribution
3. Multiple Goods
4. Inter-Realm Trade
5. Population Dynamics
6. Good Spoilage / Decay

## To Do

### Layer 7: Production Chains

Raw goods → refined goods → finished goods. Facilities transform inputs to outputs. Prove the mechanism with a single new chain (Clay → Pottery), then migrate existing goods once it works.

#### Phase A: Data Model

Static definitions only — no runtime behavior.

- **FacilityDef** readonly struct: `FacilityType`, `Name`, input good + amount, output good + amount, `LaborPerUnit` (workers needed per unit/day output), placement rule (which biomes)
- **FacilityType** enum + `Facilities.Defs[]` registry (parallel to `Goods.Defs[]`)
- **Facility** runtime struct: `FacilityType`, `CountyId`, `CellId`, `Workforce`
- **New goods:**
  - `Clay` — Raw, extracted from biome. Comfort need, inert (0% spoilage). Produced in floodplain, wetland, coastal marsh (river clay deposits).
  - `Pottery` — Refined, produced by kiln. Comfort need, inert. Not in biome table — only comes from facilities.
- **GoodDef entries** for Clay and Pottery: consumption rates, price bands, spoilage, tradeability
- **BiomeProductivity** updated with Clay column
- One **FacilityDef**: Kiln (Clay → Pottery)

Observable: new goods in enum/registry, facility definitions in dump. No runtime effect.

#### Phase B: Placement + Processing

Facilities placed and producing.

- **FacilityPlacer**: at economy init, scan each county's biome composition. Place kilns in counties with Clay productivity.
- **EconomyState** gets `Facility[]` array; `CountyEconomy` gets facility references.
- **FacilityProductionSystem** (daily tick, `ITickSystem`):
  1. Each facility checks county stock for Clay
  2. Pulls input (capped by availability and workforce)
  3. Produces Pottery into county stock
  4. Production rate = `min(inputAvailable / inputRequired, workforce / laborRequired)` × base output
- **Labor allocation**: county population splits between biome extraction and facility work. Facility workers subtracted from extraction headcount (extraction output scales down proportionally). Simple proportional split — each facility gets workers up to its labor demand, remainder does extraction.

Observable: Clay accumulating from biome extraction, Pottery appearing in counties with kilns, Timber/Food extraction slightly reduced in kiln counties due to labor diversion.

#### Phase C: Economic Integration

Pottery wired into the full economic loop.

- **Population consumption**: Pottery as comfort good (specific rate TBD, likely ~0.01 kg/person/day — one pot lasts a long time)
- **Admin consumption**: county/province/realm consume Pottery for administration (storage, vessels)
- **Feudal redistribution**: Clay and Pottery flow through tax/relief (standard 20% rates)
- **Inter-realm trade**: Pottery tradeable with price bands. Realms without clay deposits must import.
- **Spoilage**: both inert (0%)
- **Buy priority**: Pottery slotted after existing comfort goods
- **analyze_econ.py** updated with production chain reporting (facility counts, throughput, refined goods flows)

Observable: full loop — Clay extraction → kiln processing → Pottery in stockpiles → consumption/trade. Counties with kilns become production hubs. Realms without clay biomes import Pottery.

#### Phase D: Chain Migration

With the mechanism proven, add more production chains. Each is a data-driven addition using the same FacilityDef/FacilityProductionSystem infrastructure.

- **Ale → brewery** (Food → Ale). Remove Ale from biome extraction table. Ale now only comes from breweries in grain-producing counties.
- **Timber → Lumber** (sawmill). Lumber replaces Timber in construction/admin consumption.
- **IronOre → IronIngots** (smelter). IronIngots replace IronOre in admin/military consumption.
- **Wool → Cloth** (weaver). Cloth replaces Wool in population comfort consumption.
- **Finished goods** (multi-input): Tools (IronIngots + Lumber → smithy). Deferred until single-input chains are stable.

### Future Layers (unordered)

- **Labor specialization** — unskilled vs craftsman, skill acquisition from employment
- **Road emergence** — traffic volume builds paths → roads, reducing transport cost
- **Political effects** — tariffs at realm borders, trade agreements
- **Black markets** — theft, smuggling, informal economy
