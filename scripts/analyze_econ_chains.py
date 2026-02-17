#!/usr/bin/env python3
import argparse
import json
import math
import statistics
from dataclasses import dataclass, field
from pathlib import Path
from typing import Dict, Iterable, List, Optional, Sequence, Tuple


@dataclass
class GoodMetrics:
    supply: float = 0.0
    demand: float = 0.0
    volume: float = 0.0
    prices: List[float] = field(default_factory=list)
    active_markets: int = 0
    short_markets: int = 0
    excess_markets: int = 0

    @property
    def demand_over_supply(self) -> float:
        if self.supply <= 0:
            return math.inf if self.demand > 0 else 0.0
        return self.demand / self.supply

    @property
    def supply_over_demand(self) -> float:
        if self.demand <= 0:
            return math.inf if self.supply > 0 else 0.0
        return self.supply / self.demand

    @property
    def fill_ratio(self) -> float:
        if self.demand <= 0:
            return 1.0
        return self.volume / self.demand

    @property
    def short_fraction(self) -> float:
        if self.active_markets <= 0:
            return 0.0
        return self.short_markets / self.active_markets

    @property
    def excess_fraction(self) -> float:
        if self.active_markets <= 0:
            return 0.0
        return self.excess_markets / self.active_markets

    @property
    def price_cv(self) -> float:
        if not self.prices:
            return 0.0
        mean_price = statistics.mean(self.prices)
        if mean_price <= 0:
            return 0.0
        return statistics.pstdev(self.prices) / mean_price


@dataclass
class FacilityMetrics:
    count: int = 0
    active_count: int = 0
    workers: float = 0.0
    labor_required: float = 0.0
    efficiency_sum: float = 0.0
    loss_days_sum: float = 0.0
    wage_debt_sum: float = 0.0

    @property
    def active_ratio(self) -> float:
        return (self.active_count / self.count) if self.count > 0 else 0.0

    @property
    def labor_fill(self) -> float:
        return (self.workers / self.labor_required) if self.labor_required > 0 else 0.0

    @property
    def avg_efficiency(self) -> float:
        return (self.efficiency_sum / self.count) if self.count > 0 else 0.0


@dataclass
class ChainStage:
    label: str
    goods: Sequence[str]
    facilities: Sequence[str]


@dataclass
class ChainDef:
    name: str
    stages: Sequence[ChainStage]

    @property
    def final_good(self) -> str:
        return self.stages[-1].goods[0]

    @property
    def raw_goods(self) -> Sequence[str]:
        return self.stages[0].goods


CHAIN_DEFS: List[ChainDef] = [
    ChainDef(
        name="Food",
        stages=[
            ChainStage("Raw grain", ["wheat", "rye", "barley", "rice_grain"], ["farm", "rye_farm", "barley_farm", "rice_paddy"]),
            ChainStage("Flour", ["flour"], ["mill", "rye_mill", "barley_mill"]),
            ChainStage("Bread", ["bread"], ["bakery", "rice_mill"]),
        ],
    ),
    ChainDef(
        name="Beer",
        stages=[
            ChainStage("Barley", ["barley"], ["barley_farm"]),
            ChainStage("Malt", ["malt"], ["malthouse"]),
            ChainStage("Beer", ["beer"], ["brewery"]),
        ],
    ),
    ChainDef(
        name="Tools",
        stages=[
            ChainStage("Iron ore", ["iron_ore"], ["mine"]),
            ChainStage("Iron", ["iron"], ["smelter"]),
            ChainStage("Tools", ["tools"], ["smithy"]),
        ],
    ),
    ChainDef(
        name="Jewelry",
        stages=[
            ChainStage("Gold ore", ["gold_ore"], ["gold_mine"]),
            ChainStage("Gold", ["gold"], ["refinery"]),
            ChainStage("Jewelry", ["jewelry"], ["jeweler"]),
        ],
    ),
    ChainDef(
        name="Furniture",
        stages=[
            ChainStage("Timber", ["timber"], ["lumber_camp"]),
            ChainStage("Lumber", ["lumber"], ["sawmill"]),
            ChainStage("Furniture", ["furniture"], ["workshop"]),
        ],
    ),
    ChainDef(
        name="Clothing",
        stages=[
            ChainStage("Sheep", ["sheep"], ["ranch"]),
            ChainStage("Wool", ["wool"], ["shearing_shed"]),
            ChainStage("Cloth", ["cloth"], ["spinning_mill"]),
            ChainStage("Clothes", ["clothes"], ["tailor"]),
        ],
    ),
    ChainDef(
        name="Dairy",
        stages=[
            ChainStage("Goats", ["goats"], ["goat_farm"]),
            ChainStage("Milk", ["milk"], ["dairy"]),
            ChainStage("Cheese", ["cheese"], ["creamery"]),
        ],
    ),
    ChainDef(
        name="Leatherwork",
        stages=[
            ChainStage("Hides", ["hides"], ["hide_farm"]),
            ChainStage("Leather", ["leather"], ["tannery"]),
            ChainStage("Shoes", ["shoes"], ["cobbler"]),
        ],
    ),
    ChainDef(
        name="Copperwork",
        stages=[
            ChainStage("Copper ore", ["copper_ore"], ["copper_mine"]),
            ChainStage("Copper", ["copper"], ["copper_smelter"]),
            ChainStage("Cookware", ["cookware"], ["coppersmith"]),
        ],
    ),
    ChainDef(
        name="Salt",
        stages=[
            ChainStage("Raw salt", ["raw_salt"], ["salt_works"]),
            ChainStage("Salt", ["salt"], ["salt_warehouse"]),
        ],
    ),
    ChainDef(
        name="Sugar",
        stages=[
            ChainStage("Sugarcane", ["sugarcane"], ["sugar_plantation"]),
            ChainStage("Cane juice", ["cane_juice"], ["sugar_press"]),
            ChainStage("Sugar", ["sugar"], ["sugar_refinery"]),
        ],
    ),
    ChainDef(
        name="Spices",
        stages=[
            ChainStage("Spice plants", ["spice_plants"], ["spice_farm"]),
            ChainStage("Spices", ["spices"], ["spice_house"]),
        ],
    ),
    ChainDef(
        name="Dyed clothes",
        stages=[
            ChainStage("Dye plants", ["dye_plants"], ["dye_farm"]),
            ChainStage("Dye", ["dye"], ["dye_works"]),
            ChainStage("Cloth", ["cloth"], ["spinning_mill"]),
            ChainStage("Dyed clothes", ["dyed_clothes"], ["dyer"]),
        ],
    ),
]


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description=(
            "Analyze econ dump for system-wide signals vs chain-level bottlenecks. "
            "If no dump path is provided, uses newest file from unity/econ_debug_output*.json "
            "and unity/debug/econ/**/*.json."
        )
    )
    parser.add_argument("dump", nargs="?", help="Path to dump JSON")
    parser.add_argument("--top", type=int, default=10, help="Top rows to show per systemic list (default: 10)")
    return parser.parse_args()


def discover_latest_dump(cwd: Path) -> Optional[Path]:
    candidates: List[Path] = []
    patterns = [
        "unity/econ_debug_output*.json",
        "unity/debug/econ/**/*.json",
    ]
    for pattern in patterns:
        for path in cwd.glob(pattern):
            if path.is_file():
                candidates.append(path)
    if not candidates:
        return None
    return max(candidates, key=lambda p: p.stat().st_mtime)


def safe_float(value: object) -> float:
    if value is None:
        return 0.0
    try:
        return float(value)
    except (TypeError, ValueError):
        return 0.0


def aggregate_goods(markets: Sequence[dict]) -> Dict[str, GoodMetrics]:
    goods: Dict[str, GoodMetrics] = {}
    for market in markets:
        for good_id, payload in (market.get("goods") or {}).items():
            gm = goods.setdefault(good_id, GoodMetrics())
            supply = safe_float(payload.get("supplyOffered", payload.get("supply", 0.0)))
            demand = safe_float(payload.get("demand", 0.0))
            volume = safe_float(payload.get("volume", 0.0))
            price = safe_float(payload.get("price", 0.0))

            gm.supply += supply
            gm.demand += demand
            gm.volume += volume
            gm.prices.append(price)

            if supply > 0 or demand > 0:
                gm.active_markets += 1
                if demand > supply * 1.05:
                    gm.short_markets += 1
                elif supply > demand * 1.05:
                    gm.excess_markets += 1
    return goods


def aggregate_facilities(counties: Sequence[dict]) -> Dict[str, FacilityMetrics]:
    facilities: Dict[str, FacilityMetrics] = {}
    for county in counties:
        for entry in county.get("facilities") or []:
            facility_type = entry.get("type")
            if not facility_type:
                continue
            fm = facilities.setdefault(facility_type, FacilityMetrics())
            fm.count += 1
            fm.active_count += 1 if bool(entry.get("active")) else 0
            fm.workers += safe_float(entry.get("workers", 0.0))
            fm.labor_required += safe_float(entry.get("laborRequired", 0.0))
            fm.efficiency_sum += safe_float(entry.get("efficiency", 0.0))
            fm.loss_days_sum += safe_float(entry.get("consecutiveLossDays", 0.0))
            fm.wage_debt_sum += safe_float(entry.get("wageDebtDays", 0.0))
    return facilities


def stage_totals(goods: Dict[str, GoodMetrics], stage_goods: Sequence[str]) -> Tuple[float, float, float]:
    supply = 0.0
    demand = 0.0
    volume = 0.0
    for good_id in stage_goods:
        gm = goods.get(good_id)
        if gm is None:
            continue
        supply += gm.supply
        demand += gm.demand
        volume += gm.volume
    return supply, demand, volume


def weighted_facility_health(
    facilities: Dict[str, FacilityMetrics], facility_ids: Iterable[str]
) -> Tuple[float, float, int]:
    total_count = 0
    active_sum = 0.0
    fill_sum = 0.0
    for facility_id in facility_ids:
        fm = facilities.get(facility_id)
        if fm is None or fm.count <= 0:
            continue
        total_count += fm.count
        active_sum += fm.active_ratio * fm.count
        fill_sum += fm.labor_fill * fm.count
    if total_count <= 0:
        return 0.0, 0.0, 0
    return active_sum / total_count, fill_sum / total_count, total_count


def classify_chain(
    chain: ChainDef,
    goods: Dict[str, GoodMetrics],
    facilities: Dict[str, FacilityMetrics],
) -> Tuple[str, str]:
    final_good = chain.final_good
    final = goods.get(final_good, GoodMetrics())
    raw_supply, raw_demand, _ = stage_totals(goods, chain.raw_goods)

    processing_facilities: List[str] = []
    for stage in chain.stages[1:]:
        processing_facilities.extend(stage.facilities)
    processing_active, processing_fill, processing_count = weighted_facility_health(
        facilities, processing_facilities
    )

    raw_demand_over_supply = (raw_demand / raw_supply) if raw_supply > 0 else math.inf
    raw_abundant = raw_supply > 0 and raw_demand_over_supply < 0.2
    processing_weak = processing_count > 0 and (processing_active < 0.4 or processing_fill < 0.35)

    if final.demand <= 0 and final.supply <= 0:
        return ("NO_SIGNAL", "No demand and no supply in this dump.")

    if final.supply <= 0 and final.demand > 0:
        if raw_supply <= 0:
            return (
                "SYSTEMIC_UPSTREAM_GAP",
                "Demand exists but zero supply and zero upstream raw supply on-map; likely import/exogenous availability gap.",
            )
        return (
            "CHAIN_CONVERSION_BOTTLENECK",
            "Demand exists with zero final supply despite upstream presence; conversion stage is effectively offline.",
        )

    if final.short_fraction >= 0.7 and final.demand_over_supply >= 2.0:
        if raw_abundant and processing_weak:
            return (
                "CHAIN_PROCESSING_BOTTLENECK",
                f"Final good is short in most markets while raw inputs are abundant; processing health is weak (active={processing_active:.0%}, labor_fill={processing_fill:.0%}).",
            )
        return (
            "SYSTEMIC_SHORTAGE",
            "Final good shortage appears in most active markets, indicating broad under-supply not isolated to one market.",
        )

    if final.excess_fraction >= 0.7 and final.supply_over_demand >= 2.0:
        return (
            "SYSTEMIC_OVERSUPPLY",
            "Final good is in excess across most active markets; demand pull is weak versus available supply.",
        )

    if (
        0.3 <= final.short_fraction <= 0.7
        and 0.3 <= final.excess_fraction <= 0.7
        and final.price_cv >= 0.6
    ):
        return (
            "LOCALIZED_DISTRIBUTION_IMBALANCE",
            "Mixed shortage/excess by market with high cross-market price dispersion suggests routing/distribution fragmentation.",
        )

    return ("MIXED", "No single dominant failure mode from this snapshot.")


def fmt_ratio(value: float) -> str:
    if math.isinf(value):
        return "inf"
    return f"{value:.2f}"


def print_global_section(summary: dict, goods: Dict[str, GoodMetrics]) -> None:
    total_supply = safe_float(summary.get("totalMarketSupply", 0.0))
    total_demand = safe_float(summary.get("totalMarketDemand", 0.0))
    total_volume = safe_float(summary.get("totalMarketVolume", 0.0))
    pending = safe_float(summary.get("totalPendingOrders", 0.0))
    lots = safe_float(summary.get("totalConsignmentLots", 0.0))

    fill = (total_volume / total_demand) if total_demand > 0 else 1.0
    demand_over_supply = (total_demand / total_supply) if total_supply > 0 else math.inf

    print("== Global System Signals ==")
    print(
        "supply={:.1f} demand={:.1f} volume={:.1f} fill={:.3f} demand/supply={} pendingOrders={} lots={}".format(
            total_supply,
            total_demand,
            total_volume,
            fill,
            fmt_ratio(demand_over_supply),
            int(pending),
            int(lots),
        )
    )
    print()


def print_good_signal_lists(goods: Dict[str, GoodMetrics], top_n: int) -> None:
    candidates = [
        (good_id, gm)
        for good_id, gm in goods.items()
        if gm.active_markets > 0 and (gm.demand > 0 or gm.supply > 0)
    ]
    systemic_short = [
        (good_id, gm)
        for good_id, gm in candidates
        if gm.demand >= 100 and gm.short_fraction >= 0.7
    ]
    systemic_excess = [
        (good_id, gm)
        for good_id, gm in candidates
        if gm.supply >= 100 and gm.excess_fraction >= 0.7
    ]
    localized = [
        (good_id, gm)
        for good_id, gm in candidates
        if gm.demand >= 100
        and 0.3 <= gm.short_fraction <= 0.7
        and 0.3 <= gm.excess_fraction <= 0.7
        and gm.price_cv >= 0.6
    ]

    systemic_short.sort(key=lambda item: item[1].demand, reverse=True)
    systemic_excess.sort(key=lambda item: item[1].supply, reverse=True)
    localized.sort(key=lambda item: item[1].price_cv, reverse=True)

    print("== Broad Shortage Goods (Systemic) ==")
    print("good              demand   supply    d/s   fill  short% excess% price_cv")
    for good_id, gm in systemic_short[:top_n]:
        print(
            f"{good_id:16} {gm.demand:8.1f} {gm.supply:8.1f} {fmt_ratio(gm.demand_over_supply):>5} {gm.fill_ratio:6.2f} "
            f"{gm.short_fraction:6.0%} {gm.excess_fraction:6.0%} {gm.price_cv:8.2f}"
        )
    print()

    print("== Broad Oversupply Goods (Systemic) ==")
    print("good              supply   demand    s/d   fill  short% excess% price_cv")
    for good_id, gm in systemic_excess[:top_n]:
        print(
            f"{good_id:16} {gm.supply:8.1f} {gm.demand:8.1f} {fmt_ratio(gm.supply_over_demand):>5} {gm.fill_ratio:6.2f} "
            f"{gm.short_fraction:6.0%} {gm.excess_fraction:6.0%} {gm.price_cv:8.2f}"
        )
    print()

    print("== Localized / Mixed Goods ==")
    print("good              demand   supply    d/s   fill  short% excess% price_cv")
    for good_id, gm in localized[:top_n]:
        print(
            f"{good_id:16} {gm.demand:8.1f} {gm.supply:8.1f} {fmt_ratio(gm.demand_over_supply):>5} {gm.fill_ratio:6.2f} "
            f"{gm.short_fraction:6.0%} {gm.excess_fraction:6.0%} {gm.price_cv:8.2f}"
        )
    print()


def print_offmap_section(markets: Sequence[dict]) -> None:
    offmap = [m for m in markets if str(m.get("type", "")).lower() == "offmap"]
    if not offmap:
        print("== Off-map Markets ==")
        print("none")
        print()
        return

    print("== Off-map Markets ==")
    for market in offmap:
        nonzero_goods = 0
        for payload in (market.get("goods") or {}).values():
            supply = safe_float(payload.get("supplyOffered", payload.get("supply", 0.0)))
            demand = safe_float(payload.get("demand", 0.0))
            volume = safe_float(payload.get("volume", 0.0))
            if supply != 0 or demand != 0 or volume != 0:
                nonzero_goods += 1
        print(
            f"id={market.get('id')} name={market.get('name')} pending={market.get('pendingOrders', 0)} "
            f"lots={market.get('consignmentLots', 0)} nonzero_goods={nonzero_goods}"
        )
    print()


def print_chain_section(goods: Dict[str, GoodMetrics], facilities: Dict[str, FacilityMetrics]) -> None:
    print("== Chain Diagnoses ==")
    print("chain          final_good       demand   supply    d/s   fill  short% excess% class")
    for chain in CHAIN_DEFS:
        final_good = chain.final_good
        gm = goods.get(final_good, GoodMetrics())
        chain_class, _ = classify_chain(chain, goods, facilities)
        print(
            f"{chain.name:14} {final_good:16} {gm.demand:8.1f} {gm.supply:8.1f} {fmt_ratio(gm.demand_over_supply):>5} "
            f"{gm.fill_ratio:6.2f} {gm.short_fraction:6.0%} {gm.excess_fraction:6.0%} {chain_class}"
        )
    print()

    print("== Chain Details ==")
    for chain in CHAIN_DEFS:
        chain_class, rationale = classify_chain(chain, goods, facilities)
        final = goods.get(chain.final_good, GoodMetrics())
        print(f"- {chain.name}: {chain_class}")
        print(f"  final={chain.final_good} demand={final.demand:.1f} supply={final.supply:.1f} fill={final.fill_ratio:.2f}")
        for stage in chain.stages:
            supply, demand, volume = stage_totals(goods, stage.goods)
            facility_active, facility_fill, facility_count = weighted_facility_health(facilities, stage.facilities)
            goods_label = "+".join(stage.goods)
            print(
                f"  stage={stage.label} goods={goods_label} supply={supply:.1f} demand={demand:.1f} volume={volume:.1f} "
                f"facilities={facility_count} active={facility_active:.0%} labor_fill={facility_fill:.0%}"
            )
        print(f"  rationale={rationale}")
    print()


def main() -> int:
    args = parse_args()
    cwd = Path.cwd()

    if args.dump:
        dump_path = Path(args.dump)
        if not dump_path.is_absolute():
            dump_path = cwd / dump_path
    else:
        latest = discover_latest_dump(cwd)
        if latest is None:
            print("error: no dump files found. pass a path explicitly.")
            return 1
        dump_path = latest

    if not dump_path.exists():
        print(f"error: dump file not found: {dump_path}")
        return 1

    with dump_path.open("r", encoding="utf-8") as fh:
        payload = json.load(fh)

    day = payload.get("day", "NA")
    year = payload.get("year", "NA")
    month = payload.get("month", "NA")
    day_of_month = payload.get("dayOfMonth", "NA")
    summary = payload.get("summary") or {}
    seed = summary.get("economySeed", "NA")
    markets = payload.get("markets") or []
    counties = payload.get("counties") or []

    print("== Econ Chain Analysis ==")
    print(f"dump={dump_path}")
    print(f"sim_day={day} calendar={year}-{month}-{day_of_month} economySeed={seed}")
    print(f"counties={len(counties)} markets={len(markets)}")
    print()

    goods = aggregate_goods(markets)
    facilities = aggregate_facilities(counties)

    print_global_section(summary, goods)
    print_offmap_section(markets)
    print_good_signal_lists(goods, top_n=max(1, args.top))
    print_chain_section(goods, facilities)

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
