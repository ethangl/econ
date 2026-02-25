# Economy Algorithms

This document describes every algorithm in the economic simulation in plain
language. Indentation represents nesting — loops, conditionals, and sub-steps.

## Overview

The simulation runs on a **fixed-timestep tick loop** where each tick represents
one day. Five systems execute in order every tick (some only on certain days).
The registration order is:

1. **EconomySystem** — daily — production, facility processing, consumption, satisfaction
2. **FiscalSystem** — daily — taxation, minting, trade, granary, relief
3. **InterRealmTradeSystem** — daily — deficit scanning, price discovery
4. **PopulationSystem** — every 30 days — births, deaths, migration
5. **SpoilageSystem** — every 30 days — monthly decay of perishable stockpiles

## Tick Loop

Each Unity frame:

    If simulation is paused or time scale is zero, do nothing
    Add frame's deltaTime to an accumulator
    While the accumulator holds at least one day's worth of time (1/timeScale seconds):
        Subtract one day from the accumulator
        Advance the day counter
        For each registered system, in order:
            If today's day number is divisible by that system's tick interval:
                Run the system

A frame budget (default 4 ticks) caps how many days are processed per frame to
prevent stalls when running at high speed.

## Goods

There are 35 goods, each belonging to a **need category**:

- **Staple** (wheat, sausage, cheese, salted fish, stockfish, ale) — pooled
  food budget, starvation if unmet
- **Basic** (salt, barley) — individually consumed, contributes to basic
  satisfaction
- **Comfort** (bread, wine, mead, bacon, honey, butter, pottery, furniture,
  tools, clothes, gold jewelry, silver jewelry) — grouped into 8 substitute
  categories; drives migration pull
- **None** (timber, iron ore, gold ore, silver ore, stone, clay, wool, pork,
  milk, fish, gold, silver, iron, charcoal, grapes) — intermediate or facility
  inputs, no direct population demand

Each good has a **base price** in Crowns per unit. For bulk goods the unit is
1 kg; for durables the unit is one item (a pot, a chair, a tool set, an
outfit). The **unit weight** field records kg per unit (1.0 for bulk, higher
for durables). Market prices float based on supply and demand (see Price
Discovery below), bounded by a floor (5% of base) and a ceiling (20x base).
Durables and durable-input goods use fixed base pricing instead.

| Durable        | Unit Weight | Base Price | Target Stock |
| -------------- | ----------- | ---------- | ------------ |
| Pottery        | 1.0 kg      | 0.15 Cr    | 3.0 /person  |
| Furniture      | 10.0 kg     | 1.50 Cr    | 0.5 /person  |
| Tools          | 3.0 kg      | 3.00 Cr    | 1.0 /person  |
| Clothes        | 2.0 kg      | 2.50 Cr    | 2.0 /person  |
| Gold Jewelry   | 0.05 kg     | 15.00 Cr   | 0.2 /person  |
| Silver Jewelry | 0.10 kg     | 8.00 Cr    | 0.3 /person  |

### Comfort Categories

Comfort goods are grouped into **substitute categories**. Within each category,
goods are fungible — their stock or consumption is summed against a single
category-level target. This means a county producing only ale can fully satisfy
the Alcohol category, while a county producing both ale and wine reaches the
target faster.

| Category      | Goods                   | Target/person | Measurement |
| ------------- | ----------------------- | ------------- | ----------- |
| Alcohol       | Wine, Mead              | 0.05 kg/day   | consumption |
| Prepared Food | Bread, Bacon            | 0.10 kg/day   | consumption |
| Pantry        | Honey, Butter           | 0.02 kg/day   | consumption |
| Pottery       | Pottery                 | 3.0 units     | stock       |
| Furniture     | Furniture               | 0.5 units     | stock       |
| Tools         | Tools                   | 1.0 units     | stock       |
| Clothing      | Clothes                 | 2.0 units     | stock       |
| Jewelry       | Gold Jewelry, Silver J. | 0.2 units     | stock       |

Category fulfillment = min(1, sum of member goods / (population × target)).
Overall comfort = average across all 8 categories.

### Staple Pool

All staple goods contribute to a shared daily food budget of **1.0 kg per
person**. Each staple has a nominal consumption rate (e.g. wheat 0.50, sausage
0.21, cheese 0.07, ale 0.05). These rates determine each staple's **ideal
share** of the pool, normalized so the shares sum to 1.0 kg. People eat and
drink from whatever staples are available, proportional to stock.

### Durable Goods

Durables (pottery, furniture, tools, clothes) are tracked in abstract units
(pots, chairs, tool sets, outfits), not kilograms. Each person needs a
**target stock level** in units (e.g. 1.0 tool set per person). All internal
accounting — stock, production, consumption, unmet need — uses units. Weight
only matters for transport cost calculation (see Transport Costs). Stock
degrades at a daily spoilage rate (wear). Production aims to maintain stock at
the target, with a catch-up rate tied to durability: lower spoilage means
slower catch-up, preventing overproduction of very durable items.

### Seasonal Extraction Modifier

Raw good extraction varies by season and long-term climate cycles. The combined
modifier has two components: a fast annual wave and a slow multi-decade wave.

    seasonal_wave = cos(2π × (day_of_year - 170) / 360)
        (170 = northern summer solstice; wave = +1 in midsummer, -1 in midwinter)

    climate_wave = cos(2π × day / (period_years × 360))
        (default period = 20 years; wave = +1 at warm peak, -1 at cold peak)

    For southern-hemisphere counties, seasonal_wave is negated

    amplitude = |latitude| / 90
        (0 at equator, ~0.56 at 50°N, 1.0 at poles)

    modifier = 1 + sensitivity × amplitude × (severity × seasonal_wave × 0.5 + climate_amplitude × climate_wave)

- **severity** is a global constant (default 1.0; 0 = no seasons)
- **climate_amplitude** controls long-term swing (default 0.1)
- **sensitivity** is per-good (wheat 0.9, barley 0.9, fish 0.3, iron ore 0.05, etc.)
- At the equator (amplitude = 0), modifier is always 1.0
- The seasonal wave averages to 1.0 annually; the climate wave shifts the
  annual average above or below 1.0 over decades

The modifier applies to both extraction and production capacity calculations.
Facility processing is not directly seasonal — it is indirectly affected through
raw material availability.

| Good      | Sensitivity | 50°N Summer | 50°N Winter | Warm Epoch Avg | Cold Epoch Avg |
| --------- | ----------- | ----------- | ----------- | -------------- | -------------- |
| Grapes    | 0.95        | 1.27×       | 0.73×       | 1.05×          | 0.95×          |
| Wheat     | 0.9         | 1.25×       | 0.75×       | 1.05×          | 0.95×          |
| Barley    | 0.9         | 1.25×       | 0.75×       | 1.05×          | 0.95×          |
| Honey     | 0.6         | 1.17×       | 0.83×       | 1.03×          | 0.97×          |
| Milk      | 0.5         | 1.14×       | 0.86×       | 1.03×          | 0.97×          |
| Pork      | 0.4         | 1.11×       | 0.89×       | 1.02×          | 0.98×          |
| Fish      | 0.3         | 1.08×       | 0.92×       | 1.02×          | 0.98×          |
| Wool      | 0.4         | 1.11×       | 0.89×       | 1.02×          | 0.98×          |
| Salt      | 0.2         | 1.06×       | 0.94×       | 1.01×          | 0.99×          |
| Timber    | 0.1         | 1.03×       | 0.97×       | 1.01×          | 0.99×          |
| Iron Ore  | 0.05        | 1.01×       | 0.99×       | 1.00×          | 1.00×          |

### Temperature Gating

Some goods require minimum or maximum cell temperatures for extraction. During
productivity initialization, if a cell's temperature falls outside a good's
range, that cell contributes zero yield for that good.

| Good      | Min Temp | Max Temp |
| --------- | -------- | -------- |
| Grapes    | 12°C     | —        |
| Honey     | 8°C      | —        |
| Wheat     | 5°C      | 35°C     |
| Barley    | 3°C      | 30°C     |
| Wool      | -10°C    | 30°C     |
| Pork      | -5°C     | 35°C     |
| Milk      | -5°C     | 35°C     |

This creates geographic specialization: grapes only grow in warm climates,
wheat fails in extreme cold or heat, and livestock tolerates a wider range.
Counties with cells outside the temperature range have zero productivity for
that good, forcing trade dependence.

## Initialization (EconomySystem)

When the simulation starts, EconomySystem sets up all economic state:

    For each county on the map:
        Set initial population from map generation data
        Compute per-good productivity:
            For each land cell in the county:
                Look up biome yields for every good (kg/person/day from biome tables)
                For coastal cells (distance 0/1/2), add a fishing bonus (0.363/0.182/0.061)
            Average across all land cells in the county
        Seed the county treasury at 1.0 Crowns per person

    Compute the global median productivity per good:
        For each good, collect all counties' productivities, sort, take the median

    Build a county adjacency graph:
        For each cell, check its neighbors
        If a neighbor belongs to a different county, those two counties are adjacent

    Place one of every facility type in every county:
        (Every county gets a kiln, carpenter, smelter, smithy, charcoal burner,
         weaver, butcher, smokehouse, cheesemaker, salter, drying rack, bakery,
         brewery, gold jeweler, silver jeweler, winery, meadery, and churn —
         whether they can actually operate depends on input availability)

    Initialize province and realm economies (empty treasuries and granaries)

    Derive per-county sabbath day from religion:
        For each county:
            Look up the seat cell's religion
            Store that religion's sabbath day (0=Monday .. 6=Sunday)
            (Default to Sunday if religion not found)
        Each religion's sabbath day is assigned randomly during world generation,
        so neighboring counties of different religions may rest on different days

    Seed market prices to base prices (so fiscal calculations work on day 1)

    Initialize markets (one per realm):
        For each realm:
            Create a MarketInfo with hub at the realm capital burg's county
            Assign all counties in that realm to this market (CountyToMarket[])
            Seed per-market prices (PerMarketPrices[marketId][]) to base prices
        Compute hub-to-hub transport cost matrix:
            For each pair of markets, run Dijkstra between hub cells
            Store as HubToHubCost[m1][m2]
        Compute average cross-market transport premium:
            Population-weighted average of hub-to-hub costs × normalization factor
            (Calibrated so the effective cross-market rate ≈ 0.02 Cr/kg)

## Static Transport Backbone

Before the first tick, a road network is generated:

    Select "major" counties:
        Sort all counties by population (descending)
        Take the top 25% (min 20, max 160)
        Always include counties containing capital burgs

    Build route pairs:
        For each major county, find its 3 nearest major-county neighbors (by centroid distance)
        Collect all unique pairs

    Trace shortest paths:
        For each pair, run Dijkstra pathfinding on the cell graph
            (terrain costs vary by biome; water cells are much more expensive)
        Weight each route by the geometric mean of the two counties' populations

    Assign road tiers:
        For every land-only edge traversed by any route, accumulate weighted usage
        All traversed edges become at least "path" tier
        The top 20% (by usage weight) become "road" tier
        Roads reduce travel cost for future pathfinding

## EconomySystem (Daily)

This system handles production, facility processing, and consumption for every
county.

### Sabbath (Rest Day)

Each county observes a weekly rest day determined by its religion's sabbath
(0=Monday .. 6=Sunday). On a county's rest day, production and facility
processing are skipped — workers rest. Consumption still runs every day.

Because religions have different sabbath days, not all counties rest on the same
day. This creates emergent economic variation: a county resting on Friday still
trades with neighbors who are working that day, and vice versa.

    Compute today's day of week: (day - 1) % 7

### Compute Global Production Capacity

Production capacity is always computed (even on rest days) because it represents
structural capacity used for price discovery, not actual daily output.

    For each county:
        For each good:
            capacity = population × productivity × seasonal_modifier
            Add capacity to total extraction capacity
    For each county's facilities:
        For each facility that has labor and output:
            Add its maximum labor-constrained output to total capacity for that output good

These totals feed into price discovery (in InterRealmTradeSystem).

### Per-County Production and Consumption

    For each county:
        If today is this county's sabbath day:
            Zero out all production, skip extraction and facility processing
            (Fall through to consumption below)

        Compute the workforce fraction available for extraction:
            workforce_fraction = (population - facility_workers) / population
            (facility_workers is from the previous tick; on tick 1 it is zero)

#### Compute Facility Input Demand (two-pass)

This determines how much raw material each facility needs, which in turn
governs demand-driven extraction and trade retain calculations.

        Pass 1 — Durable outputs (pottery, furniture, tools, clothes):
            For each facility that produces a durable good:
                Compute labor-constrained max output
                Compute target stock (population × target_stock_per_pop)
                Compute maintenance need (current_stock × spoilage_rate)
                Compute stock gap (target - current, floored at 0)
                Daily need = maintenance + gap × catch_up_rate
                Throughput = min(max_by_labor, daily_need × 3.0)
                For each input good of this facility:
                    Accumulate input demand proportional to throughput

        Pass 2 — Non-durable outputs (iron, charcoal, sausage, bread, etc.):
            Processed in facility enum order (smelter before charcoal burner, so
            charcoal demand already includes smelter's needs when charcoal burner runs)

            For each facility that produces a non-durable good:
                Compute labor-constrained max output
                If this good is a durable-chain input (iron, charcoal, etc.):
                    Throughput is capped by downstream demand:
                        target_stock = downstream_facility_demand × 7 days buffer
                        throughput = min(max_by_labor, max(0, target_stock - current_stock))
                Else (commodity — sausage, bread, ale, etc.):
                    Throughput = max labor capacity (no stock cap)
                For each input good of this facility:
                    Accumulate input demand proportional to throughput

#### Extraction (Raw Good Production)

        For each good:
            produced = population × productivity × workforce_fraction × seasonal_modifier

            If this good is a durable-chain raw input (iron ore, timber for charcoal, etc.):
                Cap extraction to local facility demand:
                    target_stock = local_facility_demand × 14 days buffer
                    produced = min(produced, max(0, target_stock - current_stock))

            Else if this good has no direct population demand and has a base price:
                Price-throttle extraction (uses local market price):
                    price_ratio = local_market_price / base_price
                    If ratio < 1: produced *= ratio
                    (When prices are depressed, workers produce less;
                     local_market_price = PerMarketPrices[county's market][good])

            Add produced to county stock

#### Facility Processing

        For each facility in the county:
            Compute material constraint:
                For each input good, how many output units could be made from available stock?
                Take the minimum across all inputs

            Compute labor constraint:
                max_by_labor = population × max_labor_fraction / labor_per_unit × output_amount

            throughput = min(material_constraint, labor_constraint), floored at 0

            If output is a durable:
                Cap throughput by stock-gap need (same formula as Pass 1)

            If output is a durable-chain intermediate:
                Cap throughput by downstream demand (same formula as Pass 2)

            If output is a normal commodity (not durable, not durable-input):
                Price-throttle: if local_market_price / base_price < 1, scale down throughput

            Consume input goods proportionally to throughput
            Add output to county stock
            Record workers assigned to this facility
        Sum total facility workers across all facilities

#### Consumption

**Non-staple goods:**

        For each good that is not a Staple:
            If it is a durable (has target stock per pop):
                Wear: replacement = current_stock × spoilage_rate
                Remove min(stock, replacement) from stock
                Unmet need = max(0, target_stock - post_wear_stock) × catch_up_rate
            Else (consumable — salt, barley, bread, ale, bacon):
                needed = population × consumption_per_pop
                consumed = min(stock, needed)
                Remove consumed from stock
                Unmet need = needed - consumed

**Staple pool (pooled food consumption):**

        staple_budget = population × 1.0 kg/day
        total_staple_available = sum of stock across all staple goods

        If enough food (total_available >= budget):
            Consume from each staple proportional to its stock share:
                consumed_wheat = (wheat_stock / total_available) × budget
                consumed_sausage = (sausage_stock / total_available) × budget
                ... etc.
        Else (starvation):
            Eat everything — every staple stock goes to zero

        For each staple, compute unmet need as:
            max(0, ideal_share - actually_consumed)
            (e.g. wheat's ideal = 0.50 of the 1.0 budget)

#### Satisfaction

Two satisfaction metrics are computed as 30-day exponential moving averages
(EMA with alpha ≈ 0.065):

**BasicSatisfaction** (drives births and deaths):

        Compute staple fulfillment:
            staple_fulfillment = min(1, total_staple_consumed / staple_budget)

        Compute weighted needs score:
            score = staple_weight × staple_fulfillment
            For each individual basic good (salt, barley):
                ratio = min(1, consumed / needed)
                score += basic_weight × ratio
            (weights are normalized so staple_weight + sum(basic_weights) = 1)

        BasicSatisfaction EMA update:
            BasicSatisfaction += 0.065 × (score - BasicSatisfaction)

**Satisfaction** (drives migration):

        Compute comfort fulfillment:
            For each comfort category (8 categories):
                Sum actual values across all member goods:
                    If durable category: actual = sum of stock across member goods
                    Else: actual = sum of consumption across member goods
                category_ratio = min(1, actual / (population × category_target))
            comfort_average = mean of all 8 category ratios

        Blended satisfaction = 0.70 × needs_score + 0.30 × comfort_average
        Satisfaction += 0.065 × (blended - Satisfaction)

## FiscalSystem (Daily)

Runs after EconomySystem. Handles taxation, minting, trade, granaries, and
emergency relief across the feudal hierarchy (county → province → realm).

### Phase 1: County Admin Consumption

    For each good that has a county admin rate:
        For each county:
            need = population × county_admin_per_pop
            consumed = min(stock, need)
            Remove consumed from stock
            If consumed < need:
                Record the shortfall as realm-level deficit

### Phase 2: Precious Metal Confiscation

    For each realm:
        For each county in the realm:
            Confiscate 100% of gold ore and silver ore stock
            Transfer to realm stockpile
    (Gold and silver are crown property — the regal right of coinage)

### Phase 3: Monetary Taxation

**County → Province (surplus tithe):**

    For each province:
        For each county in the province:
            Compute surplus value:
                For each good where production > consumption:
                    surplus_value += (production[g] - consumption[g]) × market_price[g]
            Tax = min(surplus_value × 1.3%, county_treasury)
            Transfer tax from county treasury to province treasury

**Province → Realm (revenue share):**

    For each province:
        Tax = min(province_tax_collected × 40%, province_treasury)
        Transfer tax from province treasury to realm treasury

### Phase 4: Admin Wages

Admin costs are wages, not destroyed value — money flows from higher-tier
treasuries back down to county populations.

    For each province:
        cost = min(province_pop × province_admin_cost_per_pop, province_treasury)
        Deduct cost from province treasury
        Distribute to counties proportional to population:
            each county receives cost × (county_pop / province_pop)

    For each realm:
        cost = min(realm_pop × realm_admin_cost_per_pop, realm_treasury)
        Deduct cost from realm treasury
        Distribute to all counties in the realm proportional to population

### Phase 5: Minting

    For each realm:
        Take all gold ore and silver ore from realm stockpile
        Convert to Crowns:
            crowns = gold × 1% smelting yield × 1000 Cr/kg
                   + silver × 5% smelting yield × 100 Cr/kg
        Add crowns to realm treasury

### Phase 6: Trade (Three Cascading Passes)

Trade runs in three passes of increasing scope. Each pass clears local surplus
before the wider pass runs, so local needs are met first.

**Pass 1 — Intra-province trade:**
Counties within the same province trade with each other.

**Pass 2 — Cross-province trade:**
All counties within the same realm re-enter a single pool (including those that
already traded locally). After pass 1 consumed some surplus, remaining stock is
re-evaluated. Buyers pay a 5% toll (to their own province).

**Pass 3 — Cross-market trade:**
All counties globally re-enter a single pool. Since each realm has its own
market, this pass handles trade across market zones. Buyers pay a 5% toll (to
province) and a 10% tariff (to realm). Transport cost includes a hub-to-hub
distance premium based on the precomputed average cross-market transport rate.

All three passes use the same matching algorithm, iterated in **buy priority
order** (wheat first, then bread, barley, ale, sausage, salted fish, ... down
to charcoal):

    For the current good at the current scope:
        For each county in the scope:
            surplus = stock - (population × retain_per_pop) - facility_input_need
            (retain_per_pop is the ideal daily consumption for consumables,
             or the durable retain rate for durables)

            If surplus > 0: this county is a seller
            If surplus < 0: this county is a buyer
                raw_demand = |surplus|
                affordable = county_treasury / cost_per_unit
                    where cost_per_unit = price × (1 + toll_rate + tariff_rate + market_fee_rate) + transport_cost_per_kg × unit_weight
                    market_fee_rate = 2% for cross-province and cross-realm, 0% for intra-province
                demand = min(raw_demand, affordable)

        If no supply or no demand, skip this good

        fill_ratio = min(1, total_supply / total_demand)
        sell_ratio = min(1, total_demand / total_supply)

        For each seller:
            sold = surplus × sell_ratio
            Remove sold from stock
            Earn sold × market_price into treasury

        For each buyer:
            bought = demand × fill_ratio
            Add bought to stock
            Pay goods_cost + toll + tariff + market_fee + transport_cost from treasury
            Toll → buyer's province treasury
            Tariff → buyer's realm treasury
            Market fee → buyer's market hub county treasury (cross-province and cross-market only)
            Transport cost → destroyed (represents consumed fodder, labor, cart wear)
                transport_cost = bought × transport_rate_per_kg × unit_weight

#### Transport Costs

Physical transport costs apply per-kg based on trade scope:

- **Intra-province**: free (0 Cr/kg)
- **Cross-province**: 0.007 Cr/kg
- **Cross-market**: 0.007 + avg_cross_market_premium Cr/kg (typically ~0.02 total)

The cross-market premium is derived from the hub-to-hub Dijkstra cost matrix at
initialization: a population-weighted average of all market-pair distances,
scaled by a normalization factor. This means maps with more spread-out realms
have higher cross-market transport costs than compact maps.

The per-kg rate is multiplied by the good's **unit weight**, so transport cost
reflects actual physical weight. For bulk goods (unit weight 1.0) this changes
nothing. For durables, one unit of furniture (10 kg) costs 10× as much to
transport as one kg of wheat. The cost is additive (not multiplicative on
price), so it hits cheap bulk goods (wheat at 0.03 Cr/kg → +23%
cross-province) much harder than expensive goods. This naturally localizes bulk
commodity trade while allowing high-value goods to travel freely.

Transport costs are **destroyed** — they represent real resource consumption (fodder,
labor, cart wear) and act as a money sink alongside spoilage.

### Phase 7: Ducal Granary Requisition

The duke (province) maintains an emergency food reserve by buying grain from
surplus counties. Only shelf-stable grain (wheat) is eligible — perishable
staples like ale, sausage, and cheese are not stored.

    For each province:
        For each granary-eligible good (wheat):
            target = 7 days × province_pop × ideal_per_pop
            gap = target - current_granary_stock
            If no gap, skip

            fill_amount = gap × 5% (gradual daily fill)
            Limit by what the province treasury can afford at 60% of market price

            Find surplus counties (stock > population × ideal_per_pop)
            Collect proportionally from surplus counties:
                take = county_surplus × collect_ratio
                Pay county at 60% of market price (discount)
            Add collected goods to provincial granary stockpile

### Phase 8: Emergency Relief

After trade and granary filling, distribute grain to distressed counties.

    For each granary-eligible good (wheat):
        Identify distressed counties (BasicSatisfaction < 0.70):
            deficit = (population × ideal_per_pop) - stock, floored at 0

        For each province:
            If the granary has stock and there are distressed counties:
                Distribute granary stock proportional to each county's deficit share

## InterRealmTradeSystem (Daily)

Runs after FiscalSystem. Two jobs: deficit scanning and per-market price
discovery.

### Deficit Scan

    For each good:
        For each county:
            If durable:
                shortfall = (stock × spoilage_rate) + max(0, target_stock - stock) × catch_up_rate
            Else (consumable):
                shortfall = (population × consumption_per_pop) - stock
            If shortfall > 0:
                Add to that county's realm's deficit tally

### Per-Market Price Discovery

Prices float daily for all non-durable, non-durable-input goods. Durables and
their exclusive inputs use fixed base prices. Prices are computed independently
per market zone, creating geographic price variation.

    For each market zone:
        Compute per-market production capacity:
            For each county in this market:
                Add extraction capacity and facility output capacity

        For each eligible good:
            Compute demand (from counties in this market only):
                For each county in the market:
                    demand += facility_input_need
                    demand += population × (consumption_per_pop + county_admin_per_pop)
                    demand += population × target_stock_per_pop × spoilage_rate

            Compute supply (from counties in this market only):
                supply = market_production_capacity + market_total_stock / 7 days

            Per-market price = base_price × (demand / supply)
            Clamp to [min_price, max_price]
            Write to PerMarketPrices[marketId][good]

    Compute global average prices (population-weighted across markets):
        For each eligible good:
            MarketPrices[good] = Σ(market_pop × market_price) / Σ(market_pop)

    EconomySystem reads PerMarketPrices for local production throttle decisions
    (extraction and facility throughput respond to the county's own market price,
    not the global average). FiscalSystem's cross-market trade pass uses the
    global average price for pool fill/sell math.

## PopulationSystem (Monthly, every 30 days)

### Phase 1: Birth and Death

    For each county:
        birth_multiplier = 0.5 + BasicSatisfaction  (ranges 0.5 to 1.5)
        births = population × 0.3% × birth_multiplier

        starvation = max(0, 1 - BasicSatisfaction)
        death_multiplier = 1 + 9 × starvation²  (ranges 1 to 10)
        deaths = population × 0.25% × death_multiplier

        population = population + births - deaths
        (Floored at 10 people — counties never fully die)

### Phase 2: Migration (buffered, atomic)

Migration flows from low-satisfaction to high-satisfaction counties within
the same realm. Uses the blended Satisfaction (needs + comfort), not
BasicSatisfaction.

    For each county (source):
        If population ≤ 15 (floor × 1.5), skip — too small to lose people

        Among adjacent same-realm counties, find the one with the highest
        satisfaction gap (neighbor_satisfaction - source_satisfaction)

        If the best gap ≤ 0.15 (threshold), skip — not enough pull

        emigration_rate = min(2%, gap × 2% / 0.5)
            (Scales linearly with gap; maxes out at 2% per month)
        migrants = population × emigration_rate
        (Don't push source below floor of 10)

        Buffer the outflow and inflow (don't move people during the scan)

    Apply all migration atomically:
        For each county, population += inflow - outflow

## SpoilageSystem (Monthly, every 30 days)

Applies monthly decay to perishable stockpiles. "Perishable" means any good
with a spoilage rate and no target stock (durables handle their own wear in
EconomySystem). The monthly retention factor is (1 - daily_spoilage)^30.

    For each perishable good (wheat, barley, timber, wool, pork, milk, fish,
                              sausage, bacon, cheese, salted fish, stockfish,
                              bread, ale, grapes, wine, honey, mead, butter):
        For each county:  stock *= monthly_retention
        For each province: granary_stock *= monthly_retention
        For each realm:    stockpile *= monthly_retention

## Facilities (Production Chains)

Every county contains one of each facility type. Facilities process raw goods
into refined or finished goods. Each has a recipe (inputs → output), a labor
requirement, and a max labor fraction (cap on what share of the county
population can work there).

| Facility        | Inputs                      | Output        | Labor | Max Pop % |
| --------------- | --------------------------- | ------------- | ----- | --------- |
| Kiln            | 2.0 clay                    | 1 pottery     | 3     | 5%        |
| Carpenter       | 15.0 timber                 | 1 furniture   | 1     | 10%       |
| Charcoal Burner | 5.0 timber                  | 2.0 charcoal  | 1     | 10%       |
| Smelter         | 3.0 iron ore + 0.4 charcoal | 2.0 iron      | 1     | 5%        |
| Smithy          | 5.0 iron + 0.5 charcoal     | 1 tool set    | 1     | 5%        |
| Weaver          | 4.0 wool                    | 1 outfit      | 2     | 10%       |
| Butcher         | 1.0 pork + 0.2 salt         | 3.0 sausage   | 1     | 15%       |
| Smokehouse      | 2.0 pork                    | 3.0 bacon     | 1     | 15%       |
| Cheesemaker     | 3.0 milk + 0.3 salt         | 1.5 cheese    | 1     | 15%       |
| Salter          | 1.0 fish + 0.5 salt         | 3.0 s.fish    | 1     | 15%       |
| Drying Rack     | 2.0 fish                    | 1.5 stkfish   | 1     | 10%       |
| Bakery          | 2.0 wheat + 0.03 salt       | 2.8 bread     | 1     | 15%       |
| Brewery         | 2.0 barley                  | 4.0 ale       | 1     | 15%       |
| Gold Jeweler    | 0.01 gold                   | 1 g.jewelry   | 1     | 2%        |
| Silver Jeweler  | 0.05 silver                 | 1 s.jewelry   | 1     | 2%        |
| Winery          | 2.0 grapes                  | 1.5 wine      | 1     | 10%       |
| Meadery         | 2.0 honey                   | 2.0 mead      | 1     | 10%       |
| Churn           | 3.0 milk                    | 1.0 butter    | 1     | 10%       |

Durable outputs (pottery, furniture, tools, clothes, gold jewelry, silver
jewelry) are in units; all other outputs are in kg.

Throughput is constrained by the minimum of:

- **Material**: available stock of each input, scaled proportionally
- **Labor**: population × max_labor_fraction / labor_per_unit × output_amount
- **Demand** (durables only): stock-gap cap prevents overproduction
- **Price** (commodities only): price-throttle reduces output when prices are low

## Money Supply

Crowns enter the economy through **minting**: realms confiscate all gold and
silver ore produced by counties, smelt it (1% yield for gold, 5% for silver),
and mint coins (1000 Cr/kg gold, 100 Cr/kg silver). The refined gold and silver
(post-smelting) are also used by jewelers to produce gold and silver jewelry
(comfort durables), creating a tension between minting and luxury production.

Money circulates through:

- Surplus taxes (county → province, 1.3% of surplus value above local consumption)
- Revenue sharing (province → realm, 40% of tax collected)
- Admin wages (province/realm → counties, proportional to population)
- Trade payments (buyer treasury → seller treasury, with fees/tolls/tariffs
  flowing to provinces and realms)
- Granary requisition payments (province → surplus counties, at 60% of price)
- Market fees (2% of cross-province and cross-market trade volume → buyer's
  market hub county; each realm's market hub collects fees from its own zone)

Money is destroyed through **transport costs**: buyers pay per-kg transport fees
on cross-province and cross-realm trade. These costs represent consumed fodder,
labor, and cart wear — they leave the economy entirely. This creates a natural
money sink that scales with trade volume, counterbalancing minting.
