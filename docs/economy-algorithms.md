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

There are 26 goods, each belonging to a **need category**:

- **Staple** (wheat, sausage, cheese, salted fish, stockfish) — pooled food
  budget, starvation if unmet
- **Basic** (salt, barley) — individually consumed, contributes to basic
  satisfaction
- **Comfort** (bread, ale, bacon, pottery, furniture, tools, clothes) — drives
  migration pull; durables measured by stock level, consumables by flow
- **None** (timber, iron ore, gold, silver, stone, clay, wool, pork, milk,
  fish, iron, charcoal) — intermediate or facility inputs, no direct population
  demand

Each good has a **base price** in Crowns/kg. Market prices float based on
supply and demand (see Price Discovery below), bounded by a floor (5% of base)
and a ceiling (20x base). Durables and durable-input goods use fixed base
pricing instead.

### Staple Pool

All staple goods contribute to a shared daily food budget of **1.0 kg per
person**. Each staple has a nominal consumption rate (e.g. wheat 0.50, sausage
0.21, cheese 0.07). These rates determine each staple's **ideal share** of the
pool, normalized so the shares sum to 1.0 kg. People eat from whatever staples
are available, proportional to stock.

### Durable Goods

Durables (pottery, furniture, tools, clothes) are not consumed daily. Instead
each person needs a **target stock level** (e.g. 2.0 kg/person for tools).
Stock degrades at a daily spoilage rate (wear). Production aims to maintain
stock at the target, with a catch-up rate tied to durability: lower spoilage
means slower catch-up, preventing overproduction of very durable items.

## Initialization (EconomySystem)

When the simulation starts, EconomySystem sets up all economic state:

    For each county on the map:
        Set initial population from map generation data
        Compute per-good productivity:
            For each land cell in the county:
                Look up biome yields for every good (kg/person/day from biome tables)
                For coastal cells (distance 0/1/2), add a fishing bonus (0.30/0.15/0.05)
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
         and brewery — whether they can actually operate depends on input availability)

    Initialize province and realm economies (empty treasuries and granaries)

    Seed market prices to base prices (so fiscal calculations work on day 1)

    Resolve the market county:
        Try the first realm's capital burg's cell's county
        Fallback: the county with the highest population

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

### Compute Global Production Capacity

    For each county:
        For each good:
            Add (population × productivity) to total extraction capacity
    For each county's facilities:
        For each facility that has labor and output:
            Add its maximum labor-constrained output to total capacity for that output good

These totals feed into price discovery (in InterRealmTradeSystem).

### Per-County Production and Consumption

    For each county:
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
            produced = population × productivity × workforce_fraction

            If this good is a durable-chain raw input (iron ore, timber for charcoal, etc.):
                Cap extraction to local facility demand:
                    target_stock = local_facility_demand × 14 days buffer
                    produced = min(produced, max(0, target_stock - current_stock))

            Else if this good has no direct population demand and has a base price:
                Price-throttle extraction:
                    price_ratio = market_price / base_price
                    If ratio < 1: produced *= ratio
                    (When prices are depressed, workers produce less)

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
                Price-throttle: if market_price / base_price < 1, scale down throughput

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
            For each comfort good:
                If durable: ratio = min(1, stock / target_stock)
                Else: ratio = min(1, consumed / needed)
            comfort_average = mean of all comfort ratios

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

**County → Province (production tithe):**

    For each province:
        For each county in the province:
            Compute production value = sum(production[g] × market_price[g]) across all goods
            Tax = min(production_value × 1.3%, county_treasury)
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

**Pass 3 — Cross-realm trade:**
All counties globally re-enter a single pool. Buyers pay a 5% toll (to
province) and a 10% tariff (to realm).

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
                    where cost_per_unit = price × (1 + toll_rate + tariff_rate + 2% market_fee)
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
            Pay goods_cost + toll + tariff + market_fee from treasury
            Toll → buyer's province treasury
            Tariff → buyer's realm treasury
            Market fee → the designated market county's treasury

### Phase 7: Ducal Granary Requisition

The duke (province) maintains an emergency food reserve by buying staples from
surplus counties.

    For each province:
        For each staple good:
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

After trade and granary filling, distribute food to distressed counties.

    For each staple good:
        Identify distressed counties (BasicSatisfaction < 0.70):
            deficit = (population × ideal_per_pop) - stock, floored at 0

        For each province:
            If the granary has stock and there are distressed counties:
                Distribute granary stock proportional to each county's deficit share

## InterRealmTradeSystem (Daily)

Runs after FiscalSystem. Two jobs: deficit scanning and price discovery.

### Deficit Scan

    For each good:
        For each county:
            If durable:
                shortfall = (stock × spoilage_rate) + max(0, target_stock - stock) × catch_up_rate
            Else (consumable):
                shortfall = (population × consumption_per_pop) - stock
            If shortfall > 0:
                Add to that county's realm's deficit tally

### Price Discovery

Prices float daily for all non-durable, non-durable-input goods. Durables and
their exclusive inputs use fixed base prices.

    For each eligible good:
        Compute total demand:
            For each county:
                demand += facility_input_need
                demand += population × (consumption_per_pop + county_admin_per_pop)
                demand += population × target_stock_per_pop × spoilage_rate  (replacement)

        Compute supply:
            supply = production_capacity + total_stock / 7 days

        Raw price = base_price × (demand / supply)
        Clamp to [min_price, max_price]

    Publish prices to shared MarketPrices array

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
                              bread, ale):
        For each county:  stock *= monthly_retention
        For each province: granary_stock *= monthly_retention
        For each realm:    stockpile *= monthly_retention

## Facilities (Production Chains)

Every county contains one of each facility type. Facilities process raw goods
into refined or finished goods. Each has a recipe (inputs → output), a labor
requirement, and a max labor fraction (cap on what share of the county
population can work there).

| Facility        | Inputs                      | Output       | Labor | Max Pop % |
| --------------- | --------------------------- | ------------ | ----- | --------- |
| Kiln            | 2.0 clay                    | 1.0 pottery  | 3     | 5%        |
| Carpenter       | 3.0 timber                  | 2.0 furn.    | 1     | 10%       |
| Charcoal Burner | 5.0 timber                  | 2.0 charcoal | 1     | 10%       |
| Smelter         | 3.0 iron ore + 0.4 charcoal | 2.0 iron     | 1     | 5%        |
| Smithy          | 2.0 iron + 0.2 charcoal     | 4.0 tools    | 1     | 5%        |
| Weaver          | 3.0 wool                    | 3.0 clothes  | 2     | 10%       |
| Butcher         | 1.0 pork + 0.2 salt         | 3.0 sausage  | 2     | 10%       |
| Smokehouse      | 2.0 pork                    | 2.0 bacon    | 1     | 10%       |
| Cheesemaker     | 3.0 milk + 0.3 salt         | 1.0 cheese   | 1     | 10%       |
| Salter          | 1.0 fish + 0.5 salt         | 2.0 s.fish   | 1     | 10%       |
| Drying Rack     | 2.0 fish                    | 1.5 stkfish  | 1     | 10%       |
| Bakery          | 2.0 wheat + 0.03 salt       | 2.8 bread    | 1     | 15%       |
| Brewery         | 2.0 barley                  | 4.0 ale      | 1     | 15%       |

Throughput is constrained by the minimum of:

- **Material**: available stock of each input, scaled proportionally
- **Labor**: population × max_labor_fraction / labor_per_unit × output_amount
- **Demand** (durables only): stock-gap cap prevents overproduction
- **Price** (commodities only): price-throttle reduces output when prices are low

## Money Supply

Crowns enter the economy through **minting**: realms confiscate all gold and
silver ore produced by counties, smelt it (1% yield for gold, 5% for silver),
and mint coins (1000 Cr/kg gold, 100 Cr/kg silver).

Money circulates through:

- Production taxes (county → province, 1.3% of production value)
- Revenue sharing (province → realm, 40% of tax collected)
- Admin wages (province/realm → counties, proportional to population)
- Trade payments (buyer treasury → seller treasury, with fees/tolls/tariffs
  flowing to provinces and realms)
- Granary requisition payments (province → surplus counties, at 60% of price)
- Market fees (2% of all trade volume → market county)

There is no explicit money destruction. Admin wages recirculate rather than
being consumed.
