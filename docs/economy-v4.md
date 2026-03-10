# Economy v4

## Design Principles

1. **Buy/sell orders as the universal abstraction.** Every economic action is expressed as an order on a market. No special-case systems for durables, granaries, or trade scopes.
2. **One resolution mechanism.** Markets resolve supply and demand in a single pass. No multi-phase fiscal pipeline, no two-pass production or consumption.
3. **Quantity theory of money.** Money is real — minted from gold, destroyed by hoarding and trade outflow. The money supply relative to real output determines the price level. Realms with gold mines are rich but leak coin.
4. **Honest abstraction.** Everything executes in one tick. Don't fake temporal ordering with phase sequencing. If we can't model causation within a tick, accept that and let the market absorb it.
5. **Simplicity over fidelity.** Fewer goods, fewer systems, fewer interactions. Complexity should come from emergent behavior, not from hand-tuned subsystems.

## Goods (20)

**Design rule:** No intermediates. If a raw material only exists to feed one finished good, collapse it — the finished good comes directly from the biome.

### Biome-Extracted (14)

| Good    | Category | Notes                                         |
| ------- | -------- | --------------------------------------------- |
| Wheat   | Staple   | Primary staple, bakery input                  |
| Barley  | Staple   | Secondary staple, brewery input               |
| Fish    | Staple   | Coastal regions                               |
| Meat    | Staple   | Livestock regions                             |
| Salt    | Basic    | Universal need                                |
| Timber  | Basic    | Construction, carpentry input                 |
| Stone   | Basic    | Construction                                  |
| Iron    | Basic    | Mountain regions, smithy input                |
| Wool    | Basic    | Pastoral regions, weaver input                |
| Leather | Comfort  | Livestock regions (hides+tanning collapsed)   |
| Wine    | Luxury   | Mediterranean biomes (grapes collapsed)       |
| Spices  | Luxury   | Rare tropical biomes or trade-only            |
| Silk    | Luxury   | Rare biomes or trade-only                     |
| Candles | Comfort  | Pastoral/forest biomes (tallow/wax collapsed) |

### Facility-Produced (5)

| Good      | Category | Facility  | Inputs        |
| --------- | -------- | --------- | ------------- |
| Bread     | Comfort  | Bakery    | Wheat         |
| Ale       | Comfort  | Brewery   | Barley        |
| Tools     | Comfort  | Smithy    | Iron + Timber |
| Clothes   | Comfort  | Weaver    | Wool          |
| Furniture | Comfort  | Carpenter | Timber        |

### Special (1)

| Good | Notes                                                 |
| ---- | ----------------------------------------------------- |
| Gold | Mined, minted into coin. Not traded as a normal good. |

No intermediates. All 1:1 chains collapsed into biome extraction. Smithy uses Timber directly (fuel for the forge).

**20 goods total.** (14 biome + 5 facility + 1 special)

**Future:** A resource-capacity concept for biomes could allow goods like Jewelry to emerge from biomes with amber or silver deposits, without adding those raw materials as explicit goods.

## Population Classes (Estates)

Six estates from the existing `Estate` enum, with distinct economic roles and relationships to money.

### Lower Commoners (serfs, peasants) — ~81% of population

- **Production:** biome extraction. Raw volume = lower pop × biome yield per capita.
- **Consumption:** local subsistence only. Eat what they grow. No buy orders, no coin.
- **Surplus:** production beyond local needs becomes sell orders. **Revenue goes to the county lord's treasury**, not to the peasants. They are serfs, bound to the land.
- **Lord's obligation:** the lord must keep peasants alive to keep production going. If peasants starve, output drops, lord's income drops. This is the implicit feedback loop.
- **Satisfaction:** based on subsistence fulfillment (did biome yield cover needs?) and whether the lord covered any deficit by buying food. No market interaction.
- **Migration:** least mobile class. Bound to the land — migration is slow and driven by extreme dissatisfaction only.

### Upper Commoners (artisans, merchants) — ~15% of population

- **Production:** facility operation. Sell order volume based on last tick's input availability.
- **Consumption:** staples + basics + comforts. Post buy orders with coin budgets.
- **Coin:** upper commoners hold their own coin, earned from facility sales. They are the market participants — the only class that both earns and spends coin.
- **Inputs:** facilities generate buy orders for raw inputs (wheat for bakery, iron for smithy).
- **Tax:** lord skims a percentage of upper commoner buy-side transactions.
- **Satisfaction:** based on buy order fulfillment across need tiers.
- **Migration:** most mobile class. Attracted to counties with facilities, demand for processed goods, and high satisfaction.

### Elite Estates (no production, treasury-funded consumption)

Four elite estates, each with separate treasuries and spending priorities:

**Upper Nobility (count/duke) — ~0.2% of population**

- **Income:** peasant surplus revenue, transaction tax on upper commoner buys, trade tariffs
- **Spending priority:** serf feeding → lower noble stipends → staples → basics → comforts → luxuries (future: clergy endowments, military)
- **Effect:** the lord is the intermediary between the subsistence economy and the money economy. Peasants never touch coin. The lord converts grain surplus into purchasing power.

**Lower Nobility (knights, minor lords) — ~1.8% of population**

- **Income:** stipend from upper nobility
- **Spending priority:** staples → basics → comforts → luxuries (future: military equipment)
- **Effect:** secondary demand source. Distributes coin further into the upper commoner economy.

**Upper Clergy (bishops, abbots) — ~0.2% of population**

- **Income:** tithe (% of upper commoner buy-side transactions — same mechanism as tax, different recipient)
- **Spending priority:** lower clergy wages → candles → wine → staples → comforts
- **Effect:** independent income via tithes. The church's financial independence from the crown is historically significant.

**Lower Clergy (parish priests, monks) — ~1.8% of population**

- **Income:** wages from clergy treasury (upper clergy distributes to lower)
- **Spending priority:** staples → basics → candles
- **Effect:** local religious presence. Consumes modestly, provides the religious satisfaction that peasants depend on.

All four elite estates share the pattern: no production, treasury-funded buy orders, pure demand. Their spending is the primary mechanism that feeds coin into the upper commoner economy.

### Coin Ownership

Five pools per county:

| Pool                          | Funded by                                                                      | Spent on                                                                            |
| ----------------------------- | ------------------------------------------------------------------------------ | ----------------------------------------------------------------------------------- |
| **Upper noble treasury**      | Peasant surplus sales, transaction tax, trade tariffs                          | Serf feeding, lower noble stipends, staples, basics, comforts, luxuries             |
| **Lower noble treasury**      | Stipend from upper nobility                                                    | Staples, basics, comforts, luxuries                                                 |
| **Upper clergy treasury**     | Tithe on upper commoner buys                                                   | Lower clergy wages, candles, wine, staples, comforts                                |
| **Lower clergy coin**         | Wages from upper clergy treasury                                               | Staples, basics, candles                                                            |
| **Upper commoner coin (= M)** | Facility sales revenue (from elite purchases + other upper commoner purchases) | Facility inputs, staples, basics, comforts                                          |

Lower commoners have no coin. They exist entirely in the subsistence economy.

**M (money supply) = upper commoner coin + lower clergy coin.** Elite treasuries (upper noble, lower noble, upper clergy) are not "in circulation." When elites spend (buy orders), coin moves from their treasury into M. When elites tax/tithe, coin moves from M into their treasuries. This makes "hoarding is deflationary" literally true.

Coin circulates: elite taxes/collects → elite buys goods → upper commoners earn → upper commoners buy → elite taxes → ...

### Economic Geography

- **Rural counties:** mostly lower commoners. Lord collects grain surplus, sells it. Low coin circulation, few facilities.
- **Urban counties:** mixed lower + upper. Lord's spending attracts artisans. Facilities process raw goods into finished goods. Higher coin circulation.
- **Capital counties:** large noble/clergy presence. Lord spends heavily on luxuries. Draws upper commoners from across the realm. Highest prices, most coin.

**Open question:** What drives class ratio shifts over time? Upper commoner migration toward high-satisfaction, high-demand counties is the primary mechanism. Exact mechanics TBD.

## Production

Two production modes, one per productive class:

### Biome Extraction (lower commoners)

- Raw volume = lower commoner population × biome yield per good per capita
- **Subsistence first:** peasants eat what they need from their own production. Only the surplus reaches the market.
- If production > local need: surplus becomes sell orders, revenue goes to lord's treasury
- If production < local need: no sell orders. The deficit must be covered by the lord buying food on the market (from his treasury) and feeding his serfs. A lord with no coin and no local food production has a dying county.
- No facility needed — the biome IS the means of production
- A self-sufficient county barely interacts with the money economy — historically accurate for subsistence agriculture

### Facility Production (upper commoners)

- **Sell orders based on last tick's input availability.** A facility looks at what it successfully bought last tick, and posts sell orders for the corresponding output this tick. A new facility (or one that couldn't source inputs) posts zero sell orders.
- **Buy orders based on desired throughput.** Facilities always post buy orders for inputs at full capacity. Whether they're filled depends on market resolution and the facility's coin budget.
- Volume = upper commoner population × facility throughput per capita (capped by last tick's input fill rate)
- One-tick production lag for processed goods. Raw extraction is instant.

Production generates sell orders (from last tick's inputs) and facility input buy orders (for next tick). That's it.

### Facilities (5)

| Facility  | Input         | Output    |
| --------- | ------------- | --------- |
| Bakery    | Wheat         | Bread     |
| Brewery   | Barley        | Ale       |
| Smithy    | Iron + Timber | Tools     |
| Weaver    | Wool          | Clothes   |
| Carpenter | Timber        | Furniture |

All other goods (Wine, Leather, Candles, etc.) come directly from biome extraction — no facility needed.

**Facility assignment:** every county has access to all 5 facilities. Whether a facility operates depends on whether upper commoners can source inputs and sell output profitably. A bakery in a county with no wheat access will post buy orders that go unfilled — natural selection through the market.

## Tick Structure

```
1. GENERATE ORDERS
   - Biome extraction (lower commoners) → sell orders (surplus after subsistence)
   - Facility production from last tick's inputs (upper commoners) → sell orders
   - Upper noble buy orders (from treasury — serf feeding, stipends, staples, basics, comforts, luxuries)
   - Lower noble buy orders (from treasury — staples, basics, comforts, luxuries)
   - Upper clergy buy orders (from treasury — lower clergy wages, church needs, candles, wine)
   - Lower clergy buy orders (from coin balance — staples, basics, candles)
   - Upper commoner buy orders (from coin balance — staples, basics, comforts)
   - Cross-market trade from last tick's prices → buy/sell orders posted to both markets

2. RESOLVE MARKETS
   - Per market, per good: compute clearing price from scarcity formula × price level
   - Buy side: fill orders by descending bid until supply exhausted or all filled
   - Sell side: if demand < supply, all sellers move proportionally (no priority ordering)
   - Unfilled buy orders: buyer keeps coin, need goes unmet
   - Unfilled sell orders: goods are lost (no inventory)
   - Tax: upper noble treasury += tax_rate × transaction value on upper commoner buys
   - Tithe: clergy treasury += tithe_rate × transaction value on upper commoner buys

3. UPDATE MONEY
   - Minting: gold production → new coin enters upper noble treasury
   - Stipend: upper noble treasury → lower noble treasury (fixed amount per tick)
   - Clergy wages: upper clergy treasury → lower clergy coin (fixed amount per tick)
   - Trade coin flows: net importers lose coin to net exporters
   - Wear: small constant drain on M (upper commoner circulating coin)

4. UPDATE SATISFACTION
   - Lower commoners: subsistence fulfillment + lord feeding + stability + religion
   - Upper commoners: staple fulfillment + comfort fulfillment + stability + religion
   - Lower clergy: staple fulfillment + worship fulfillment + stability + religion
   - Upper nobility/lower nobility: luxury fulfillment + stability + religion
   - Upper clergy: worship fulfillment + luxury fulfillment + stability + religion
   - Satisfaction drives birth/death/migration
```

No two-pass anything. No phase ordering within steps. Each step depends only on the output of the previous step.

## Pricing Math

### Good Properties

Each good has two designer-set constants:

| Property  | Meaning                              | Example                         |
| --------- | ------------------------------------ | ------------------------------- |
| `value_g` | Relative worth (for pricing)         | Wheat=1, Iron=4, Silk=50        |
| `bulk_g`  | Physical mass/volume (for transport) | Wheat=high, Iron=high, Silk=low |

These are independent. Silk is valuable but light. Wheat is cheap but heavy.

| Tier     | Goods                                                   | Value range | Bulk   |
| -------- | ------------------------------------------------------- | ----------- | ------ |
| Staples  | Wheat, Barley, Fish, Meat                               | 1–2         | high   |
| Basics   | Salt, Timber, Stone, Iron, Wool                         | 2–5         | high   |
| Comforts | Bread, Ale, Tools, Clothes, Furniture, Leather, Candles | 5–15        | medium |
| Luxuries | Wine, Spices, Silk                                      | 20–50       | low    |

Exact values TBD through playtesting.

### Layer 1: Clearing Price (per good, per market)

The buy/sell order ratio determines how scarce each good is:

```
scarcity_g = clamp((buy_g - sell_g) / max(min(buy_g, sell_g), 1), -1, +1)
clearing_price_g = value_g × (1 + 0.75 × scarcity_g) × price_level
```

- Range: 0.25× to 1.75× the good's base value, scaled by price level
- `max(..., 1)` floor prevents division by zero when one side has no orders
- At equilibrium (buy = sell): clearing price = value_g × price_level

### Layer 2: Price Level (per market)

From quantity theory of money (MV = PQ):

```
price_level = max((M × V) / Q, 1.0)
```

- **M** = upper commoner coin + lower clergy coin in circulation across this market's counties (elite treasuries excluded)
- **V** = velocity of money. Scales with population density: `V = base_V × log(pop_density)`. Cities have higher velocity (more transactions per coin per tick) than rural counties.
- **Q** = total real output = Σ(sell_g × value_g) across all goods g
- **Floor of 1.0** ensures prices are meaningful even when M ≈ 0. In low-money economies, goods trade at their real value. As coin enters circulation, inflation kicks in naturally.

### Market Resolution (per good, per market)

Buy orders include a **max bid** — the most the buyer is willing to pay per unit, based on their coin budget. Resolution fills orders by ability to pay:

```
1. Sum all sell orders → total_supply
2. Sum all buy orders → total_demand
3. Compute clearing_price from scarcity formula
4. Sort buy orders by max_bid descending
5. Fill buy orders top-down:
   - If bid ≥ clearing_price → filled (buyer spends coin, receives goods)
   - If bid < clearing_price → unfilled (buyer is priced out)
6. All filled transactions occur at the clearing_price
   Coin routing by seller type:
   - Peasant surplus sell order → coin to selling county's upper noble treasury
   - Facility sell order → coin to upper commoner coin pool (M)
   - Trade sell order → coin to exporting market's M
7. Tax on upper commoner buys: upper noble treasury += tax_rate × clearing_price × quantity
   Tithe on upper commoner buys: clergy treasury += tithe_rate × clearing_price × quantity
8. Sell side: if total filled demand < total supply, all sellers move proportionally
   - e.g., if 80% of wheat demanded, every wheat seller sells 80% of their volume
   - Unsold goods are lost (no inventory tracking)
```

This means:

- **Elites bid high** (treasury-funded) — filled first
- **Upper commoners bid medium** — usually filled, sometimes priced out of comforts
- **Trade merchants bid based on arbitrage margin** — can outbid locals when price gaps are large
- The lord eats during a famine. The artisan might not. The serf eats what the land gives or what the lord provides.

### Budget Allocation and Bidding

Five types of buyers with separate budgets:

**Upper nobility:** budget = upper noble treasury. Priority:

```
1. Serf deficit  — buy food to cover shortfall in lower commoner subsistence
2. Stipends      — pay lower nobility (fixed transfer, not a buy order)
3. Staples       — household food
4. Basics        — household basics
5. Comforts      — processed goods
6. Luxuries      — wine, spices, silk
(future: clergy endowments, military spending)
```

Feeding serfs comes first. A lord who lets serfs starve to buy silk will lose his productive base.

**Lower nobility:** budget = lower noble treasury (stipend from upper noble). Priority:

```
1. Staples   — household food
2. Basics    — household basics
3. Comforts  — processed goods
4. Luxuries  — wine, spices, silk
(future: military equipment)
```

**Upper Clergy:** budget = upper clergy treasury (tithe income). Priority:

```
1. Lower clergy wages — pay parish priests (fixed transfer, not a buy order)
2. Candles   — worship (high priority for clergy)
3. Wine      — sacramental
4. Staples   — household food
5. Comforts  — church maintenance, furnishing
```

**Lower Clergy:** budget = their coin balance (from upper clergy wages). Priority:

```
1. Staples   — household food
2. Basics    — household basics
3. Candles   — worship
```

**Upper commoners:** budget = their coin balance (from last tick's facility sales + savings). Allocated across needs:

```
1. Staples  — up to X% of budget
2. Basics   — up to Y% of remaining
3. Comforts — up to Z% of remaining
4. Luxuries — up to W% of remaining (small — aspirational spending)
```

Within a tier, bid per unit = budget allocated to that tier / total units needed.

**Trade buy orders** are funded by the importing market's coin. The merchant bids at `last_price_g_B - transport_cost` — what the good is worth at the destination minus costs. Foreign merchants can outbid locals when the price gap is large. Lords who don't like it impose tariffs.

### Self-Correction

The system is self-correcting through affordability, not through supply/demand responding to price:

- **High prices → buyers can't afford → effective demand drops → scarcity falls → prices drop**
- **Low prices → buyers have surplus coin → demand for comforts/luxuries rises**
- Raw production (biome yields) doesn't respond to price — the land gives what it gives
- Facility production responds indirectly: if output price < input cost, the facility is unprofitable and upper commoners migrate away over time

## Market Structure

Markets map to the existing geographic model:

- MarketPlacer assigns counties to markets based on proximity and transport cost
- Each market has a hub county
- All counties in a market post orders to that market
- Markets resolve independently, then cross-market trade connects them
- Markets require sufficient monetary circulation to exist — counties without access to coin subsist outside the market system

## Cross-Market Trade

No trade scopes. One mechanism, driven by last tick's prices (avoids circular dependency — merchants decide based on yesterday's information):

### Price Differential

```
price_diff_g = last_price_g_B - last_price_g_A
transport_cost_g = distance(A, B) × bulk_g × transport_rate
profit_g = price_diff_g - transport_cost_g
```

- `distance(A, B)` = TransportGraph cost between market hubs (terrain-weighted)
- `transport_rate` = base cost per unit of bulk per unit of distance
- Bulky goods (high bulk_g) cost more to move — wheat barely travels, silk goes everywhere
- This naturally produces medieval trade patterns: long-distance trade in luxuries, local trade in staples

### Trade Volume

```
// All values from LAST TICK (merchants decide on yesterday's information)
last_surplus_g_A = last_sell_g_A - last_filled_buy_g_A   // unsold supply in exporting market
last_deficit_g_B = last_unfilled_buy_g_B                  // unmet demand in importing market
max_volume_g = min(last_surplus_g_A, last_deficit_g_B)

margin_g = profit_g / last_price_g_A    // profit as fraction of buy price
trade_volume_g = max_volume_g × clamp(margin_g, 0, 1)
```

- All trade decisions use last tick's data — prices, surplus, deficit. No circular dependency with current-tick orders.
- Can't export more than last tick's surplus or more than the destination's unmet demand
- Merchants scale commitment by profit margin — thin margins move a trickle, fat margins move everything
- This means:
  - **Adjacent markets with similar prices barely trade** (low margin, not worth the cart)
  - **Distant markets only trade high-value goods** (transport cost eats margin on cheap bulk)
  - **Gold-rich markets import heavily** (high local prices = fat margins for importers)

### Trade Orders

Trade is expressed as orders posted to both markets:

- Sell order of trade_volume_g posted in market B (goods arriving)
- Buy order of trade_volume_g posted in market A (goods leaving)
- These orders participate in normal market resolution

### Coin Flow

Net coin flows opposite to goods:

- Market B (importer) sends coin to Market A (exporter)
- Amount = trade_volume_g × last_price_g_A (paid at last tick's source market price, consistent with all trade decisions using last tick's data)
- This is the quantity-theory feedback: gold-rich markets leak coin through imports, naturally equalizing price levels over time

### Lord's Cut

The realm controlling a market's territory can impose a tariff (percentage skim on cross-market transactions). This is the only policy lever for trade. Simple, historically appropriate.

## Monetary System

### Money Creation

- Gold mines produce gold based on biome yields and lower commoner population
- A fraction of gold production is minted into coin per tick
- Minting rate may be capped by a mint facility or just a fixed fraction
- New coin enters the lord's treasury of the realm that controls the mine

### Money Destruction / Drain

- **Trade outflow:** buying from foreign markets sends coin to those markets
- **Hoarding:** lord's treasury accumulates coin not yet spent (reduces M indirectly — coin sitting in treasury isn't in circulation)
- **Wear:** small constant percentage of M (upper commoner coin) lost per tick (coin degradation)
- **Trade fees:** a fraction of the transport cost could destroy coin (friction loss)

### Low-Money Economies

Most realms won't have gold mines. They operate with low money supply:

- Low price levels (goods are "cheap" in nominal terms)
- Real goods matter more than coin
- Trade with gold-rich neighbors brings in coin, raising prices
- A market with near-zero money supply won't be a market for long — counties revert to subsistence

This is historically accurate — medieval economies outside gold/silver regions were largely subsistence with limited monetary circulation.

### Emergent Dynamics

The monetary system creates natural feedback loops:

- **Gold realm inflation:** minting → lord spends → high M → high prices → imports are cheap → coin drains abroad → M falls → prices correct
- **Trade equalization:** coin flows from high-price to low-price markets, raising prices in the destination, lowering in the source
- **Hoarding as deflation:** lord taxes heavily and doesn't spend → M drops → prices fall → exports become cheap → foreigners buy → coin returns (to lord's treasury, not to M — lord must spend for it to help)
- **Subsistence trap:** no gold, no trade surplus → no coin → no market → no specialization → low output. Breaking out requires a neighbor with coin wanting your goods.

## Taxation and Tithes

Upper commoners pay two skims on every buy-side transaction:

1. **Tax** → upper noble treasury. Rate set by the lord (tax_rate).
2. **Tithe** → clergy treasury. Rate is fixed (tithe_rate, historically ~10%).

Both use the same mechanism: skim = rate × clearing_price × quantity on filled buy orders. Upper commoners pay both on every purchase. Lower clergy are exempt from tax and tithe (clerical privilege).

Additionally:

- **Peasant surplus revenue** → upper noble treasury. The lord captures all coin from lower commoner sell orders. Not a "tax" — it's serfdom.
- **Trade tariffs** → upper noble treasury. Lord skims cross-market trade value.
- **Lower noble stipend** → upper noble treasury pays out a fixed amount per tick to lower noble treasury.

Elite treasuries are money sinks. Coin re-enters M when elites buy goods.

- A lord who taxes heavily and hoards causes deflation — upper commoners leave
- A lord who spends freely stimulates the local economy — upper commoners flock in
- A lord with gold mines and high spending creates a boom town
- A wealthy church (high tithe income, high spending on candles/wine) creates demand for specific goods

## Satisfaction

**Per-class, per-county.** Satisfaction is primarily about survival, faith, and safety — not economic comfort. Economic fulfillment is a secondary modifier.

### Satisfaction Components

| Component      | Weight         | Notes                                                                                                                                     |
| -------------- | -------------- | ----------------------------------------------------------------------------------------------------------------------------------------- |
| **Survival**   | heaviest       | Am I eating? Am I alive? Dominates all other factors.                                                                                     |
| **Religion**   | heavy          | Derived from clergy buy order fulfillment. A well-funded church (candles, wine) provides religious satisfaction. A broke church does not. |
| **Stability**  | heavy          | Peace vs war, raiding, recent conquest.                                                                                                   |
| **Economic**   | moderate       | Buy order fulfillment for goods beyond survival. Class-dependent.                                                                         |
| **Governance** | light          | Tax burden fairness, lord's legitimacy.                                                                                                   |
| **Health**     | light (future) | Plague, overcrowding. Not in v4 initial.                                                                                                  |

Survival dominates. A well-fed serf in a warzone is unhappy. A comfortable merchant under a heretic lord is unhappy. A starving peasant is desperate regardless of everything else.

### Per-Class Details

**Lower commoners:**

- Survival = (local staple production + lord-provided food) / staple need. Staples only — salt and timber don't affect survival.
- Religion = clergy buy order fulfillment in this county (well-funded church → spiritual comfort)
- Stability = peace/war status
- No economic component (they don't participate in the market)

**Upper commoners:**

- Survival = staple buy order fulfillment
- Religion = clergy buy order fulfillment in this county
- Stability = peace/war status
- Economic = comfort buy order fulfillment (bread, ale, tools, clothes, etc.)

**Lower Clergy:**

- Survival = staple buy order fulfillment (they buy their own food from wages)
- Religion = clergy buy order fulfillment (their mission — they care deeply)
- Stability = peace/war status
- Economic = candle/worship buy order fulfillment

**Upper Elites (upper/lower nobility, upper clergy):**

- Survival = rarely an issue (bid highest, filled first)
- Religion = clergy buy order fulfillment (upper clergy care about this most — it's their mission)
- Stability = peace/war status (nobility care most — they fight)
- Economic = luxury buy order fulfillment (wine, spices, silk)

### Effects

Satisfaction per class drives:

- **Birth rate:** higher satisfaction → more births (per class)
- **Death rate:** low satisfaction → more deaths (survival component dominates — you die from hunger, not lack of furniture)
- **Migration:** upper commoners flow toward higher satisfaction. Lower commoners are mostly immobile. Elites don't migrate (they own the land).

## Tuning Parameters

All values TBD through simulation. This section enumerates the knobs that exist.

### Per-Good Constants

| Parameter | Meaning                          | Example              |
| --------- | -------------------------------- | -------------------- |
| `value_g` | Relative worth for pricing       | Wheat=1, Silk=50     |
| `bulk_g`  | Physical mass for transport cost | Wheat=high, Silk=low |

### Per-Biome, Per-Good Yields

How much of each good a biome produces per lower commoner per tick. Most biome/good combinations are zero.

| Biome         | Wheat | Barley | Fish | Meat | Salt | Timber | Stone | Iron | Wool | Leather | Wine | Candles | Silk | Spices | Gold |
| ------------- | ----- | ------ | ---- | ---- | ---- | ------ | ----- | ---- | ---- | ------- | ---- | ------- | ---- | ------ | ---- |
| Grassland     | high  | med    | —    | med  | —    | low    | —     | —    | med  | low     | —    | low     | —    | —      | —    |
| Coast         | low   | —      | high | —    | high | —      | —     | —    | —    | —       | —    | —       | —    | —      | —    |
| Forest        | —     | —      | —    | low  | —    | high   | —     | —    | —    | low     | —    | med     | —    | —      | —    |
| Mountain      | —     | —      | —    | —    | low  | low    | high  | high | —    | —       | —    | —       | —    | —      | low  |
| Steppe        | low   | med    | —    | high | —    | —      | —     | —    | high | med     | —    | low     | —    | —      | —    |
| Mediterranean | med   | med    | —    | —    | —    | low    | low   | —    | —    | —       | high | —       | low  | —      | —    |
| Desert        | —     | —      | —    | —    | med  | —      | low   | —    | —    | —       | —    | —       | —    | low    | low  |
| Tropical      | low   | —      | —    | —    | —    | med    | —     | —    | —    | —       | —    | —       | med  | med    | —    |

Collapsed goods and their biome sources:
- **Leather** — from livestock regions (hides + tanning collapsed). Wherever Meat is produced, Leather is a byproduct at lower yield.
- **Wine** — from Mediterranean biomes (grapes + winemaking collapsed). Direct extraction, no facility.
- **Candles** — from pastoral/forest biomes (tallow/wax collapsed). Wherever animals or bees exist.
- **Silk** — rare Mediterranean/tropical biomes. Trade-dependent for most realms.
- **Spices** — rare desert/tropical biomes. Trade-dependent for most realms.
- **Gold** — rare mountain/desert biomes. Minted into coin, not traded as a good.

Key balance constraint: a typical grassland county should produce ~120-150% of its staple needs, creating a modest surplus for trade. A specialized county (mining, pastoral) should produce <50% of its staple needs, forcing trade dependency.

### Per-Class Consumption (per capita per tick)

**Lower commoners** — consumed via subsistence (not market):
| Good | Amount | Notes |
|------|--------|-------|
| Wheat/Barley/Fish/Meat | X | Staple food — whatever the biome produces. Drives survival satisfaction. |
| Salt | X | Small quantity. Consumed if available, no satisfaction impact. |
| Timber | X | Heating, shelter. Consumed if available, no satisfaction impact. |

**Upper commoners** — consumed via market buy orders:
| Good | Amount | Notes |
|------|--------|-------|
| Staples | X | All food purchased — upper commoners don't do biome extraction |
| Salt, Timber, Stone | X | Basic needs |
| Bread, Ale, Tools, Clothes, Furniture, Leather, Candles | X | Comfort goods — the reason they participate in the cash economy |

**Upper nobility** — consumed via treasury buy orders:
| Good | Amount | Notes |
|------|--------|-------|
| Staples (for serf deficit + household) | X | Feeding serfs is priority #1 |
| Basics | X | Household needs |
| Comforts | X | Quality of life |
| Wine, Spices, Silk | X | Prestige luxuries |

**Lower nobility** — consumed via treasury buy orders:
| Good | Amount | Notes |
|------|--------|-------|
| Staples | X | Household food |
| Basics | X | Household needs |
| Comforts | X | Quality of life |
| Wine, Spices, Silk | X | Prestige luxuries |

**Upper clergy** — consumed via treasury buy orders:
| Good | Amount | Notes |
|------|--------|-------|
| Candles, Wine | X | Worship goods — highest priority after clergy wages |
| Staples | X | Household food |
| Comforts | X | Church furnishing |

**Lower clergy** — consumed via coin buy orders:
| Good | Amount | Notes |
|------|--------|-------|
| Staples | X | Household food |
| Basics | X | Household needs |
| Candles | X | Worship |

### Per-Buyer Budget Allocation

What percentage of coin budget each buyer type allocates to each need tier.

| Tier                    | Upper Commoners | Upper Nobility | Lower Nobility | Upper Clergy | Lower Clergy |
| ----------------------- | --------------- | -------------- | -------------- | ------------ | ------------ |
| Serf feeding            | —               | first priority | —              | —            | —            |
| Stipends/Wages          | —               | after serfs    | —              | first priority | —          |
| Staples                 | 40%             | 10%            | 20%            | 15%          | 50%          |
| Basics                  | 25%             | 10%            | 15%            | 10%          | 25%          |
| Comforts                | 30%             | 30%            | 40%            | 35%          | —            |
| Luxuries                | 5%              | 50%            | 25%            | —            | —            |
| Worship (Candles, Wine) | —               | —              | —              | 40%          | 25%          |

### Monetary Parameters

| Parameter        | Meaning                                                                  |
| ---------------- | ------------------------------------------------------------------------ |
| `base_V`         | Base velocity of money (tuning knob for price level)                     |
| `gold_mint_rate` | Fraction of gold production converted to coin per tick                   |
| `coin_wear_rate` | Fraction of M (circulating coin) lost per tick                           |
| `tax_rate`       | Percentage lord skims from upper commoner buy-side transactions          |
| `tithe_rate`     | Percentage clergy skims from upper commoner buy-side transactions (~10%) |
| `stipend_rate`   | Fixed coin amount upper nobility pays to lower nobility per tick         |
| `clergy_wage`    | Fixed coin amount upper clergy pays to lower clergy per tick             |

### Trade Parameters

| Parameter        | Meaning                                         |
| ---------------- | ----------------------------------------------- |
| `transport_rate` | Base cost per unit of bulk per unit of distance |

### Population Parameters

| Parameter                     | Meaning                                                  |
| ----------------------------- | -------------------------------------------------------- |
| `base_birth_rate`             | Births per capita per tick at neutral satisfaction       |
| `base_death_rate`             | Deaths per capita per tick at neutral satisfaction       |
| `satisfaction_birth_modifier` | How much satisfaction scales birth rate                  |
| `satisfaction_death_modifier` | How much low satisfaction increases death rate           |
| `migration_rate`              | Max fraction of population that migrates per tick        |
| `migration_threshold`         | Satisfaction gap needed to trigger migration             |
| `upper_mobility`              | Migration rate multiplier for upper commoners            |
| `lower_mobility`              | Migration rate multiplier for lower commoners (very low) |

### Facility Parameters

| Parameter                   | Per-facility | Meaning                                                     |
| --------------------------- | ------------ | ----------------------------------------------------------- |
| `input_ratio`               | Yes          | Units of input consumed per unit of output                  |
| `throughput_per_capita`     | Yes          | Output per upper commoner per tick                          |
| `max_facility_pop_fraction` | Yes          | Max fraction of county's upper pop that can work a facility |

### Key Balance Relationships

These ratios determine whether the economy is alive or dead:

1. **Surplus ratio** = biome yield / local consumption. Must be >1.0 for food-producing counties to have anything to trade. ~1.2 is a healthy target.
2. **Specialization pressure** = how much a non-food county needs food imports. Mining counties should be hungry enough to trade, but not so hungry they die before trade routes form.
3. **Facility value-add** = output value / input value. Must be >1.0 or nobody runs facilities. Should be high enough to attract upper commoners.
4. **Trade viability** = price differential / transport cost. For staples (high bulk, low value), this limits trade to short distances. For luxuries (low bulk, high value), trade spans the map.
5. **Monetary circulation** = minting rate vs wear rate vs trade drain. Too much minting → hyperinflation. Too little → deflation and market death.
6. **Lord spend ratio** = lord spending / lord income. A lord who spends <50% of income is hoarding, deflating the local economy. A lord who spends >100% is running down the treasury.

## Out of Scope (for now)

These systems from v3 are explicitly dropped. They can be re-added later as modifiers on the buy/sell order system if needed:

- Granary system (provincial staple reserves)
- Theft system (black market)
- Tithe hierarchy (parish → diocese → archdiocese flow — simplified to flat clergy treasury for now)
- Spoilage (perishable goods decay)
- Durable goods planning (two-pass demand)
- Three-scope trade (intra/cross-province/cross-market)
- 8-phase fiscal pipeline
- Precious metal minting as a separate phase (simplified into monetary system)
- Overseas virtual market (replaced by distant markets with high fees)
- Seasonal production modulation (can re-add as sell order modifier)

## Migration from v3

The economy v4 is a clean rebuild. It reuses:

- MapGen pipeline (unchanged)
- MapData, Cell, County, Province, Realm (unchanged)
- TransportGraph (unchanged)
- MarketPlacer (unchanged or lightly modified)
- Biome yield data (reduced good set)
- Rendering stack (unchanged)

It replaces:

- ProductionSystem, ConsumptionSystem, TradeSystem, FiscalSystem, InterRealmTradeSystem
- TheftSystem, SpoilageSystem, TitheSystem
- EconomyState (simplified)
- GoodDef, FacilityDef (reduced)
- Market resolution logic (new)
