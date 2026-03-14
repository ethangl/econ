#!/usr/bin/env python3
"""Analyze econ_debug_output.json and print a summary."""

import json
import sys
from pathlib import Path

# Module-level references set by init_goods()
GOODS: list[str] = []
GOOD_IDX: dict[str, int] = {}
VALUES: list[float] = []


def init_goods(data: dict):
    """Initialize goods metadata from dump."""
    global GOODS, GOOD_IDX, VALUES

    # Economy section contains goods metadata
    econ = data.get("economy", {})
    goods_meta = data.get("goods") or econ.get("goods")
    if goods_meta:
        goods_meta = sorted(goods_meta, key=lambda g: g["index"])
        GOODS = [g["name"] for g in goods_meta]
        GOOD_IDX = {g: i for i, g in enumerate(GOODS)}
        VALUES = [g.get("value", 0) for g in goods_meta]


def load(path: str | None = None) -> dict:
    if path is None:
        path = Path(__file__).resolve().parent.parent / "unity" / "econ_debug_output.json"
    with open(path) as f:
        return json.load(f)


def fmt(n: float, decimals: int = 1) -> str:
    if abs(n) >= 1_000_000:
        return f"{n / 1_000_000:,.{decimals}f}M"
    if abs(n) >= 1_000:
        return f"{n / 1_000:,.{decimals}f}k"
    return f"{n:,.{decimals}f}"


def section(title: str):
    print(f"\n{'─' * 60}")
    print(f"  {title}")
    print(f"{'─' * 60}")


def print_header(data: dict):
    day = data.get("day", 0)
    year = data.get("year", 0)
    month = data.get("month", 0)
    dom = data.get("dayOfMonth", 0)
    ticks = data.get("totalTicks", 0)
    ts = data.get("timestamp", "")

    print(f"\n══════════════════════════════════════════════════════════")
    print(f"  ECONOMY DUMP — Day {day} (Y{year} M{month} D{dom})")
    print(f"══════════════════════════════════════════════════════════")
    print(f"  Ticks: {ticks:,}  Timestamp: {ts}")

    s = data.get("summary", {})
    if s:
        print(f"  Population: {s.get('totalPopulation', 0):,.0f}  "
              f"Counties: {s.get('totalCounties', 0)}  "
              f"Provinces: {s.get('totalProvinces', 0)}  "
              f"Realms: {s.get('totalRealms', 0)}  "
              f"Markets: {s.get('marketCount', 0)}")
        paths = s.get("pathSegments", 0)
        roads = s.get("roadSegments", 0)
        if paths or roads:
            print(f"  Roads: {paths} paths, {roads} roads")


def print_performance(data: dict):
    perf = data.get("performance")
    econ = data.get("economy", {})
    pt = econ.get("phaseTiming", {})

    if not perf and not pt:
        return

    section("Performance")

    if perf:
        print(f"  Tick samples: {perf.get('tickSamples', 0)}  "
              f"Avg: {perf.get('avgTickMs', 0):.2f}ms  "
              f"Max: {perf.get('maxTickMs', 0):.2f}ms  "
              f"Last: {perf.get('lastTickMs', 0):.2f}ms")

        systems = perf.get("systems", {})
        if systems:
            print(f"\n  {'System':>16s}  {'Interval':>8s}  {'Invocations':>11s}  "
                  f"{'Avg(ms)':>8s}  {'Max(ms)':>8s}  {'Total(ms)':>10s}")
            for name, s in sorted(systems.items(), key=lambda x: -x[1].get("avgMs", 0)):
                print(f"  {name:>16s}  {s.get('tickInterval', 0):>8d}  "
                      f"{s.get('invocations', 0):>11d}  "
                      f"{s.get('avgMs', 0):>8.2f}  "
                      f"{s.get('maxMs', 0):>8.2f}  "
                      f"{s.get('totalMs', 0):>10.1f}")

    # Economy phase timing (last tick)
    if pt:
        print(f"\n  ── Economy Phase Timing (last tick) ──")
        total = pt.get("total", 0)
        phases = [
            ("GenerateOrders", pt.get("generateOrders", 0)),
            ("ResolveMarkets", pt.get("resolveMarkets", 0)),
            ("UpdateMoney", pt.get("updateMoney", 0)),
            ("UpdateSatisfaction", pt.get("updateSatisfaction", 0)),
            ("UpdatePopulation", pt.get("updatePopulation", 0)),
        ]
        print(f"  {'Phase':>22s}  {'ms':>8s}  {'%':>6s}")
        for name, ms in phases:
            pct = ms / total * 100 if total > 0 else 0
            print(f"  {name:>22s}  {ms:>8.3f}  {pct:>5.1f}%")
        print(f"  {'Total':>22s}  {total:>8.3f}")


def print_roads(data: dict):
    r = data.get("roads")
    if not r:
        return

    section("Roads")
    print(f"  Total segments: {r.get('totalSegments', 0)}  "
          f"Paths: {r.get('paths', 0)}  Roads: {r.get('roads', 0)}")
    print(f"  Total traffic: {r.get('totalTraffic', 0):,.0f}")

    busiest = r.get("busiestSegments", [])
    if busiest:
        print(f"\n  Top {len(busiest)} busiest:")
        print(f"  {'CellA':>8s}  {'CellB':>8s}  {'Tier':>6s}  {'Traffic':>10s}")
        for seg in busiest[:10]:
            print(f"  {seg['cellA']:>8d}  {seg['cellB']:>8d}  "
                  f"{seg.get('tier', '?'):>6s}  {seg.get('traffic', 0):>10,.0f}")


def print_economy(data: dict):
    v4 = data.get("economy")
    if not v4:
        return

    section("Economy")
    print(f"  Counties: {v4.get('countyCount', 0)}  Markets: {v4.get('marketCount', 0)}  "
          f"Goods: {v4.get('goodCount', 0)}  Facilities: {v4.get('facilityCount', 0)}")
    print(f"  Total pop: {v4.get('totalPopulation', 0):,.0f}  "
          f"Money supply: {v4.get('totalMoneySupply', 0):,.2f}  "
          f"Food-deficit counties: {v4.get('foodDeficitCounties', 0)}")

    # Production / consumption / surplus
    prod = v4.get("production", {})
    cons = v4.get("consumption", {})
    surp = v4.get("surplus", {})
    if prod:
        print(f"\n  ── Daily Production / Consumption / Surplus (kg/day) ──")
        all_goods = sorted(set(list(prod.keys()) + list(cons.keys()) + list(surp.keys())))
        print(f"  {'Good':>12s}  {'Production':>12s}  {'Consumption':>12s}  {'Surplus':>12s}  {'Surplus%':>8s}")
        for g in all_goods:
            p = prod.get(g, 0)
            c = cons.get(g, 0)
            s = surp.get(g, 0)
            pct_str = f"{s / p * 100:.0f}%" if p > 0 else "—"
            print(f"  {g:>12s}  {p:>12,.1f}  {c:>12,.1f}  {s:>12,.1f}  {pct_str:>8s}")

    # Coin flows
    cf = v4.get("coinFlows", {})
    if cf:
        print(f"\n  ── Coin Flows (last tick) ──")
        print(f"  Total coin in system:   {cf.get('totalCoinInSystem', 0):>12,.2f}")
        print(f"  Upper noble treasuries: {v4.get('totalUpperNobleTreasury', 0):>12,.2f}")
        print(f"  Lower noble treasuries: {v4.get('totalLowerNobleTreasury', 0):>12,.2f}")
        print(f"  Upper clergy treasuries: {v4.get('totalUpperClergyTreasury', 0):>12,.2f}")
        print(f"  Money supply (M):       {v4.get('totalMoneySupply', 0):>12,.2f}")
        print(f"  Upper noble spend:      {cf.get('totalUpperNobleSpend', 0):>12,.2f}")
        print(f"  Upper noble income:     {cf.get('totalUpperNobleIncome', 0):>12,.2f}")
        print(f"  Lower noble spend:      {cf.get('totalLowerNobleSpend', 0):>12,.2f}")
        print(f"  Serf food provided:     {cf.get('totalSerfFoodProvided', 0):>12,.1f} kg")
        tariff = cf.get("totalTariffRevenue", 0)
        if tariff > 0:
            print(f"  Tariff revenue:         {tariff:>12,.2f}")

    # Upper commoner economy
    uce = v4.get("upperCommonerEconomy", {})
    if uce:
        print(f"\n  ── Upper Commoner Economy ──")
        print(f"  Total coin (M contrib):  {uce.get('totalCoin', 0):>12,.2f}")
        print(f"  Income (facility sales): {uce.get('totalIncome', 0):>12,.2f}")
        print(f"  Spend (goods):           {uce.get('totalSpend', 0):>12,.2f}")
        print(f"  Tax paid:                {uce.get('taxRevenue', 0):>12,.2f}")
        print(f"  Tithe paid:              {uce.get('titheRevenue', 0):>12,.2f}")

    # Clergy economy
    cle = v4.get("clergyEconomy", {})
    if cle:
        print(f"\n  ── Clergy Economy ──")
        print(f"  Upper clergy treasury:   {cle.get('upperClergyTreasury', 0):>12,.2f}")
        print(f"  Upper clergy income:     {cle.get('upperClergyIncome', 0):>12,.2f}")
        print(f"  Upper clergy spend:      {cle.get('upperClergySpend', 0):>12,.2f}")
        print(f"  Lower clergy coin:       {cle.get('lowerClergyCoin', 0):>12,.2f}")
        print(f"  Lower clergy income:     {cle.get('lowerClergyIncome', 0):>12,.2f}")
        print(f"  Lower clergy spend:      {cle.get('lowerClergySpend', 0):>12,.2f}")

    # Population dynamics
    pd = v4.get("populationDynamics", {})
    if pd:
        print(f"\n  ── Population Dynamics ──")
        print(f"  Initial pop: {pd.get('initialTotalPop', 0):>12,.0f}  "
              f"Current pop: {pd.get('currentTotalPop', 0):>12,.0f}  "
              f"Growth: {pd.get('growthPercent', 0):>+.2f}%")
        print(f"  Daily births:    {pd.get('dailyBirths', 0):>10,.1f}  "
              f"Daily deaths:   {pd.get('dailyDeaths', 0):>10,.1f}  "
              f"Net growth: {pd.get('dailyNetGrowth', 0):>+10,.1f}")
        print(f"  Annual growth rate: {pd.get('annualGrowthRatePercent', 0):>+.2f}%")
        print(f"  Migration volume:   {pd.get('dailyMigrationVolume', 0):>10,.1f}/day  "
              f"Counties gaining: {pd.get('countiesGaining', 0)}  "
              f"Losing: {pd.get('countiesLosing', 0)}")

        pop_class = pd.get("popByClass", {})
        if pop_class:
            print(f"\n  ── Population by Class ──")
            print(f"  {'Class':>16s}  {'Population':>12s}  {'Share':>6s}")
            total = pd.get("currentTotalPop", 1)
            for cls, label in [("lowerCommoner", "Lower Commoner"),
                               ("upperCommoner", "Upper Commoner"),
                               ("lowerNobility", "Lower Nobility"),
                               ("upperNobility", "Upper Nobility"),
                               ("lowerClergy", "Lower Clergy"),
                               ("upperClergy", "Upper Clergy")]:
                p = pop_class.get(cls, 0)
                pct_val = p / total * 100 if total > 0 else 0
                print(f"  {label:>16s}  {p:>12,.0f}  {pct_val:>5.1f}%")

    # Facility throughput
    facs = v4.get("facilities_throughput", [])
    if facs:
        print(f"\n  ── Facility Throughput ──")
        print(f"  {'Facility':>12s}  {'Output':>12s}  {'Daily(kg)':>10s}  {'MeanFill':>8s}  {'Active':>6s}")
        for f in facs:
            print(f"  {f['name']:>12s}  {f['output']:>12s}  "
                  f"{f.get('totalDailyOutput', 0):>10,.1f}  "
                  f"{f.get('meanFillRate', 0):>8.3f}  "
                  f"{f.get('activeCounties', 0):>6d}")

    # County details (worst/best)
    details = v4.get("countyDetails", [])
    if details:
        details = sorted(details, key=lambda x: x.get("satisfaction", 0))
        deficit_counties = [d for d in details if d.get("foodDeficit")]
        surplus_counties = [d for d in details if not d.get("foodDeficit")]

        if deficit_counties:
            print(f"\n  ── Sample Deficit Counties (worst {len(deficit_counties)}) ──")
            print(f"  {'County':>8s}  {'Pop':>8s}  {'Satisf':>7s}  {'Treasury':>10s}  {'SerfFood':>8s}  Top production")
            for d in deficit_counties[:10]:
                prod_items = d.get("production", {})
                top = sorted(prod_items.items(), key=lambda x: -x[1])[:3]
                top_str = ", ".join(f"{g}={v:.0f}" for g, v in top)
                print(f"  {d['countyId']:>8d}  {d.get('lowerCommonerPop', 0):>8,.0f}  "
                      f"{d.get('satisfaction', 0):>7.3f}  "
                      f"{d.get('upperNobleTreasury', 0):>10,.1f}  "
                      f"{d.get('serfFoodProvided', 0):>8,.1f}  {top_str}")

        if surplus_counties:
            print(f"\n  ── Sample Surplus Counties (best {len(surplus_counties)}) ──")
            print(f"  {'County':>8s}  {'Pop':>8s}  {'Satisf':>7s}  {'Treasury':>10s}  {'Income':>10s}  Top surplus")
            for d in surplus_counties[-10:]:
                surp_items = d.get("surplus", {})
                top = sorted(surp_items.items(), key=lambda x: -x[1])[:3]
                top_str = ", ".join(f"{g}={v:.0f}" for g, v in top)
                print(f"  {d['countyId']:>8d}  {d.get('lowerCommonerPop', 0):>8,.0f}  "
                      f"{d.get('satisfaction', 0):>7.3f}  "
                      f"{d.get('upperNobleTreasury', 0):>10,.1f}  "
                      f"{d.get('upperNobleIncome', 0):>10,.1f}  {top_str}")

    # Trade flows
    trade_flows = v4.get("tradeFlows", [])
    total_trade_vol = v4.get("totalTradeVolume", 0)
    total_trade_val = v4.get("totalTradeValue", 0)
    total_tariff = v4.get("totalTariffRevenue", 0)
    if trade_flows or total_trade_vol > 0:
        print(f"\n  ── Cross-Market Trade ──")
        print(f"  Total volume: {total_trade_vol:,.1f} kg  "
              f"Total value: {total_trade_val:,.2f} Cr  "
              f"Tariff revenue: {total_tariff:,.2f} Cr")
        if trade_flows:
            by_good = {}
            for tf in trade_flows:
                g = tf.get("good", "?")
                if g not in by_good:
                    by_good[g] = []
                by_good[g].append(tf)

            print(f"\n  {'Good':>12s}  {'From→To':>10s}  {'Posted':>10s}  {'Filled':>10s}  {'Value':>10s}")
            for g in sorted(by_good.keys()):
                flows = sorted(by_good[g], key=lambda x: -x.get("filled", 0))
                for tf in flows[:5]:
                    route = f"{tf.get('from', '?')}→{tf.get('to', '?')}"
                    print(f"  {g:>12s}  {route:>10s}  "
                          f"{tf.get('posted', 0):>10,.1f}  "
                          f"{tf.get('filled', 0):>10,.1f}  "
                          f"{tf.get('value', 0):>10,.2f}")

    # Markets
    markets = v4.get("markets", [])
    if markets:
        print(f"\n  ── Markets ({len(markets)}) ──")
        print(f"  {'ID':>4s}  {'Realm':>6s}  {'Counties':>8s}  {'PriceLevel':>10s}  {'M':>10s}  {'Q':>10s}")
        for m in markets:
            print(f"  {m['id']:>4d}  {m.get('hubRealmId', 0):>6d}  "
                  f"{m.get('counties', 0):>8d}  {m.get('priceLevel', 0):>10.2f}  "
                  f"{m.get('totalM', 0):>10.2f}  {m.get('totalQ', 0):>10.0f}")

        first = markets[0]
        prices = first.get("clearingPrices", {})
        if prices:
            print(f"\n  ── Clearing Prices (market {first['id']}, sample) ──")
            print(f"  {'Good':>12s}  {'Price':>8s}  {'BaseVal':>8s}  {'Ratio':>6s}")
            goods_meta = {g["name"]: g for g in v4.get("goods", [])}
            for name in sorted(prices.keys()):
                price = prices[name]
                base_val = goods_meta.get(name, {}).get("value", 0)
                ratio = f"{price / base_val:.2f}" if base_val > 0 else "—"
                print(f"  {name:>12s}  {price:>8.2f}  {base_val:>8.1f}  {ratio:>6s}")


def print_satisfaction(data: dict):
    econ = data.get("economy", {})
    sat = econ.get("satisfaction")
    if not sat:
        return

    section("Satisfaction")

    classes = [
        ("lowerCommoner", "Lower Commoner (serf)"),
        ("upperCommoner", "Upper Commoner (artisan)"),
        ("lowerNobility", "Lower Nobility"),
        ("upperNobility", "Upper Nobility"),
        ("lowerClergy", "Lower Clergy"),
        ("upperClergy", "Upper Clergy"),
    ]

    print(f"  {'Class':>28s}  {'Mean':>7s}  {'Min':>7s}  {'Max':>7s}  {'Counties':>8s}")
    for key, label in classes:
        c = sat.get(key, {})
        if c.get("counties", 0) == 0:
            continue
        print(f"  {label:>28s}  {c.get('mean', 0):>7.3f}  "
              f"{c.get('min', 0):>7.3f}  {c.get('max', 0):>7.3f}  "
              f"{c.get('counties', 0):>8d}")

    comp = sat.get("components", {})
    if comp:
        print(f"\n  ── Satisfaction Components (county means) ──")
        components = [
            ("Survival", "survivalMean", "survivalWeight"),
            ("Religion", "religionMean", "religionWeight"),
            ("Stability", "stabilityMean", "stabilityWeight"),
            ("Economic", "economicMean", "economicWeight"),
            ("Governance", "governanceMean", "governanceWeight"),
        ]
        weighted_total = 0
        for label, mean_key, weight_key in components:
            m = comp.get(mean_key, 0)
            w = comp.get(weight_key, 0)
            weighted_total += m * w
            placeholder = " (placeholder)" if label in ("Stability", "Governance") else ""
            print(f"  {label:>12s}: {m:.3f}  (weight {w:.2f}){placeholder}")
        print(f"  {'Weighted':>12s}: {weighted_total:.3f}")


def main():
    path = sys.argv[1] if len(sys.argv) > 1 else None
    data = load(path)
    init_goods(data)

    print_header(data)
    print_performance(data)
    print_satisfaction(data)
    print_economy(data)
    print_roads(data)
    print()


if __name__ == "__main__":
    main()
