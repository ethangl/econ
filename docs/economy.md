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
7. Production Chains (Phases A–C)

## To Do

### Layer 7: Production Chains — Phase D: Chain Migration

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
