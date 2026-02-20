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
7. Production Chains

**Planned chains:**

- **Ale → brewery** (Food → Ale). Remove Ale from biome extraction table. Ale now only comes from breweries in grain-producing counties.
- **Timber → Lumber** (sawmill). Lumber replaces Timber in construction/admin consumption.
- **IronOre → IronIngots** (smelter). IronIngots replace IronOre in admin/military consumption.
- **Wool → Cloth** (weaver). Cloth replaces Wool in population comfort consumption.
- **Finished goods** (multi-input): Tools (IronIngots + Lumber → smithy). Deferred until single-input chains are stable.

**Per-chain migration checklist:**

1. **Add output good** to `GoodType` enum and `Goods.Defs[]` (consumption, admin rates, price band, need category, spoilage)
2. **Add facility** to `FacilityType` enum and `Facilities.Defs[]` (input/output good + amounts, labor, placement threshold)
3. **Update `BiomeProductivity`** — add column for new good (if raw) or zero column (if refined-only)
4. **Reclassify input good** if it becomes a pure facility input:
   - Zero out its `ConsumptionPerPop` and admin rates in `Goods.Defs[]` (so `HasDirectDemand` becomes false)
   - Move its old consumption/admin rates to the new output good
   - It will automatically become demand-driven extraction + tax-exempt via Phase D logic
5. **Update `BuyPriority`** in `Goods` — insert new good at appropriate priority
6. **Update `analyze_econ.py`** — production chain reporting picks up new facilities automatically
7. **Update `EconDebugBridge`** — facility dump picks up new facilities automatically
8. **Test** — run `generate_and_run`, verify: output covers old consumption, input extraction matches facility demand, tax/trade/relief flows correct for new good

### Future Layers (unordered)

- **Weather/Seasonality**
- **Money circulation + market tolls** — Domestic money flow and market-driven production.
  - Counties and provinces hold coin balances (treasury at every tier)
  - Feudal tax phases pay for goods taken: realm pays province, duke pays county, at administered prices from treasury
  - Counties respond to high market prices by producing above quota (price signal = demand signal)
  - Feudal hierarchy can't tax goods that counties sell directly at market — shifts from in-kind taxation to transaction tolls:
    1. **Market toll** — host county collects a fee on every transaction (funds market town growth)
    2. **Ducal toll** — duke of the selling county takes a cut of coin revenue
    3. **Royal toll** — king takes a cut above that
  - Creates circular money flow: mint → royal treasury → provinces/counties (paying for taxed goods) → market (selling surplus) → tolls back up the hierarchy
  - Market towns accumulate coin from tolls → grow in population → attract more trade (feedback loop)
  - Solves pottery export problem: currently low trade volume because no incentive to overproduce; with coin incentive, kiln counties produce above quota and sell at market price
- **Labor specialization** — unskilled vs craftsman, skill acquisition from employment
- **Road emergence** — traffic volume builds paths → roads, reducing transport cost
- **Political effects** — tariffs at realm borders, trade agreements
- **Black markets** — theft, smuggling, informal economy
