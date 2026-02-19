# Economy Design

### Philosophy

Build the economy in layers, each adding one mechanism. Every layer must run and produce observable data before the next begins. No speculative infrastructure.

Update the econ debug bridge and `analyze_econ.py` with every change.

### [Completed Layers](./economy-complete.md)

1. Autarky
2. Feudal Tax Redistribution
3. Multiple Goods
4. Inter-Realm Trade

## To Do

### Layer 5: Population Dynamics

Static population → dynamic. Growth from food surplus, decline from starvation, migration toward prosperity. Monthly tick evaluates conditions over ~30 daily economic cycles.

- Growth/decline driven by sustained food satisfaction (not single-day spikes)
- Migration toward counties with better conditions
- Monthly update reads back over daily TimeSeries for averaging
- Observable: growing vs shrinking counties, migration flows, carrying capacity emergence

### Layer 6: Production Chains

Raw goods → refined goods → finished goods. Facilities transform inputs to outputs.

- Facilities placed by geography (sawmill near forest, smelter near ore)
- Labor requirements from county population
- Processing takes time (input consumed, output buffered)
- Refactor goods from enum + parallel arrays to `GoodDef` registry — each good needs metadata (inputs, outputs, processing time, facility type, category, tradeability)

### Future Layers (unordered)

- **Labor specialization** — unskilled vs craftsman, skill acquisition from employment
- **Decay/spoilage** — perishable goods lose value over time
- **Road emergence** — traffic volume builds paths → roads, reducing transport cost
- **Political effects** — tariffs at realm borders, trade agreements
- **Black markets** — theft, smuggling, informal economy
