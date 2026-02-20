#!/usr/bin/env python3
"""Analyze econ_debug_output.json and print a summary."""

import json
import sys
from pathlib import Path

_FALLBACK_GOODS = ["bread", "timber", "ironOre", "goldOre", "silverOre", "salt", "wool", "stone", "ale", "clay", "pottery", "furniture", "iron", "tools", "charcoal", "clothes", "pork", "sausage", "bacon", "milk", "cheese", "fish", "saltedFish", "stockfish"]
_STAPLE_GOODS = {"bread", "sausage", "cheese", "saltedFish", "stockfish"}
_FALLBACK_BASE_PRICES = [1.0, 0.5, 5.0, 0.0, 0.0, 3.0, 2.0, 0.3, 0.8, 0.2, 2.0, 5.0, 10.0, 15.0, 2.0, 3.0, 2.0, 4.0, 5.0, 1.5, 6.0, 1.5, 3.0, 2.5]
_FALLBACK_TRADEABLE = {"bread", "timber", "ironOre", "salt", "wool", "stone", "ale", "clay", "pottery", "furniture", "iron", "tools", "charcoal", "clothes", "pork", "sausage", "bacon", "milk", "cheese", "fish", "saltedFish", "stockfish"}

# Module-level references set by init_goods()
GOODS: list[str] = []
GOOD_IDX: dict[str, int] = {}
BASE_PRICES: list[float] = []
TRADEABLE: set[str] = set()


def init_goods(data: dict):
    """Initialize goods metadata from dump or fallback to hardcoded values."""
    global GOODS, GOOD_IDX, BASE_PRICES, TRADEABLE

    goods_meta = data.get("goods")
    if goods_meta:
        # Sort by index to ensure correct ordering
        goods_meta = sorted(goods_meta, key=lambda g: g["index"])
        GOODS = [g["name"] for g in goods_meta]
        GOOD_IDX = {g: i for i, g in enumerate(GOODS)}
        BASE_PRICES = [g["basePrice"] for g in goods_meta]
        TRADEABLE = {g["name"] for g in goods_meta if g["isTradeable"]}
    else:
        GOODS = list(_FALLBACK_GOODS)
        GOOD_IDX = {g: i for i, g in enumerate(GOODS)}
        BASE_PRICES = list(_FALLBACK_BASE_PRICES)
        TRADEABLE = set(_FALLBACK_TRADEABLE)


def load(path: str | None = None) -> dict:
    if path is None:
        path = Path(__file__).resolve().parent.parent / "unity" / "econ_debug_output.json"
    with open(path) as f:
        return json.load(f)


def fmt(n: float, decimals: int = 1) -> str:
    if abs(n) >= 1_000_000:
        return f"{n / 1_000_000:.{decimals}f}M"
    if abs(n) >= 1_000:
        return f"{n / 1_000:.{decimals}f}k"
    return f"{n:.{decimals}f}"


def pct(a: float, b: float) -> str:
    if b == 0:
        return "n/a"
    return f"{a / b * 100:.1f}%"


def trend(series: list[float], label: str = "") -> str:
    if len(series) < 2:
        return ""
    delta = series[-1] - series[0]
    direction = "+" if delta >= 0 else ""
    return f"{direction}{fmt(delta)}"


def section(title: str):
    print(f"\n{'=' * 60}")
    print(f"  {title}")
    print(f"{'=' * 60}")


def print_header(data: dict):
    section("SIMULATION SUMMARY")
    s = data["summary"]
    print(f"  Date:        Day {data['day']} (Year {data['year']}, Month {data['month']}, Day {data['dayOfMonth']})")
    print(f"  Ticks:       {data['totalTicks']}")
    print(f"  Population:  {fmt(s['totalPopulation'], 0)}")
    print(f"  Counties:    {s['totalCounties']}")
    print(f"  Provinces:   {s['totalProvinces']}")
    print(f"  Realms:      {s['totalRealms']}")
    print(f"  Roads:       {s['roadSegments']} road / {s['pathSegments']} path segments")


def print_performance(data: dict):
    section("PERFORMANCE")
    p = data["performance"]
    print(f"  Avg tick:    {p['avgTickMs']:.3f} ms")
    print(f"  Max tick:    {p['maxTickMs']:.3f} ms")
    print(f"  Last tick:   {p['lastTickMs']:.3f} ms")
    print()
    for name, sys in p["systems"].items():
        print(f"  [{name}]")
        print(f"    Invocations: {sys['invocations']}")
        print(f"    Avg: {sys['avgMs']:.3f} ms  Max: {sys['maxMs']:.3f} ms  Total: {fmt(sys['totalMs'])} ms")


def print_economy(data: dict):
    section("ECONOMY")
    e = data["economy"]
    ts = e["timeSeries"]
    first, last = ts[0], ts[-1]

    print(f"  Counties:    {e['countyCount']}")
    print(f"  Productivity (bread): avg={e['avgProductivity']:.3f}  min={e['minProductivity']:.3f}  max={e['maxProductivity']:.3f}")
    print()

    # Per-good productivity
    print("  Productivity by good:")
    for good, info in e["productivityByGood"].items():
        print(f"    {good:8s}  avg={info['avg']:.3f}  min={info['min']:.3f}  max={info['max']:.3f}")

    # Production vs consumption balance
    section("PRODUCTION / CONSUMPTION BALANCE (latest day)")
    print(f"  {'Good':8s}  {'Production':>12s}  {'Consumption':>12s}  {'Surplus':>12s}  {'Unmet Need':>12s}  {'Cons%':>6s}")
    print(f"  {'-'*8}  {'-'*12}  {'-'*12}  {'-'*12}  {'-'*12}  {'-'*6}")
    for i, good in enumerate(GOODS):
        prod = last["productionByGood"][i]
        cons = last["consumptionByGood"][i]
        unmet = last["unmetNeedByGood"][i]
        surplus = prod - cons
        print(f"  {good:8s}  {fmt(prod):>12s}  {fmt(cons):>12s}  {fmt(surplus):>12s}  {fmt(unmet):>12s}  {pct(cons, prod):>6s}")

    total_prod = last["totalProduction"]
    total_cons = last["totalConsumption"]
    total_unmet = last["totalUnmetNeed"]
    print(f"  {'TOTAL':8s}  {fmt(total_prod):>12s}  {fmt(total_cons):>12s}  {fmt(total_prod - total_cons):>12s}  {fmt(total_unmet):>12s}  {pct(total_cons, total_prod):>6s}")


def print_stocks(data: dict):
    section("STOCKPILE TRENDS")
    ts = data["economy"]["timeSeries"]
    first, last = ts[0], ts[-1]

    print(f"  Total stock: {fmt(first['totalStock'])} -> {fmt(last['totalStock'])}  ({trend([first['totalStock'], last['totalStock']])})")
    print()
    print(f"  {'Good':8s}  {'Day 2':>10s}  {'Latest':>10s}  {'Change':>10s}")
    print(f"  {'-'*8}  {'-'*10}  {'-'*10}  {'-'*10}")
    for i, good in enumerate(GOODS):
        s0 = first["stockByGood"][i]
        s1 = last["stockByGood"][i]
        print(f"  {good:8s}  {fmt(s0):>10s}  {fmt(s1):>10s}  {trend([s0, s1]):>10s}")

    # County health
    section("COUNTY HEALTH TRENDS")
    print(f"  {'Metric':20s}  {'Day 2':>8s}  {'Latest':>8s}  {'Change':>8s}")
    print(f"  {'-'*20}  {'-'*8}  {'-'*8}  {'-'*8}")
    for key, label in [
        ("surplusCounties", "Surplus counties"),
        ("deficitCounties", "Deficit counties"),
        ("starvingCounties", "Starving counties"),
    ]:
        v0, v1 = first[key], last[key]
        delta = v1 - v0
        sign = "+" if delta >= 0 else ""
        print(f"  {label:20s}  {v0:>8d}  {v1:>8d}  {sign}{delta:>7d}")

    # Find when starvation bottomed out
    starving = [t["starvingCounties"] for t in ts]
    min_starving = min(starving)
    min_day = ts[starving.index(min_starving)]["day"]
    print(f"\n  Starvation low: {min_starving} counties on day {min_day}")


def print_fiscal(data: dict):
    section("FISCAL SYSTEM (latest day)")
    ts = data["economy"]["timeSeries"]
    last = ts[-1]

    print(f"  Ducal tax:       {fmt(last['ducalTax']):>10s}    Relief: {fmt(last['ducalRelief']):>10s}    Provincial stockpile: {fmt(last['provincialStockpile'])}")
    print(f"  Royal tax:       {fmt(last['royalTax']):>10s}    Relief: {fmt(last['royalRelief']):>10s}    Royal stockpile:      {fmt(last['royalStockpile'])}")

    print()
    print(f"  {'Good':8s}  {'Ducal Tax':>12s}  {'Ducal Relief':>12s}  {'Royal Tax':>12s}")
    print(f"  {'-'*8}  {'-'*12}  {'-'*12}  {'-'*12}")
    for i, good in enumerate(GOODS):
        dt = last["ducalTaxByGood"][i]
        dr = last["ducalReliefByGood"][i]
        rt = last["royalTaxByGood"][i]
        print(f"  {good:8s}  {fmt(dt):>12s}  {fmt(dr):>12s}  {fmt(rt):>12s}")

    # Fiscal evolution
    section("FISCAL TRENDS (first 5 days vs last 5 days)")
    early = ts[4:9]  # days 6-10 (first days with fiscal data)
    late = ts[-5:]
    if early and late:
        avg_early_tax = sum(t["ducalTax"] for t in early) / len(early)
        avg_late_tax = sum(t["ducalTax"] for t in late) / len(late)
        avg_early_relief = sum(t["ducalRelief"] for t in early) / len(early)
        avg_late_relief = sum(t["ducalRelief"] for t in late) / len(late)
        print(f"  Avg ducal tax:    {fmt(avg_early_tax):>10s} -> {fmt(avg_late_tax):>10s}")
        print(f"  Avg ducal relief: {fmt(avg_early_relief):>10s} -> {fmt(avg_late_relief):>10s}")
        surplus_early = avg_early_tax - avg_early_relief
        surplus_late = avg_late_tax - avg_late_relief
        print(f"  Net ducal surplus:{fmt(surplus_early):>10s} -> {fmt(surplus_late):>10s}")


def print_trade_snapshot(data: dict):
    section("TRADE / FISCAL SNAPSHOT (end of sim)")
    t = data["trade"]
    print(f"  Tax-paying counties:      {t['taxPayingCounties']}")
    print(f"  Relief-receiving counties: {t['reliefReceivingCounties']}")
    print()
    print(f"  {'Good':8s}  {'Ducal Tax':>12s}  {'Ducal Relief':>12s}")
    print(f"  {'-'*8}  {'-'*12}  {'-'*12}")
    for good in GOODS:
        dt = t["ducalTaxByGood"].get(good, 0)
        dr = t["ducalReliefByGood"].get(good, 0)
        print(f"  {good:8s}  {fmt(dt):>12s}  {fmt(dr):>12s}")

    # Provincial stockpiles
    print()
    print(f"  Total provincial stockpile: {fmt(t['totalProvincialStockpile'])}")
    for good in GOODS:
        v = t["provincialStockpileByGood"].get(good, 0)
        print(f"    {good:8s}: {fmt(v)}")

    # Royal stockpiles
    print()
    print(f"  Total royal stockpile: {fmt(t['totalRoyalStockpile'])}")
    for good in GOODS:
        v = t["royalStockpileByGood"].get(good, 0)
        print(f"    {good:8s}: {fmt(v)}")

    # Market fees
    market_fees = t.get("marketFeesCollected", 0)
    market_county = t.get("marketCountyId", -1)
    print()
    print(f"  Market fees collected: {fmt(market_fees)} (county {market_county})")

    # Per-realm breakdown
    print()
    print(f"  Realm stockpiles:")
    for r in t["realms"]:
        parts = []
        for good in GOODS:
            v = r["stockpileByGood"].get(good, 0)
            parts.append(f"{good}={fmt(v):>8s}")
        print(f"    Realm {r['id']:2d}: {'  '.join(parts)}")


def print_treasury(data: dict):
    section("TREASURY")
    ts = data["economy"]["timeSeries"]
    last = ts[-1]
    first = ts[0]

    print(f"  Realm treasury (latest):    {fmt(last.get('treasury', 0))} Crowns")
    print(f"  County treasury (latest):   {fmt(last.get('countyTreasury', 0))} Crowns")
    print(f"  Province treasury (latest): {fmt(last.get('provinceTreasury', 0))} Crowns")
    print(f"  Domestic total (latest):    {fmt(last.get('domesticTreasury', 0))} Crowns")
    print(f"  Treasury trend:             {fmt(first.get('treasury', 0))} -> {fmt(last.get('treasury', 0))}  ({trend([first.get('treasury', 0), last.get('treasury', 0)])})")
    print()
    print(f"  Daily crown flows (latest):")
    print(f"    Ducal tax crowns (prov→county):  {fmt(last.get('ducalTaxCrowns', 0))}")
    print(f"    Royal tax crowns (realm→prov):    {fmt(last.get('royalTaxCrowns', 0))}")
    print(f"    Ducal relief crowns (county→prov): {fmt(last.get('ducalReliefCrowns', 0))}")
    print(f"    Royal relief crowns (prov→realm):  {fmt(last.get('royalReliefCrowns', 0))}")
    print()
    print(f"  Daily minting (latest):")
    print(f"    Gold minted:   {fmt(last.get('goldMinted', 0))} kg")
    print(f"    Silver minted: {fmt(last.get('silverMinted', 0))} kg")
    print(f"    Crowns minted: {fmt(last.get('crownsMinted', 0))} Crowns")

    # Per-realm treasury breakdown
    t = data.get("trade", {})
    realms = t.get("realms", [])
    if realms:
        print()
        print(f"  Per-realm treasury:")
        print(f"    {'Realm':>6s}  {'Treasury':>12s}  {'Gold/day':>10s}  {'Silver/day':>10s}  {'Crowns/day':>12s}")
        print(f"    {'-'*6}  {'-'*12}  {'-'*10}  {'-'*10}  {'-'*12}")
        for r in sorted(realms, key=lambda x: x.get("treasury", 0), reverse=True):
            print(f"    {r['id']:>6d}  {fmt(r.get('treasury', 0)):>12s}  {fmt(r.get('goldMinted', 0)):>10s}  {fmt(r.get('silverMinted', 0)):>10s}  {fmt(r.get('crownsMinted', 0)):>12s}")

    # Treasury time series trend (sample 5 points)
    if len(ts) >= 10:
        print()
        print(f"  Treasury over time (realm / county / province / domestic total):")
        indices = [0, len(ts)//4, len(ts)//2, 3*len(ts)//4, len(ts)-1]
        for idx in indices:
            snap = ts[idx]
            print(f"    Day {snap['day']:>4d}: realm={fmt(snap.get('treasury', 0)):>10s}  county={fmt(snap.get('countyTreasury', 0)):>10s}  prov={fmt(snap.get('provinceTreasury', 0)):>10s}  total={fmt(snap.get('domesticTreasury', 0)):>10s}")


def print_intra_province_trade(data: dict):
    section("INTRA-PROVINCE TRADE")
    ts = data["economy"]["timeSeries"]
    last = ts[-1]

    # Time series data
    bought = last.get("intraProvTradeBoughtByGood")
    sold = last.get("intraProvTradeSoldByGood")
    spending = last.get("intraProvTradeSpending", 0)
    revenue = last.get("intraProvTradeRevenue", 0)

    if not bought and not sold:
        # Try trade section (end-of-sim snapshot)
        t = data.get("trade", {})
        bought_map = t.get("intraProvTradeBoughtByGood", {})
        sold_map = t.get("intraProvTradeSoldByGood", {})
        spending = t.get("intraProvTradeSpending", 0)
        revenue = t.get("intraProvTradeRevenue", 0)

        if not bought_map and not sold_map:
            print("  No intra-province trade data")
            return

        print(f"  {'Good':12s}  {'Bought':>10s}  {'Sold':>10s}")
        print(f"  {'-'*12}  {'-'*10}  {'-'*10}")
        for good in GOODS:
            b = bought_map.get(good, 0)
            s = sold_map.get(good, 0)
            if b > 0 or s > 0:
                print(f"  {good:12s}  {fmt(b):>10s}  {fmt(s):>10s}")
    else:
        n = len(GOODS)
        if not bought:
            bought = [0] * n
        if not sold:
            sold = [0] * n

        print(f"  Daily Volumes (latest day):")
        print(f"  {'Good':12s}  {'Bought':>10s}  {'Sold':>10s}")
        print(f"  {'-'*12}  {'-'*10}  {'-'*10}")
        for i, good in enumerate(GOODS):
            b = bought[i] if i < len(bought) else 0
            s = sold[i] if i < len(sold) else 0
            if b > 0 or s > 0:
                print(f"  {good:12s}  {fmt(b):>10s}  {fmt(s):>10s}")

    print()
    print(f"  Crown flows (latest day):")
    print(f"    Spending (buyers):  {fmt(spending)} Crowns  (+ 2% market fee)")
    print(f"    Revenue (sellers):  {fmt(revenue)} Crowns")

    # Trend over time
    if len(ts) >= 20:
        early = ts[4:14]
        late = ts[-10:]
        early_sp = sum(t.get("intraProvTradeSpending", 0) for t in early) / len(early)
        late_sp = sum(t.get("intraProvTradeSpending", 0) for t in late) / len(late)
        print()
        print(f"  Trade spending trend: {fmt(early_sp)}/day -> {fmt(late_sp)}/day")


def print_cross_province_trade(data: dict):
    section("CROSS-PROVINCE TRADE")
    ts = data["economy"]["timeSeries"]
    last = ts[-1]

    # Time series data
    bought = last.get("crossProvTradeBoughtByGood")
    sold = last.get("crossProvTradeSoldByGood")
    spending = last.get("crossProvTradeSpending", 0)
    revenue = last.get("crossProvTradeRevenue", 0)
    tolls_paid = last.get("tradeTollsPaid", 0)
    tolls_collected = last.get("tradeTollsCollected", 0)

    if not bought and not sold:
        # Try trade section (end-of-sim snapshot)
        t = data.get("trade", {})
        bought_map = t.get("crossProvTradeBoughtByGood", {})
        sold_map = t.get("crossProvTradeSoldByGood", {})
        spending = t.get("crossProvTradeSpending", 0)
        revenue = t.get("crossProvTradeRevenue", 0)
        tolls_paid = t.get("tradeTollsPaid", 0)
        tolls_collected = t.get("tradeTollsCollected", 0)

        if not bought_map and not sold_map:
            print("  No cross-province trade data")
            return

        print(f"  {'Good':12s}  {'Bought':>10s}  {'Sold':>10s}")
        print(f"  {'-'*12}  {'-'*10}  {'-'*10}")
        for good in GOODS:
            b = bought_map.get(good, 0)
            s = sold_map.get(good, 0)
            if b > 0 or s > 0:
                print(f"  {good:12s}  {fmt(b):>10s}  {fmt(s):>10s}")
    else:
        n = len(GOODS)
        if not bought:
            bought = [0] * n
        if not sold:
            sold = [0] * n

        print(f"  Daily Volumes (latest day):")
        print(f"  {'Good':12s}  {'Bought':>10s}  {'Sold':>10s}")
        print(f"  {'-'*12}  {'-'*10}  {'-'*10}")
        for i, good in enumerate(GOODS):
            b = bought[i] if i < len(bought) else 0
            s = sold[i] if i < len(sold) else 0
            if b > 0 or s > 0:
                print(f"  {good:12s}  {fmt(b):>10s}  {fmt(s):>10s}")

    print()
    print(f"  Crown flows (latest day):")
    print(f"    Spending (buyers→sellers):  {fmt(spending)} Crowns  (+ 5% toll + 2% market fee)")
    print(f"    Revenue (sellers):           {fmt(revenue)} Crowns")
    print(f"    Tolls paid (buyers→prov):    {fmt(tolls_paid)} Crowns")
    print(f"    Tolls collected (provinces): {fmt(tolls_collected)} Crowns")

    # Trend over time
    if len(ts) >= 20:
        early = ts[4:14]
        late = ts[-10:]
        early_sp = sum(t.get("crossProvTradeSpending", 0) for t in early) / len(early)
        late_sp = sum(t.get("crossProvTradeSpending", 0) for t in late) / len(late)
        early_toll = sum(t.get("tradeTollsPaid", 0) for t in early) / len(early)
        late_toll = sum(t.get("tradeTollsPaid", 0) for t in late) / len(late)
        print()
        print(f"  Trade spending trend: {fmt(early_sp)}/day -> {fmt(late_sp)}/day")
        print(f"  Toll revenue trend:   {fmt(early_toll)}/day -> {fmt(late_toll)}/day")


def print_cross_realm_trade(data: dict):
    section("CROSS-REALM TRADE")
    ts = data["economy"]["timeSeries"]
    last = ts[-1]

    # Time series data
    bought = last.get("crossRealmTradeBoughtByGood")
    sold = last.get("crossRealmTradeSoldByGood")
    spending = last.get("crossRealmTradeSpending", 0)
    revenue = last.get("crossRealmTradeRevenue", 0)
    tolls_paid = last.get("crossRealmTollsPaid", 0)
    tariffs_paid = last.get("crossRealmTariffsPaid", 0)
    tariffs_collected = last.get("crossRealmTariffsCollected", 0)

    if not bought and not sold:
        # Try trade section (end-of-sim snapshot)
        t = data.get("trade", {})
        bought_map = t.get("crossRealmTradeBoughtByGood", {})
        sold_map = t.get("crossRealmTradeSoldByGood", {})
        spending = t.get("crossRealmTradeSpending", 0)
        revenue = t.get("crossRealmTradeRevenue", 0)
        tolls_paid = t.get("crossRealmTollsPaid", 0)
        tariffs_paid = t.get("crossRealmTariffsPaid", 0)
        tariffs_collected = t.get("tradeTariffsCollected", 0)

        if not bought_map and not sold_map:
            print("  No cross-realm trade data")
            return

        print(f"  {'Good':12s}  {'Bought':>10s}  {'Sold':>10s}")
        print(f"  {'-'*12}  {'-'*10}  {'-'*10}")
        for good in GOODS:
            b = bought_map.get(good, 0)
            s = sold_map.get(good, 0)
            if b > 0 or s > 0:
                print(f"  {good:12s}  {fmt(b):>10s}  {fmt(s):>10s}")
    else:
        n = len(GOODS)
        if not bought:
            bought = [0] * n
        if not sold:
            sold = [0] * n

        print(f"  Daily Volumes (latest day):")
        print(f"  {'Good':12s}  {'Bought':>10s}  {'Sold':>10s}")
        print(f"  {'-'*12}  {'-'*10}  {'-'*10}")
        for i, good in enumerate(GOODS):
            b = bought[i] if i < len(bought) else 0
            s = sold[i] if i < len(sold) else 0
            if b > 0 or s > 0:
                print(f"  {good:12s}  {fmt(b):>10s}  {fmt(s):>10s}")

    print()
    print(f"  Crown flows (latest day):")
    print(f"    Spending (buyers→sellers):     {fmt(spending)} Crowns  (+ 5% toll + 10% tariff + 2% market fee)")
    print(f"    Revenue (sellers):              {fmt(revenue)} Crowns")
    print(f"    Tolls paid (buyers→prov):       {fmt(tolls_paid)} Crowns")
    print(f"    Tariffs paid (buyers→realm):    {fmt(tariffs_paid)} Crowns")
    print(f"    Tariffs collected (realms):     {fmt(tariffs_collected)} Crowns")
    print(f"    Tariff balance (paid-collected): {fmt(tariffs_paid - tariffs_collected)} Crowns")

    # Trend over time
    if len(ts) >= 20:
        early = ts[4:14]
        late = ts[-10:]
        early_sp = sum(t.get("crossRealmTradeSpending", 0) for t in early) / len(early)
        late_sp = sum(t.get("crossRealmTradeSpending", 0) for t in late) / len(late)
        early_tar = sum(t.get("crossRealmTariffsPaid", 0) for t in early) / len(early)
        late_tar = sum(t.get("crossRealmTariffsPaid", 0) for t in late) / len(late)
        print()
        print(f"  Trade spending trend:  {fmt(early_sp)}/day -> {fmt(late_sp)}/day")
        print(f"  Tariff revenue trend:  {fmt(early_tar)}/day -> {fmt(late_tar)}/day")


def print_market_fees(data: dict):
    section("MARKET FEES")
    ts = data["economy"]["timeSeries"]
    last = ts[-1]

    fees = last.get("marketFeesCollected", 0)
    market_county = data.get("trade", {}).get("marketCountyId",
                     data.get("summary", {}).get("marketCountyId", -1))

    print(f"  Market county ID:     {market_county}")
    print(f"  Daily fees (latest):  {fmt(fees)} Crowns")

    # Trend over time
    if len(ts) >= 20:
        early = ts[4:14]
        late = ts[-10:]
        early_fees = sum(t.get("marketFeesCollected", 0) for t in early) / len(early)
        late_fees = sum(t.get("marketFeesCollected", 0) for t in late) / len(late)
        print(f"  Fee trend:            {fmt(early_fees)}/day -> {fmt(late_fees)}/day")

    # Cumulative estimate
    total_fees = sum(t.get("marketFeesCollected", 0) for t in ts)
    print(f"  Cumulative (approx):  {fmt(total_fees)} Crowns")


def print_inter_realm_trade(data: dict):
    section("INTER-REALM TRADE")
    ts = data["economy"]["timeSeries"]
    last = ts[-1]

    # Market prices
    prices = last.get("marketPrices")
    if not prices:
        print("  No inter-realm trade data (marketPrices missing)")
        return

    tradeable_names = [g for g in GOODS if g in TRADEABLE]
    TRADEABLE_IDX = [GOOD_IDX[g] for g in tradeable_names]

    print("  Market Prices (Crowns/kg):")
    print(f"    {'Good':8s}  {'Price':>10s}  {'Base':>8s}  {'Ratio':>8s}")
    print(f"    {'-'*8}  {'-'*10}  {'-'*8}  {'-'*8}")
    for g in TRADEABLE_IDX:
        p = prices[g]
        bp = BASE_PRICES[g]
        ratio = f"{p/bp:.2f}x" if bp > 0 else "n/a"
        print(f"    {GOODS[g]:8s}  {fmt(p, 2):>10s}  {fmt(bp, 2):>8s}  {ratio:>8s}")

    # Trade volumes
    n = len(GOODS)
    imports = last.get("tradeImportsByGood", [0]*n)
    exports = last.get("tradeExportsByGood", [0]*n)
    deficits = last.get("realmDeficitByGood", [0]*n)

    print()
    print("  Trade Volumes (latest day):")
    print(f"    {'Good':8s}  {'Imports':>10s}  {'Exports':>10s}  {'Deficit':>10s}  {'Fill%':>8s}")
    print(f"    {'-'*8}  {'-'*10}  {'-'*10}  {'-'*10}  {'-'*8}")
    for g in TRADEABLE_IDX:
        imp = imports[g]
        exp = exports[g]
        deficit = deficits[g]
        fill = pct(imp, deficit) if deficit > 0 else "n/a"
        print(f"    {GOODS[g]:8s}  {fmt(imp):>10s}  {fmt(exp):>10s}  {fmt(deficit):>10s}  {fill:>8s}")

    # Total trade spending/revenue
    spending = last.get("tradeSpending", 0)
    revenue = last.get("tradeRevenue", 0)
    print()
    print(f"  Total trade spending: {fmt(spending)} Crowns")
    print(f"  Total trade revenue:  {fmt(revenue)} Crowns")

    # Per-realm trade activity
    t = data.get("trade", {})
    realms = t.get("realms", [])
    if realms:
        print()
        print("  Per-Realm Trade Activity:")
        print(f"    {'Realm':>6s}  {'Treasury':>10s}  {'Spending':>10s}  {'Revenue':>10s}  {'Net':>10s}")
        print(f"    {'-'*6}  {'-'*10}  {'-'*10}  {'-'*10}  {'-'*10}")
        for r in sorted(realms, key=lambda x: x.get("tradeSpending", 0), reverse=True):
            treas = r.get("treasury", 0)
            sp = r.get("tradeSpending", 0)
            rev = r.get("tradeRevenue", 0)
            net = rev - sp
            print(f"    {r['id']:>6d}  {fmt(treas):>10s}  {fmt(sp):>10s}  {fmt(rev):>10s}  {fmt(net):>10s}")

    # Price trends (early vs late)
    if len(ts) >= 20:
        early_snaps = [t for t in ts[4:14] if t.get("marketPrices")]
        late_snaps = [t for t in ts[-10:] if t.get("marketPrices")]
        if early_snaps and late_snaps:
            print()
            print("  Price Trends (early vs late):")
            print(f"    {'Good':8s}  {'Early':>10s}  {'Late':>10s}  {'Change':>10s}")
            print(f"    {'-'*8}  {'-'*10}  {'-'*10}  {'-'*10}")
            for g in TRADEABLE_IDX:
                early_p = sum(s["marketPrices"][g] for s in early_snaps) / len(early_snaps)
                late_p = sum(s["marketPrices"][g] for s in late_snaps) / len(late_snaps)
                delta = late_p - early_p
                sign = "+" if delta >= 0 else ""
                print(f"    {GOODS[g]:8s}  {fmt(early_p, 2):>10s}  {fmt(late_p, 2):>10s}  {sign}{fmt(delta, 2):>9s}")


def print_roads(data: dict):
    section("ROAD NETWORK")
    r = data["roads"]
    print(f"  Total segments: {r['totalSegments']} ({r['roads']} roads + {r['paths']} paths)")
    print(f"  Total traffic:  {fmt(r['totalTraffic'])}")
    print()
    print(f"  Top 5 busiest segments:")
    seen = set()
    top = []
    for seg in r["busiestSegments"]:
        key = (min(seg["cellA"], seg["cellB"]), max(seg["cellA"], seg["cellB"]))
        if key not in seen:
            seen.add(key)
            top.append(seg)
        if len(top) >= 5:
            break
    for seg in top:
        print(f"    {seg['cellA']:>6d} <-> {seg['cellB']:>6d}  tier={seg['tier']:4s}  traffic={seg['traffic']:.2f}")


def print_population(data: dict):
    section("POPULATION DYNAMICS")
    ts = data["economy"]["timeSeries"]
    if not ts:
        print("  No time series data")
        return

    first, last = ts[0], ts[-1]
    pop_first = first.get("totalPopulation", 0)
    pop_last = last.get("totalPopulation", 0)

    if pop_first == 0 and pop_last == 0:
        print("  No population data in time series (run longer or update bridge)")
        return

    days = last["day"] - first["day"]
    growth = pop_last - pop_first
    growth_pct = (growth / pop_first * 100) if pop_first > 0 else 0

    print(f"  Population:  {fmt(pop_first, 0)} -> {fmt(pop_last, 0)}  ({'+' if growth >= 0 else ''}{fmt(growth, 0)}, {'+' if growth_pct >= 0 else ''}{growth_pct:.2f}%)")
    if days > 0:
        annual_rate = (pop_last / pop_first) ** (365 / days) - 1 if pop_first > 0 else 0
        print(f"  Annualized:  {'+' if annual_rate >= 0 else ''}{annual_rate * 100:.2f}%/yr over {days} days")

    # Latest satisfaction
    avg_sat = last.get("avgBasicSatisfaction", last.get("avgFoodSatisfaction", 0))
    min_sat = last.get("minBasicSatisfaction", last.get("minFoodSatisfaction", 0))
    max_sat = last.get("maxBasicSatisfaction", last.get("maxFoodSatisfaction", 0))
    distress = last.get("countiesInDistress", 0)
    print(f"\n  Basic Satisfaction (latest):")
    print(f"    Average:   {avg_sat:.3f}")
    print(f"    Min:       {min_sat:.3f}")
    print(f"    Max:       {max_sat:.3f}")
    print(f"    Distressed counties (<0.5): {distress}")

    # Births / deaths from latest snapshot
    births = last.get("totalBirths", 0)
    deaths = last.get("totalDeaths", 0)
    print(f"\n  Monthly Vitals (latest):")
    print(f"    Births:    {fmt(births, 0)}")
    print(f"    Deaths:    {fmt(deaths, 0)}")
    print(f"    Natural:   {'+' if births - deaths >= 0 else ''}{fmt(births - deaths, 0)}")

    # Satisfaction trend (sample 5 points)
    if len(ts) >= 10:
        print(f"\n  Satisfaction over time:")
        indices = [0, len(ts)//4, len(ts)//2, 3*len(ts)//4, len(ts)-1]
        for idx in indices:
            snap = ts[idx]
            pop = snap.get("totalPopulation", 0)
            sat = snap.get("avgBasicSatisfaction", snap.get("avgFoodSatisfaction", 0))
            dist = snap.get("countiesInDistress", 0)
            print(f"    Day {snap['day']:>4d}: pop={fmt(pop, 0):>8s}  sat={sat:.3f}  distress={dist}")

    # Population trend (first vs last 30 days)
    if len(ts) >= 60:
        early_pops = [t.get("totalPopulation", 0) for t in ts[:30] if t.get("totalPopulation", 0) > 0]
        late_pops = [t.get("totalPopulation", 0) for t in ts[-30:] if t.get("totalPopulation", 0) > 0]
        if early_pops and late_pops:
            avg_early = sum(early_pops) / len(early_pops)
            avg_late = sum(late_pops) / len(late_pops)
            pop_change = avg_late - avg_early
            print(f"\n  Population trend:")
            print(f"    First 30d avg: {fmt(avg_early, 0)}")
            print(f"    Last 30d avg:  {fmt(avg_late, 0)}")
            print(f"    Change:        {'+' if pop_change >= 0 else ''}{fmt(pop_change, 0)}")


def print_facilities(data: dict):
    fac = data.get("facilities")
    if not fac or fac.get("totalCount", 0) == 0:
        return
    section("FACILITIES")
    print(f"  Total facilities: {fac['totalCount']}")
    print(f"  Total workers:    {fac['totalWorkers']}")
    by_type = fac.get("byType", {})
    if by_type:
        print()
        # Detect format: old (int count) vs new (object with details)
        first_val = next(iter(by_type.values()))
        if isinstance(first_val, dict):
            print(f"  {'Type':12s}  {'Count':>6s}  {'Recipe':30s}  {'Labor':>6s}  {'Throughput':>12s}  {'Workers':>10s}")
            print(f"  {'-'*12}  {'-'*6}  {'-'*30}  {'-'*6}  {'-'*12}  {'-'*10}")
            for name, info in by_type.items():
                inputs = info.get('inputs')
                if inputs:
                    in_parts = " + ".join(f"{inp['amount']:g} {inp['good']}" for inp in inputs)
                    recipe = f"{in_parts} -> {info['outputAmount']:.0f} {info['outputGood']}"
                else:
                    recipe = f"? -> {info['outputAmount']:.0f} {info['outputGood']}"
                actual_tp = info.get('actualThroughput', 0)
                actual_wk = info.get('actualWorkers', 0)
                tp_str = f"{fmt(actual_tp)}/{fmt(info['expectedThroughput'])}"
                print(f"  {name:12s}  {info['count']:>6d}  {recipe:30s}  {info['laborPerUnit']:>6d}  {tp_str:>12s}  {fmt(actual_wk):>10s}")
        else:
            print(f"  By type:")
            for name, count in by_type.items():
                print(f"    {name:12s}: {count}")


def print_production_chains(data: dict):
    """Show supply/demand analysis for refined goods (produced by facilities)."""
    fac = data.get("facilities", {})
    by_type = fac.get("byType", {})
    if not by_type or not isinstance(next(iter(by_type.values()), None), dict):
        return

    # Collect refined goods from facility output
    refined = {}  # goodName -> {facilityName, count, expectedThroughput, actualThroughput, actualWorkers}
    for name, info in by_type.items():
        out = info["outputGood"]
        refined[out] = {
            "facility": name,
            "count": info["count"],
            "throughput": info["expectedThroughput"],
            "actualThroughput": info.get("actualThroughput", 0),
            "actualWorkers": info.get("actualWorkers", 0),
            "inputs": info.get("inputs", []),
        }

    if not refined:
        return

    ts = data["economy"]["timeSeries"]
    if len(ts) < 2:
        return

    section("PRODUCTION CHAINS")

    last = ts[-1]
    for good_name, chain in refined.items():
        if good_name not in GOOD_IDX:
            continue
        g = GOOD_IDX[good_name]

        prod = last["productionByGood"][g]
        cons = last["consumptionByGood"][g]
        unmet = last["unmetNeedByGood"][g]
        demand = cons + unmet
        coverage = (cons / demand * 100) if demand > 0 else 0

        actual_tp = chain.get("actualThroughput", 0)
        actual_wk = chain.get("actualWorkers", 0)

        print(f"\n  {good_name.upper()} (from {chain['facility']} x{chain['count']})")
        for inp in chain.get("inputs", []):
            input_name = inp["good"]
            ig = GOOD_IDX.get(input_name)
            input_prod = last["productionByGood"][ig] if ig is not None else 0
            input_stock = last["stockByGood"][ig] if ig is not None else 0
            print(f"    Input ({input_name}):  production={fmt(input_prod)}/day  stock={fmt(input_stock)}")
        print(f"    Output:           production={fmt(prod)}/day  throughput={fmt(actual_tp)}/day (max={fmt(chain['throughput'])}/day)")
        print(f"    Workers:          {fmt(actual_wk)} actual")
        print(f"    Demand:           consumption={fmt(cons)}/day  unmet={fmt(unmet)}/day  total={fmt(demand)}/day")
        print(f"    Coverage:         {coverage:.1f}%")

        # Early vs late trend
        if len(ts) >= 20:
            early = ts[4:14]
            late = ts[-10:]
            early_prod = sum(t["productionByGood"][g] for t in early) / len(early)
            late_prod = sum(t["productionByGood"][g] for t in late) / len(late)
            early_unmet = sum(t["unmetNeedByGood"][g] for t in early) / len(early)
            late_unmet = sum(t["unmetNeedByGood"][g] for t in late) / len(late)
            print(f"    Trend:            prod {fmt(early_prod)} -> {fmt(late_prod)}  unmet {fmt(early_unmet)} -> {fmt(late_unmet)}")


def print_convergence(data: dict):
    section("CONVERGENCE ANALYSIS")
    ts = data["economy"]["timeSeries"]
    if len(ts) < 20:
        print("  Not enough data for convergence analysis")
        return

    # Staple goods indices for pooled analysis
    staple_indices = [i for i, g in enumerate(GOODS) if g in _STAPLE_GOODS]

    def total_staple_unmet(snap):
        return sum(snap["unmetNeedByGood"][i] for i in staple_indices)

    starving = [t["starvingCounties"] for t in ts]

    # Rate of change over last 30 days
    window = min(30, len(ts))
    recent = ts[-window:]
    staple_rate = (total_staple_unmet(recent[-1]) - total_staple_unmet(recent[0])) / window
    starving_rate = (recent[-1]["starvingCounties"] - recent[0]["starvingCounties"]) / window
    stock_rate = (recent[-1]["totalStock"] - recent[0]["totalStock"]) / window

    print(f"  Last {window} days (daily averages):")
    print(f"    Staple unmet need change: {staple_rate:+.1f}/day")
    print(f"    Starving counties change: {starving_rate:+.2f}/day")
    print(f"    Total stock change:       {fmt(stock_rate)}/day")

    # Steady state check: is stock growth slowing?
    if len(ts) >= 60:
        mid = len(ts) // 2
        first_half_rate = (ts[mid]["totalStock"] - ts[0]["totalStock"]) / mid
        second_half_rate = (ts[-1]["totalStock"] - ts[mid]["totalStock"]) / (len(ts) - mid)
        print(f"\n  Stock growth rate: first half={fmt(first_half_rate)}/day, second half={fmt(second_half_rate)}/day")
        if second_half_rate < first_half_rate * 0.5:
            print("  -> Growth decelerating (approaching equilibrium)")
        elif second_half_rate < first_half_rate:
            print("  -> Growth slowing")
        else:
            print("  -> Growth not slowing (not yet near equilibrium)")

    # Staple pool (bread + sausage + cheese)
    latest_staple_unmet = total_staple_unmet(ts[-1])
    latest_staple_prod = sum(ts[-1]["productionByGood"][i] for i in staple_indices)
    latest_staple_cons = sum(ts[-1]["consumptionByGood"][i] for i in staple_indices)
    staple_names = "+".join(GOODS[i] for i in staple_indices)
    print(f"\n  Staple pool ({staple_names}):")
    print(f"    Production:  {fmt(latest_staple_prod)}/day")
    print(f"    Consumption: {fmt(latest_staple_cons)}/day")
    print(f"    Unmet need:  {fmt(latest_staple_unmet)}")
    print(f"  Starving:     {ts[-1]['starvingCounties']}/{data['economy']['countyCount']} counties")

    # Per-good status
    for i, good in enumerate(GOODS):
        unmet = ts[-1]["unmetNeedByGood"][i]
        cons = ts[-1]["consumptionByGood"][i]
        demand = cons + unmet
        if demand == 0:
            print(f"\n  {good:8s}: no consumption")
        elif unmet == 0:
            for t in ts:
                if t["unmetNeedByGood"][i] == 0:
                    print(f"\n  {good:8s}: fully supplied since day {t['day']}")
                    break
        else:
            print(f"\n  {good:8s}: {fmt(unmet)} unmet ({pct(unmet, demand)} of demand)")


def main():
    path = sys.argv[1] if len(sys.argv) > 1 else None
    data = load(path)
    init_goods(data)

    print_header(data)
    print_performance(data)
    print_economy(data)
    print_stocks(data)
    print_fiscal(data)
    print_trade_snapshot(data)
    print_treasury(data)
    print_intra_province_trade(data)
    print_cross_province_trade(data)
    print_cross_realm_trade(data)
    print_market_fees(data)
    print_inter_realm_trade(data)
    print_roads(data)
    print_facilities(data)
    print_production_chains(data)
    print_population(data)
    print_convergence(data)
    print()


if __name__ == "__main__":
    main()
