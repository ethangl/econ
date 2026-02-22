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
9. County Market Access
10. Market Location + Market Fee
11. Ungated resource extraction + Trading
12. Remove Quotas and In-kind taxation

### Known Issues

**Durable goods need a different production model.** Pottery, furniture, tools, and clothes are not commodities — they don't clear on a market the way grain or salt does. The current price-based extraction throttle treats them like commodities: production overshoots the slow stock-gap demand, stockpiles accumulate, prices crash to the floor, and the throttle strangles production to ~5% of capacity. The result is 95%+ unmet need despite ample inputs and labor. Durables need a stock-gap-driven production signal rather than a market-price-driven one.

### Future Layers (unordered)

- **Market Dynamics** - Counties respond to high market prices by producing above quota (price signal = demand signal).
- **Weather/Seasonality**
- **Money circulation + market tolls** — Market towns accumulate coin from tolls → grow in population → attract more trade (feedback loop)
- **Labor specialization** — unskilled vs craftsman, skill acquisition from employment
- **Road emergence** — traffic volume builds paths → roads, reducing transport cost
- **Political effects** — ~~tariffs at realm borders~~, trade agreements
- **Black markets** — theft, smuggling, informal economy
