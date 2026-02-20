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
8. Domestic cash flow

### Layer 9 (next): County Market Access

Currently the king intermediates all trade (inter-realm only) and counties receive goods solely through feudal relief. This layer makes counties the trading agents, opening markets progressively with tolls replacing in-kind taxation at each political boundary.

Feudal redistribution (FiscalSystem) continues to run — taxes and relief still flow through the hierarchy. County trade is an additional channel that lets counties buy/sell surplus directly, bypassing the feudal pipeline for goods they can afford.

**Phase A: Intra-Province Trade** (Complete)
Counties within the same province trade directly at market prices. Untaxed (or negligible flat fee). The duke's province is a free-trade zone — counties with surplus sell to neighbors with deficit, routed via transport graph.

**Phase B: Cross-Province / Intra-Realm Trade**
Counties trade across province boundaries within the same realm. The buying county pays its own duke a 5% toll on goods entering the province. Creates incentive for provincial self-sufficiency while allowing inter-provincial specialization.

**Phase C: Cross-Realm Trade**
Counties trade across realm borders. The receiving king collects a tariff on imports (on top of any ducal toll). Replaces realm-level InterRealmTradeSystem with county-level actors. Kings set tariff rates rather than personally trading.

### Future Layers (unordered)

- **Weather/Seasonality**
- **Money circulation + market tolls** — Market towns accumulate coin from tolls → grow in population → attract more trade (feedback loop). Counties respond to high market prices by producing above quota (price signal = demand signal).
- **Labor specialization** — unskilled vs craftsman, skill acquisition from employment
- **Road emergence** — traffic volume builds paths → roads, reducing transport cost
- **Political effects** — tariffs at realm borders, trade agreements
- **Black markets** — theft, smuggling, informal economy
