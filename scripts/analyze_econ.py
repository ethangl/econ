#!/usr/bin/env python3
"""Analyze econ_debug_output.json and print a summary."""

import json
import sys
from pathlib import Path

_FALLBACK_GOODS = ["wheat", "timber", "ironOre", "goldOre", "silverOre", "salt", "wool", "stone", "barley", "clay", "pottery", "furniture", "iron", "tools", "charcoal", "clothes", "pork", "sausage", "bacon", "milk", "cheese", "fish", "saltedFish", "stockfish", "bread", "ale", "gold", "silver", "goldJewelry", "silverJewelry", "grapes", "wine", "honey", "mead", "butter", "spices", "spicedWine", "fur"]
_STAPLE_GOODS = {"wheat", "sausage", "cheese", "saltedFish", "stockfish", "ale"}
_FALLBACK_BASE_PRICES = [0.03, 0.02, 0.15, 0.0, 0.0, 0.10, 0.80, 0.01, 0.025, 0.005, 0.15, 1.50, 0.50, 3.00, 0.06, 2.50, 0.08, 0.20, 0.25, 0.025, 0.15, 0.05, 0.12, 0.08, 0.04, 0.04, 1050.0, 105.0, 15.0, 8.0, 0.04, 0.08, 0.12, 0.10, 0.15, 2.0, 0.20, 4.0]
_FALLBACK_TRADEABLE = {"wheat", "timber", "ironOre", "salt", "wool", "stone", "barley", "clay", "pottery", "furniture", "iron", "tools", "charcoal", "clothes", "pork", "sausage", "bacon", "milk", "cheese", "fish", "saltedFish", "stockfish", "bread", "ale", "gold", "silver", "goldJewelry", "silverJewelry", "grapes", "wine", "honey", "mead", "butter", "spices", "spicedWine", "fur"}

# Module-level references set by init_goods()
GOODS: list[str] = []
GOOD_IDX: dict[str, int] = {}
BASE_PRICES: list[float] = []
UNIT_WEIGHTS: list[float] = []
TRADEABLE: set[str] = set()


def init_goods(data: dict):
    """Initialize goods metadata from dump or fallback to hardcoded values."""
    global GOODS, GOOD_IDX, BASE_PRICES, UNIT_WEIGHTS, TRADEABLE

    goods_meta = data.get("goods")
    if goods_meta:
        # Sort by index to ensure correct ordering
        goods_meta = sorted(goods_meta, key=lambda g: g["index"])
        GOODS = [g["name"] for g in goods_meta]
        GOOD_IDX = {g: i for i, g in enumerate(GOODS)}
        BASE_PRICES = [g["basePrice"] for g in goods_meta]
        UNIT_WEIGHTS = [g.get("unitWeight", 1.0) for g in goods_meta]
        TRADEABLE = {g["name"] for g in goods_meta if g["isTradeable"]}
    else:
        GOODS = list(_FALLBACK_GOODS)
        GOOD_IDX = {g: i for i, g in enumerate(GOODS)}
        BASE_PRICES = list(_FALLBACK_BASE_PRICES)
        UNIT_WEIGHTS = [1.0] * len(GOODS)
        TRADEABLE = set(_FALLBACK_TRADEABLE)


def unit_label(good_idx: int) -> str:
    """Return 'units' for durable goods (UnitWeight > 1), 'kg' otherwise."""
    if good_idx < len(UNIT_WEIGHTS) and UNIT_WEIGHTS[good_idx] > 1.0:
        return "units"
    return "kg"


def is_durable(good_idx: int) -> bool:
    return good_idx < len(UNIT_WEIGHTS) and UNIT_WEIGHTS[good_idx] > 1.0


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
    print(f"  Date:        Day {data.get('day', '?')} (Year {data.get('year', '?')}, Month {data.get('month', '?')}, Day {data.get('dayOfMonth', '?')})")
    print(f"  Ticks:       {data.get('totalTicks', '?')}")
    s = data.get("summary")
    if not s:
        return
    print(f"  Population:  {fmt(s['totalPopulation'], 0)}")
    print(f"  Counties:    {s['totalCounties']}")
    print(f"  Provinces:   {s['totalProvinces']}")
    print(f"  Realms:      {s['totalRealms']}")
    print(f"  Roads:       {s['roadSegments']} road / {s['pathSegments']} path segments")


def print_performance(data: dict):
    if "performance" not in data:
        return
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
    print(f"  {'Good':12s}  {'Production':>12s}  {'Consumption':>12s}  {'Surplus':>12s}  {'Unmet Need':>12s}  {'Cons%':>6s}")
    print(f"  {'-'*12}  {'-'*12}  {'-'*12}  {'-'*12}  {'-'*12}  {'-'*6}")
    for i, good in enumerate(GOODS):
        prod = last["productionByGood"][i]
        cons = last["consumptionByGood"][i]
        unmet = last["unmetNeedByGood"][i]
        surplus = prod - cons
        label = f"{good}" if not is_durable(i) else f"{good}*"
        print(f"  {label:12s}  {fmt(prod):>12s}  {fmt(cons):>12s}  {fmt(surplus):>12s}  {fmt(unmet):>12s}  {pct(cons, prod):>6s}")

    total_prod = last["totalProduction"]
    total_cons = last["totalConsumption"]
    total_unmet = last["totalUnmetNeed"]
    print(f"  {'TOTAL':12s}  {fmt(total_prod):>12s}  {fmt(total_cons):>12s}  {fmt(total_prod - total_cons):>12s}  {fmt(total_unmet):>12s}  {pct(total_cons, total_prod):>6s}")
    # Note about durables
    durables = [g for i, g in enumerate(GOODS) if is_durable(i)]
    if durables:
        print(f"\n  * = units (not kg): {', '.join(durables)}")


def print_stocks(data: dict):
    section("STOCKPILE TRENDS")
    ts = data["economy"]["timeSeries"]
    first, last = ts[0], ts[-1]

    print(f"  Total stock: {fmt(first['totalStock'])} -> {fmt(last['totalStock'])}  ({trend([first['totalStock'], last['totalStock']])})")
    print()
    print(f"  {'Good':12s}  {'Day 2':>10s}  {'Latest':>10s}  {'Change':>10s}")
    print(f"  {'-'*12}  {'-'*10}  {'-'*10}  {'-'*10}")
    for i, good in enumerate(GOODS):
        s0 = first["stockByGood"][i]
        s1 = last["stockByGood"][i]
        label = f"{good}" if not is_durable(i) else f"{good}*"
        print(f"  {label:12s}  {fmt(s0):>10s}  {fmt(s1):>10s}  {trend([s0, s1]):>10s}")

    # County health
    section("COUNTY HEALTH TRENDS")
    print(f"  {'Metric':20s}  {'Day 2':>8s}  {'Latest':>8s}  {'Change':>8s}")
    print(f"  {'-'*20}  {'-'*8}  {'-'*8}  {'-'*8}")
    for key, label in [
        ("surplusCounties", "Surplus counties"),
        ("deficitCounties", "Deficit counties"),
        ("shortfallCounties", "Shortfall counties"),
    ]:
        v0, v1 = first[key], last[key]
        delta = v1 - v0
        sign = "+" if delta >= 0 else ""
        print(f"  {label:20s}  {v0:>8d}  {v1:>8d}  {sign}{delta:>7d}")

    # Find when shortfall bottomed out
    shortfall = [t["shortfallCounties"] for t in ts]
    min_shortfall = min(shortfall)
    min_day = ts[shortfall.index(min_shortfall)]["day"]
    print(f"\n  Shortfall low: {min_shortfall} counties on day {min_day}")


def print_fiscal(data: dict):
    section("FISCAL SYSTEM (latest day)")
    ts = data["economy"]["timeSeries"]
    last = ts[-1]

    # Monetary taxation
    tax_to_prov = last.get("monetaryTaxToProvince", 0)
    tax_to_realm = last.get("monetaryTaxToRealm", 0)
    prov_admin = last.get("provinceAdminCost", 0)
    realm_admin = last.get("realmAdminCost", 0)
    granary_crowns = last.get("granaryRequisitionCrowns", 0)
    relief = last.get("ducalRelief", 0)
    prov_stock = last.get("provincialStockpile", 0)
    royal_stock = last.get("royalStockpile", 0)

    print(f"  Monetary Flows (Crowns/day):")
    print(f"    County → Province prod tax:  {fmt(tax_to_prov)}")
    print(f"    Province → Realm rev share:  {fmt(tax_to_realm)}")
    print(f"    Province admin cost:          {fmt(prov_admin)}")
    print(f"    Realm admin cost:             {fmt(realm_admin)}")
    print(f"    Granary requisition spend:    {fmt(granary_crowns)}")
    print()
    print(f"  Ducal relief (food):           {fmt(relief)}")
    print(f"  Provincial stockpile (granary): {fmt(prov_stock)}")
    print(f"  Royal stockpile (metals):       {fmt(royal_stock)}")

    # Ducal granary fill status
    granary_req = last.get("granaryRequisitionedByGood")
    ducal_relief = last.get("ducalReliefByGood")
    if granary_req or ducal_relief:
        print()
        n = len(GOODS)
        if not granary_req:
            granary_req = [0] * n
        if not ducal_relief:
            ducal_relief = [0] * n
        print(f"  {'Good':12s}  {'Requisitioned':>14s}  {'Relief':>10s}")
        print(f"  {'-'*12}  {'-'*14}  {'-'*10}")
        for i, good in enumerate(GOODS):
            if good not in _STAPLE_GOODS:
                continue
            req = granary_req[i] if i < len(granary_req) else 0
            rel = ducal_relief[i] if i < len(ducal_relief) else 0
            if req > 0 or rel > 0:
                print(f"  {good:12s}  {fmt(req):>14s}  {fmt(rel):>10s}")

    # Fiscal evolution
    section("FISCAL TRENDS (first 5 days vs last 5 days)")
    early = ts[4:9]
    late = ts[-5:]
    if early and late:
        avg_early_tax = sum(t.get("monetaryTaxToProvince", 0) for t in early) / len(early)
        avg_late_tax = sum(t.get("monetaryTaxToProvince", 0) for t in late) / len(late)
        avg_early_relief = sum(t.get("ducalRelief", 0) for t in early) / len(early)
        avg_late_relief = sum(t.get("ducalRelief", 0) for t in late) / len(late)
        print(f"  Avg prod tax (county→prov): {fmt(avg_early_tax):>10s} -> {fmt(avg_late_tax):>10s}")
        print(f"  Avg ducal relief:           {fmt(avg_early_relief):>10s} -> {fmt(avg_late_relief):>10s}")


def print_trade_snapshot(data: dict):
    section("TRADE / FISCAL SNAPSHOT (end of sim)")
    t = data["trade"]
    print(f"  Relief-receiving counties: {t.get('reliefReceivingCounties', 0)}")
    print(f"  Production tax (county→prov): {fmt(t.get('totalMonetaryTaxToProvince', 0))} Crowns/day")
    print(f"  Granary requisition crowns: {fmt(t.get('totalGranaryRequisitionCrowns', 0))} Crowns/day")
    print()

    # Per-good relief + granary
    print(f"  {'Good':12s}  {'Relief':>10s}  {'Granary Req':>12s}")
    print(f"  {'-'*12}  {'-'*10}  {'-'*12}")
    relief_map = t.get("ducalReliefByGood", {})
    granary_map = t.get("granaryRequisitionedByGood", {})
    for good in GOODS:
        dr = relief_map.get(good, 0)
        gr = granary_map.get(good, 0)
        if dr > 0 or gr > 0:
            print(f"  {good:12s}  {fmt(dr):>10s}  {fmt(gr):>12s}")

    # Ducal granary (provincial stockpiles — staples only)
    print()
    prov_stock = t.get("provincialStockpileByGood", {})
    total_granary = sum(prov_stock.get(g, 0) for g in _STAPLE_GOODS)
    print(f"  Ducal Granary (staples): {fmt(total_granary)}")
    for good in GOODS:
        if good not in _STAPLE_GOODS:
            continue
        v = prov_stock.get(good, 0)
        if v > 0:
            print(f"    {good:12s}: {fmt(v)}")

    # Royal stockpile (precious metals only)
    print()
    royal_stock = t.get("royalStockpileByGood", {})
    precious = {"goldOre", "silverOre"}
    total_royal = sum(royal_stock.get(g, 0) for g in precious)
    print(f"  Royal Stockpile (metals): {fmt(total_royal)}")
    for good in precious:
        v = royal_stock.get(good, 0)
        if v > 0:
            print(f"    {good:12s}: {fmt(v)}")

    # Province monetary summary
    print()
    print(f"  Province monetary flows:")
    print(f"    Tax collected (county→prov): {fmt(t.get('totalProvMonetaryTaxCollected', 0))}")
    print(f"    Tax paid (prov→realm):       {fmt(t.get('totalProvMonetaryTaxToRealm', 0))}")
    print(f"    Admin cost:                  {fmt(t.get('totalProvAdminCost', 0))}")
    print(f"    Granary spend:               {fmt(t.get('totalProvGranarySpent', 0))}")

    # Market fees
    market_fees = t.get("marketFeesCollected", 0)
    market_count = t.get("marketCount", 0)
    print()
    print(f"  Market fees collected: {fmt(market_fees)} ({market_count} markets)")

    # Per-realm breakdown
    print()
    print(f"  Realm summary:")
    for r in t.get("realms", []):
        print(f"    Realm {r['id']:2d}: treasury={fmt(r.get('treasury', 0)):>10s}  "
              f"taxCollected={fmt(r.get('monetaryTaxCollected', 0)):>8s}  "
              f"adminCost={fmt(r.get('adminCrownsCost', 0)):>8s}  "
              f"minted={fmt(r.get('crownsMinted', 0)):>8s}")


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
    print(f"    Prod tax (county→prov):           {fmt(last.get('monetaryTaxToProvince', 0))}")
    print(f"    Rev share (prov→realm):           {fmt(last.get('monetaryTaxToRealm', 0))}")
    print(f"    Province admin cost:              {fmt(last.get('provinceAdminCost', 0))}")
    print(f"    Realm admin cost:                 {fmt(last.get('realmAdminCost', 0))}")
    print(f"    Granary requisition:              {fmt(last.get('granaryRequisitionCrowns', 0))}")
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
    transport = last.get("transportCostsPaid", 0)

    print()
    print(f"  Crown flows (latest day):")
    print(f"    Spending (buyers→sellers):  {fmt(spending)} Crowns  (+ 5% toll + 2% market fee + transport)")
    print(f"    Revenue (sellers):           {fmt(revenue)} Crowns")
    print(f"    Tolls paid (buyers→prov):    {fmt(tolls_paid)} Crowns")
    print(f"    Tolls collected (provinces): {fmt(tolls_collected)} Crowns")
    print(f"    Transport costs (destroyed): {fmt(transport)} Crowns")

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


def print_cross_market_trade(data: dict):
    section("CROSS-MARKET TRADE")
    ts = data["economy"]["timeSeries"]
    last = ts[-1]

    # Time series data
    bought = last.get("crossMarketTradeBoughtByGood")
    sold = last.get("crossMarketTradeSoldByGood")
    spending = last.get("crossMarketTradeSpending", 0)
    revenue = last.get("crossMarketTradeRevenue", 0)
    tolls_paid = last.get("crossMarketTollsPaid", 0)
    tariffs_paid = last.get("crossMarketTariffsPaid", 0)
    tariffs_collected = last.get("crossMarketTariffsCollected", 0)

    if not bought and not sold:
        # Try trade section (end-of-sim snapshot)
        t = data.get("trade", {})
        bought_map = t.get("crossMarketTradeBoughtByGood", {})
        sold_map = t.get("crossMarketTradeSoldByGood", {})
        spending = t.get("crossMarketTradeSpending", 0)
        revenue = t.get("crossMarketTradeRevenue", 0)
        tolls_paid = t.get("crossMarketTollsPaid", 0)
        tariffs_paid = t.get("crossMarketTariffsPaid", 0)
        tariffs_collected = t.get("tradeTariffsCollected", 0)

        if not bought_map and not sold_map:
            print("  No cross-market trade data")
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
        early_sp = sum(t.get("crossMarketTradeSpending", 0) for t in early) / len(early)
        late_sp = sum(t.get("crossMarketTradeSpending", 0) for t in late) / len(late)
        early_tar = sum(t.get("crossMarketTariffsPaid", 0) for t in early) / len(early)
        late_tar = sum(t.get("crossMarketTariffsPaid", 0) for t in late) / len(late)
        print()
        print(f"  Trade spending trend:  {fmt(early_sp)}/day -> {fmt(late_sp)}/day")
        print(f"  Tariff revenue trend:  {fmt(early_tar)}/day -> {fmt(late_tar)}/day")


def print_market_fees(data: dict):
    section("MARKET FEES")
    ts = data["economy"]["timeSeries"]
    last = ts[-1]

    fees = last.get("marketFeesCollected", 0)
    market_count = data.get("trade", {}).get("marketCount",
                     data.get("summary", {}).get("marketCount", 0))

    print(f"  Market count:         {market_count}")
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


def print_transport_costs(data: dict):
    section("TRANSPORT COSTS")
    ts = data["economy"]["timeSeries"]
    last = ts[-1]

    daily = last.get("transportCostsPaid", 0)
    print(f"  Daily transport costs (latest):  {fmt(daily)} Crowns (destroyed)")

    # Trend over time
    if len(ts) >= 20:
        early = ts[4:14]
        late = ts[-10:]
        early_tc = sum(t.get("transportCostsPaid", 0) for t in early) / len(early)
        late_tc = sum(t.get("transportCostsPaid", 0) for t in late) / len(late)
        print(f"  Transport cost trend:            {fmt(early_tc)}/day -> {fmt(late_tc)}/day")

    # Cumulative
    total = sum(t.get("transportCostsPaid", 0) for t in ts)
    print(f"  Cumulative (approx):             {fmt(total)} Crowns destroyed")

    # Compare to minting
    total_minted = sum(t.get("crownsMinted", 0) for t in ts)
    if total_minted > 0:
        pct = total / total_minted * 100
        print(f"  As % of total minted:            {pct:.1f}%")


def print_virtual_market(data: dict):
    section("VIRTUAL OVERSEAS MARKET")
    vm = data.get("trade", {}).get("virtualMarket", {})
    if not vm or not vm.get("enabled"):
        print("  Virtual market not enabled")
        return

    traded = vm.get("tradedGoods", [])
    print(f"  Traded goods:        {', '.join(traded)}")
    print(f"  Overseas surcharge:  {vm.get('overseasSurcharge', 0):.3f} Cr/kg")

    # Port cost distribution
    reach = vm.get("reachableCounties", 0)
    print(f"  Reachable counties:  {reach}")
    if reach > 0:
        print(f"  Port cost range:     {fmt(vm.get('minPortCost', 0), 3)} - {fmt(vm.get('maxPortCost', 0), 3)} Cr/kg")
        print(f"  Port cost avg:       {fmt(vm.get('avgPortCost', 0), 3)} Cr/kg")

    # Current VM state
    stock = vm.get("stockByGood", {})
    sell = vm.get("sellPriceByGood", {})
    buy = vm.get("buyPriceByGood", {})
    target = vm.get("targetStockByGood", {})
    replenish = vm.get("replenishRateByGood", {})
    max_stock = vm.get("maxStockByGood", {})

    print()
    print("  VM State (end of run):")
    print(f"    {'Good':8s}  {'Stock':>10s}  {'Target':>10s}  {'Max':>10s}  {'Replenish':>10s}  {'SellPx':>8s}  {'BuyPx':>8s}")
    print(f"    {'-'*8}  {'-'*10}  {'-'*10}  {'-'*10}  {'-'*10}  {'-'*8}  {'-'*8}")
    for g in traded:
        print(f"    {g:8s}  {fmt(stock.get(g, 0)):>10s}  {fmt(target.get(g, 0)):>10s}  {fmt(max_stock.get(g, 0)):>10s}  {fmt(replenish.get(g, 0)):>10s}  {fmt(sell.get(g, 0), 3):>8s}  {fmt(buy.get(g, 0), 3):>8s}")

    # Latest-day trade volumes
    imported = vm.get("importedByGood", {})
    exported = vm.get("exportedByGood", {})
    imp_spend = vm.get("importSpending", 0)
    exp_rev = vm.get("exportRevenue", 0)
    tariffs = vm.get("tariffsPaid", 0)

    print()
    print("  Trade Volumes (latest day):")
    print(f"    {'Good':8s}  {'Imported':>10s}  {'Exported':>10s}  {'Net':>10s}")
    print(f"    {'-'*8}  {'-'*10}  {'-'*10}  {'-'*10}")
    for g in traded:
        imp = imported.get(g, 0)
        exp = exported.get(g, 0)
        net = imp - exp
        sign = "+" if net >= 0 else ""
        print(f"    {g:8s}  {fmt(imp):>10s}  {fmt(exp):>10s}  {sign}{fmt(net):>9s}")

    print()
    print(f"  Import spending:     {fmt(imp_spend)} Crowns")
    print(f"  Export revenue:      {fmt(exp_rev)} Crowns")
    print(f"  Tariffs paid:        {fmt(tariffs)} Crowns")
    net_flow = exp_rev - imp_spend
    sign = "+" if net_flow >= 0 else ""
    print(f"  Net crown flow:      {sign}{fmt(net_flow)} Crowns ({'inflow' if net_flow >= 0 else 'outflow'})")

    # Time series trends
    ts = data.get("economy", {}).get("timeSeries", [])
    if len(ts) >= 20:
        early = ts[4:14]
        late = ts[-10:]
        for g in traded:
            gi = GOOD_IDX.get(g)
            if gi is None:
                continue
            early_imp = sum(t["vmImportedByGood"][gi] for t in early if "vmImportedByGood" in t) / max(1, sum(1 for t in early if "vmImportedByGood" in t))
            late_imp = sum(t["vmImportedByGood"][gi] for t in late if "vmImportedByGood" in t) / max(1, sum(1 for t in late if "vmImportedByGood" in t))
            early_exp = sum(t["vmExportedByGood"][gi] for t in early if "vmExportedByGood" in t) / max(1, sum(1 for t in early if "vmExportedByGood" in t))
            late_exp = sum(t["vmExportedByGood"][gi] for t in late if "vmExportedByGood" in t) / max(1, sum(1 for t in late if "vmExportedByGood" in t))
            early_stock = [t["vmStockByGood"][gi] for t in early if "vmStockByGood" in t]
            late_stock = [t["vmStockByGood"][gi] for t in late if "vmStockByGood" in t]
            print()
            print(f"  {g} trends (early avg -> late avg):")
            print(f"    Imports:  {fmt(early_imp)}/day -> {fmt(late_imp)}/day")
            print(f"    Exports:  {fmt(early_exp)}/day -> {fmt(late_exp)}/day")
            if early_stock and late_stock:
                print(f"    VM stock: {fmt(sum(early_stock)/len(early_stock))} -> {fmt(sum(late_stock)/len(late_stock))}")

        # Crown flow trend
        early_spend = sum(t.get("vmImportSpending", 0) for t in early) / len(early)
        late_spend = sum(t.get("vmImportSpending", 0) for t in late) / len(late)
        early_rev = sum(t.get("vmExportRevenue", 0) for t in early) / len(early)
        late_rev = sum(t.get("vmExportRevenue", 0) for t in late) / len(late)
        print()
        print(f"  Crown flow trend:")
        print(f"    Import spending: {fmt(early_spend)}/day -> {fmt(late_spend)}/day")
        print(f"    Export revenue:  {fmt(early_rev)}/day -> {fmt(late_rev)}/day")


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

    print("  Market Prices (Crowns/unit):")
    print(f"    {'Good':8s}  {'Price':>10s}  {'Base':>8s}  {'Ratio':>8s}  {'Unit':>6s}")
    print(f"    {'-'*8}  {'-'*10}  {'-'*8}  {'-'*8}  {'-'*6}")
    for g in TRADEABLE_IDX:
        p = prices[g]
        bp = BASE_PRICES[g]
        ratio = f"{p/bp:.2f}x" if bp > 0 else "n/a"
        ul = unit_label(g)
        print(f"    {GOODS[g]:8s}  {fmt(p, 2):>10s}  {fmt(bp, 2):>8s}  {ratio:>8s}  {ul:>6s}")

    # Per-market price divergence
    pmp = last.get("perMarketPrices")
    if pmp and len(pmp) > 1:
        market_ids = sorted(pmp.keys(), key=lambda k: int(k))
        # Show divergence: for each tradeable good, show min/max/spread across markets
        print()
        print("  Per-Market Price Divergence:")
        print(f"    {'Good':8s}  {'Global':>8s}  {'Min':>8s}  {'Max':>8s}  {'Spread':>8s}  {'MinMkt':>6s}  {'MaxMkt':>6s}")
        print(f"    {'-'*8}  {'-'*8}  {'-'*8}  {'-'*8}  {'-'*8}  {'-'*6}  {'-'*6}")
        for g in TRADEABLE_IDX:
            bp = BASE_PRICES[g]
            if bp <= 0:
                continue
            gp = prices[g]
            mp_vals = {}
            for mid in market_ids:
                mp_arr = pmp[mid]
                if g < len(mp_arr):
                    mp_vals[mid] = mp_arr[g]
            if not mp_vals:
                continue
            min_mid = min(mp_vals, key=lambda k: mp_vals[k])
            max_mid = max(mp_vals, key=lambda k: mp_vals[k])
            min_p = mp_vals[min_mid]
            max_p = mp_vals[max_mid]
            spread = (max_p - min_p) / bp if bp > 0 else 0
            if spread < 0.01:
                continue  # skip goods with negligible divergence
            print(f"    {GOODS[g]:8s}  {fmt(gp,2):>8s}  {fmt(min_p,2):>8s}  {fmt(max_p,2):>8s}  {spread:>7.0%}  {min_mid:>6s}  {max_mid:>6s}")

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
    avg_needs = last.get("avgBasicSatisfaction", last.get("avgFoodSatisfaction", 0))
    min_needs = last.get("minBasicSatisfaction", last.get("minFoodSatisfaction", 0))
    max_needs = last.get("maxBasicSatisfaction", last.get("maxFoodSatisfaction", 0))
    avg_sat = last.get("avgSatisfaction", avg_needs)
    min_sat = last.get("minSatisfaction", min_needs)
    max_sat = last.get("maxSatisfaction", max_needs)
    distress = last.get("countiesInDistress", 0)
    print(f"\n  Needs Fulfillment (drives birth/death):")
    print(f"    Average:   {avg_needs:.3f}")
    print(f"    Min:       {min_needs:.3f}")
    print(f"    Max:       {max_needs:.3f}")
    print(f"  Satisfaction (drives migration):")
    print(f"    Average:   {avg_sat:.3f}")
    print(f"    Min:       {min_sat:.3f}")
    print(f"    Max:       {max_sat:.3f}")
    print(f"    Distressed counties (<0.5 needs): {distress}")

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
            needs = snap.get("avgBasicSatisfaction", snap.get("avgFoodSatisfaction", 0))
            sat = snap.get("avgSatisfaction", needs)
            dist = snap.get("countiesInDistress", 0)
            print(f"    Day {snap['day']:>4d}: pop={fmt(pop, 0):>8s}  needs={needs:.3f}  sat={sat:.3f}  distress={dist}")

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
            print(f"  {'Type':12s}  {'Count':>6s}  {'Recipe':30s}  {'Throughput':>12s}  {'Workers':>10s}")
            print(f"  {'-'*12}  {'-'*6}  {'-'*30}  {'-'*12}  {'-'*10}")
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
                print(f"  {name:12s}  {info['count']:>6d}  {recipe:30s}  {tp_str:>12s}  {fmt(actual_wk):>10s}")
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

    shortfall = [t["shortfallCounties"] for t in ts]

    # Rate of change over last 30 days
    window = min(30, len(ts))
    recent = ts[-window:]
    staple_rate = (total_staple_unmet(recent[-1]) - total_staple_unmet(recent[0])) / window
    shortfall_rate = (recent[-1]["shortfallCounties"] - recent[0]["shortfallCounties"]) / window
    stock_rate = (recent[-1]["totalStock"] - recent[0]["totalStock"]) / window

    print(f"  Last {window} days (daily averages):")
    print(f"    Staple unmet need change: {staple_rate:+.1f}/day")
    print(f"    Shortfall counties change: {shortfall_rate:+.2f}/day")
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
    print(f"  Shortfall:    {ts[-1]['shortfallCounties']}/{data['economy']['countyCount']} counties")

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


def print_tithes(data: dict):
    trade = data.get("trade", {})
    tithes = trade.get("tithes")
    if not tithes:
        return

    section("RELIGIOUS TITHES")

    print(f"  Faiths:         {tithes.get('faithCount', 0)}")
    print(f"  Parishes:       {tithes.get('parishCount', 0)}")
    print(f"  Dioceses:       {tithes.get('dioceseCount', 0)}")
    print(f"  Archdioceses:   {tithes.get('archdioceseCount', 0)}")
    print()
    print(f"  Monthly tithe collected:  {fmt(tithes.get('totalTithePaid', 0))} Crowns")
    print()
    print(f"  Church Treasury:")
    print(f"    Parishes:      {fmt(tithes.get('totalParishTreasury', 0))} Crowns")
    print(f"    Dioceses:      {fmt(tithes.get('totalDioceseTreasury', 0))} Crowns")
    print(f"    Archdioceses:  {fmt(tithes.get('totalArchdioceseTreasury', 0))} Crowns")
    print(f"    Total:         {fmt(tithes.get('totalChurchTreasury', 0))} Crowns")

    per_faith = tithes.get("perFaith", [])
    if per_faith:
        print()
        print(f"  Per-Faith Breakdown:")
        print(f"    {'Faith':>6s}  {'Parishes':>8s}  {'Dioceses':>8s}  {'Archdio':>8s}  {'Treasury':>12s}")
        print(f"    {'------':>6s}  {'--------':>8s}  {'--------':>8s}  {'--------':>8s}  {'--------':>12s}")
        for f in per_faith:
            rid = f.get("religionId", -1)
            label = f"R{rid}" if rid >= 0 else f"F{f['faithIndex']}"
            print(f"    {label:>6s}  {f.get('parishes', 0):>8d}  {f.get('dioceses', 0):>8d}  {f.get('archdioceses', 0):>8d}  {fmt(f.get('totalTreasury', 0)):>12s}")


def print_economy_v4(data: dict):
    v4 = data.get("economyV4")
    if not v4 or not v4.get("initialized"):
        return

    print("\n══════════════════════════════════════════════════════════")
    print("  ECONOMY V4")
    print("══════════════════════════════════════════════════════════")
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
        print("\n  ── Daily Production / Consumption / Surplus (kg/day) ──")
        all_goods = sorted(set(list(prod.keys()) + list(cons.keys()) + list(surp.keys())))
        print(f"  {'Good':>12s}  {'Production':>12s}  {'Consumption':>12s}  {'Surplus':>12s}  {'Surplus%':>8s}")
        for g in all_goods:
            p = prod.get(g, 0)
            c = cons.get(g, 0)
            s = surp.get(g, 0)
            pct = f"{s / p * 100:.0f}%" if p > 0 else "—"
            print(f"  {g:>12s}  {p:>12,.1f}  {c:>12,.1f}  {s:>12,.1f}  {pct:>8s}")

    # Satisfaction
    sat = v4.get("survivalSatisfaction", {})
    if sat:
        print(f"\n  ── Survival Satisfaction (lower commoners) ──")
        print(f"  Mean: {sat.get('mean', 0):.3f}  "
              f"Min: {sat.get('min', 0):.3f}  "
              f"Max: {sat.get('max', 0):.3f}  "
              f"Counties: {sat.get('counties', 0)}")

    # Noble satisfaction
    nsat = v4.get("nobleSatisfaction", {})
    if nsat and nsat.get("counties", 0) > 0:
        print(f"\n  ── Noble Satisfaction ──")
        print(f"  Upper noble mean: {nsat.get('upperNobleMean', 0):.3f}  "
              f"Lower noble mean: {nsat.get('lowerNobleMean', 0):.3f}  "
              f"Counties: {nsat.get('counties', 0)}")

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

    # Upper commoner economy
    uce = v4.get("upperCommonerEconomy", {})
    if uce:
        print(f"\n  ── Upper Commoner Economy ──")
        print(f"  Total coin (M contrib):  {uce.get('totalCoin', 0):>12,.2f}")
        print(f"  Income (facility sales): {uce.get('totalIncome', 0):>12,.2f}")
        print(f"  Spend (goods):           {uce.get('totalSpend', 0):>12,.2f}")
        print(f"  Tax paid:                {uce.get('taxRevenue', 0):>12,.2f}")
        print(f"  Tithe paid:              {uce.get('titheRevenue', 0):>12,.2f}")
        print(f"  Satisfaction mean:       {uce.get('satisfactionMean', 0):>12.3f}")

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
        print(f"  Upper clergy sat mean:   {cle.get('upperClergySatMean', 0):>12.3f}")
        print(f"  Lower clergy sat mean:   {cle.get('lowerClergySatMean', 0):>12.3f}")

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
                pct = p / total * 100 if total > 0 else 0
                print(f"  {label:>16s}  {p:>12,.0f}  {pct:>5.1f}%")

        sb = pd.get("satisfactionBreakdown", {})
        if sb:
            print(f"\n  ── Satisfaction Breakdown (component means) ──")
            print(f"  Survival:   {sb.get('survivalMean', 0):.3f}  (weight 0.40)")
            print(f"  Religion:   {sb.get('religionMean', 0):.3f}  (weight 0.25)")
            print(f"  Stability:  {sb.get('stabilityPlaceholder', 1.0):.3f}  (weight 0.20, placeholder)")
            print(f"  Economic:   {sb.get('economicMean', 0):.3f}  (weight 0.10)")
            print(f"  Governance: {sb.get('governancePlaceholder', 0.7):.3f}  (weight 0.05, placeholder)")

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
        # Sort by satisfaction for display
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
            # Group by good, show top flows
            by_good = {}
            for tf in trade_flows:
                g = tf.get("good", "?")
                if g not in by_good:
                    by_good[g] = []
                by_good[g].append(tf)

            print(f"\n  {'Good':>12s}  {'From→To':>10s}  {'Posted':>10s}  {'Filled':>10s}  {'Value':>10s}")
            for g in sorted(by_good.keys()):
                flows = sorted(by_good[g], key=lambda x: -x.get("filled", 0))
                for tf in flows[:5]:  # top 5 per good
                    route = f"{tf.get('from', '?')}→{tf.get('to', '?')}"
                    print(f"  {g:>12s}  {route:>10s}  "
                          f"{tf.get('posted', 0):>10,.1f}  "
                          f"{tf.get('filled', 0):>10,.1f}  "
                          f"{tf.get('value', 0):>10,.2f}")
    elif cf:
        tariff_from_cf = cf.get("totalTariffRevenue", 0)
        if tariff_from_cf > 0:
            print(f"\n  ── Cross-Market Trade ──")
            print(f"  Tariff revenue: {tariff_from_cf:,.2f} Cr  (no trade flows this tick)")

    # Markets
    markets = v4.get("markets", [])
    if markets:
        print(f"\n  ── Markets ({len(markets)}) ──")
        print(f"  {'ID':>4s}  {'Realm':>6s}  {'Counties':>8s}  {'PriceLevel':>10s}  {'M':>10s}  {'Q':>10s}")
        for m in markets:
            print(f"  {m['id']:>4d}  {m.get('hubRealmId', 0):>6d}  "
                  f"{m.get('counties', 0):>8d}  {m.get('priceLevel', 0):>10.2f}  "
                  f"{m.get('totalM', 0):>10.2f}  {m.get('totalQ', 0):>10.0f}")

        # Clearing prices for first market (sample)
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


def main():
    path = sys.argv[1] if len(sys.argv) > 1 else None
    data = load(path)
    init_goods(data)

    print_header(data)
    print_performance(data)

    # V4 economy (if present)
    print_economy_v4(data)

    # V3 systems (skip if v4-only dump)
    if "economy" in data:
        print_economy(data)
        print_stocks(data)
        print_fiscal(data)
        print_trade_snapshot(data)
        print_treasury(data)
        print_tithes(data)
        print_intra_province_trade(data)
        print_cross_province_trade(data)
        print_cross_market_trade(data)
        print_market_fees(data)
        print_transport_costs(data)
        print_virtual_market(data)
        print_inter_realm_trade(data)
        print_roads(data)
        print_facilities(data)
        print_production_chains(data)
        print_population(data)
        print_convergence(data)
    print()


if __name__ == "__main__":
    main()
