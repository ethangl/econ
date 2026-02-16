#!/usr/bin/env bash
set -euo pipefail

usage() {
  cat <<'EOF'
Usage:
  scripts/compare_econ_dumps.sh <benchmark_dump.json> <candidate_dump.json> [top_n]

Compares two EconDebugBridge dump files and prints:
  - header identity checks (day/seed/size)
  - summary metric deltas
  - total pending-order / consignment-lot deltas
  - per-market pending/lot deltas
  - largest market-good drifts (price/supply/demand/volume)

Requires: jq, awk, join, sort
EOF
}

if [[ "${1:-}" == "-h" || "${1:-}" == "--help" ]]; then
  usage
  exit 0
fi

if [[ $# -lt 2 || $# -gt 3 ]]; then
  usage
  exit 1
fi

BENCH_FILE="$1"
NEW_FILE="$2"
TOP_N="${3:-10}"

if [[ ! -f "$BENCH_FILE" ]]; then
  echo "error: benchmark file not found: $BENCH_FILE" >&2
  exit 1
fi

if [[ ! -f "$NEW_FILE" ]]; then
  echo "error: candidate file not found: $NEW_FILE" >&2
  exit 1
fi

if ! command -v jq >/dev/null 2>&1; then
  echo "error: jq is required but not installed." >&2
  exit 1
fi

TMP_DIR="$(mktemp -d)"
trap 'rm -rf "$TMP_DIR"' EXIT

echo "== Economy Dump Comparison =="
echo "Benchmark: $BENCH_FILE"
echo "Candidate: $NEW_FILE"
echo

echo "== Header Check =="
echo "benchmark: $(jq -r '"day=\(.day), economySeed=\(.summary.economySeed), counties=\(.summary.totalCounties), markets=\(.summary.totalMarkets), facilities=\(.summary.totalFacilities)"' "$BENCH_FILE")"
echo "candidate: $(jq -r '"day=\(.day), economySeed=\(.summary.economySeed), counties=\(.summary.totalCounties), markets=\(.summary.totalMarkets), facilities=\(.summary.totalFacilities)"' "$NEW_FILE")"
echo

jq -r '.summary | to_entries[] | [.key, (.value | tostring)] | @tsv' "$BENCH_FILE" | sort > "$TMP_DIR/bench_summary.tsv"
jq -r '.summary | to_entries[] | [.key, (.value | tostring)] | @tsv' "$NEW_FILE" | sort > "$TMP_DIR/new_summary.tsv"

echo "== Summary Deltas =="
join -t $'\t' -a1 -a2 -e NA -o 0,1.2,2.2 \
  "$TMP_DIR/bench_summary.tsv" "$TMP_DIR/new_summary.tsv" \
  | awk -F'\t' '
function isnum(x) { return x ~ /^-?([0-9]+([.][0-9]*)?|[.][0-9]+)([eE][+-]?[0-9]+)?$/ }
function fmt(x) { return sprintf("%.10g", x) }
BEGIN {
  printf "%-30s %-14s %-14s %-14s %-12s\n", "metric", "benchmark", "candidate", "delta", "delta_pct";
}
{
  metric=$1; b=$2; n=$3; d="NA"; p="NA";
  if (isnum(b) && isnum(n)) {
    d=n-b;
    if (b != 0) p=(d/b)*100;
    d=fmt(d);
    if (p != "NA") p=fmt(p);
  }
  printf "%-30s %-14s %-14s %-14s %-12s\n", metric, b, n, d, p;
}'
echo

BENCH_PENDING="$(jq -r '([.markets[].pendingOrders] | add) // 0' "$BENCH_FILE")"
NEW_PENDING="$(jq -r '([.markets[].pendingOrders] | add) // 0' "$NEW_FILE")"
BENCH_LOTS="$(jq -r '([.markets[].consignmentLots] | add) // 0' "$BENCH_FILE")"
NEW_LOTS="$(jq -r '([.markets[].consignmentLots] | add) // 0' "$NEW_FILE")"

echo "== Global Book Cardinality =="
awk -v bp="$BENCH_PENDING" -v np="$NEW_PENDING" -v bl="$BENCH_LOTS" -v nl="$NEW_LOTS" '
function pct(d,b) { if (b == 0) return "NA"; return sprintf("%.8g", (d/b)*100); }
BEGIN {
  pd=np-bp; ld=nl-bl;
  printf "pendingOrders: benchmark=%s candidate=%s delta=%s delta_pct=%s\n", bp, np, pd, pct(pd,bp);
  printf "consignmentLots: benchmark=%s candidate=%s delta=%s delta_pct=%s\n", bl, nl, ld, pct(ld,bl);
}'
echo

jq -r '.markets[] | [.id, .pendingOrders, .consignmentLots] | @tsv' "$BENCH_FILE" | sort -n > "$TMP_DIR/bench_markets.tsv"
jq -r '.markets[] | [.id, .pendingOrders, .consignmentLots] | @tsv' "$NEW_FILE" | sort -n > "$TMP_DIR/new_markets.tsv"

echo "== Per-Market Book Deltas =="
join -t $'\t' -a1 -a2 -e NA -o 0,1.2,2.2,1.3,2.3 \
  "$TMP_DIR/bench_markets.tsv" "$TMP_DIR/new_markets.tsv" \
  | awk -F'\t' '
function isnum(x) { return x ~ /^-?[0-9]+([.][0-9]+)?([eE][+-]?[0-9]+)?$/ }
BEGIN {
  printf "%-8s %-13s %-13s %-13s %-13s %-13s %-13s\n",
    "marketId", "benchPending", "newPending", "deltaPending", "benchLots", "newLots", "deltaLots";
}
{
  dp="NA"; dl="NA";
  if (isnum($2) && isnum($3)) dp=$3-$2;
  if (isnum($4) && isnum($5)) dl=$5-$4;
  printf "%-8s %-13s %-13s %-13s %-13s %-13s %-13s\n",
    $1, $2, $3, dp, $4, $5, dl;
}'
echo

jq -r '.markets[] | .id as $mid | .goods | to_entries[] | [($mid|tostring) + ":" + .key, (.value.price|tostring), (.value.supplyOffered|tostring), (.value.demand|tostring), (.value.volume|tostring)] | @tsv' "$BENCH_FILE" | sort > "$TMP_DIR/bench_goods.tsv"
jq -r '.markets[] | .id as $mid | .goods | to_entries[] | [($mid|tostring) + ":" + .key, (.value.price|tostring), (.value.supplyOffered|tostring), (.value.demand|tostring), (.value.volume|tostring)] | @tsv' "$NEW_FILE" | sort > "$TMP_DIR/new_goods.tsv"

join -t $'\t' -a1 -a2 -e NA -o 0,1.2,2.2,1.3,2.3,1.4,2.4,1.5,2.5 \
  "$TMP_DIR/bench_goods.tsv" "$TMP_DIR/new_goods.tsv" > "$TMP_DIR/joined_goods.tsv"

echo "== Largest Market-Good Drift =="
awk -F'\t' '
function abs(x) { return x < 0 ? -x : x }
function isnum(x) { return x ~ /^-?([0-9]+([.][0-9]*)?|[.][0-9]+)([eE][+-]?[0-9]+)?$/ }
BEGIN {
  maxp=0; maxs=0; maxd=0; maxv=0; kp=""; ks=""; kd=""; kv="";
}
{
  key=$1;
  if (isnum($2) && isnum($3)) {
    dp=$3-$2; ap=abs(dp); if (ap > maxp) { maxp=ap; kp=key; }
  }
  if (isnum($4) && isnum($5)) {
    ds=$5-$4; as=abs(ds); if (as > maxs) { maxs=as; ks=key; }
  }
  if (isnum($6) && isnum($7)) {
    dd=$7-$6; ad=abs(dd); if (ad > maxd) { maxd=ad; kd=key; }
  }
  if (isnum($8) && isnum($9)) {
    dv=$9-$8; av=abs(dv); if (av > maxv) { maxv=av; kv=key; }
  }
}
END {
  printf "max_abs_price_delta=%g (%s)\n", maxp, kp;
  printf "max_abs_supplyOffered_delta=%g (%s)\n", maxs, ks;
  printf "max_abs_demand_delta=%g (%s)\n", maxd, kd;
  printf "max_abs_volume_delta=%g (%s)\n", maxv, kv;
}' "$TMP_DIR/joined_goods.tsv"
echo

echo "== Top ${TOP_N} Price Drift (abs) =="
awk -F'\t' '
function abs(x) { return x < 0 ? -x : x }
function isnum(x) { return x ~ /^-?([0-9]+([.][0-9]*)?|[.][0-9]+)([eE][+-]?[0-9]+)?$/ }
isnum($2) && isnum($3) {
  dp=$3-$2;
  printf "%.12f\t%s\t%s\t%s\t%.10g\n", abs(dp), $1, $2, $3, dp;
}' "$TMP_DIR/joined_goods.tsv" \
  | sort -nr -k1,1 \
  | head -n "$TOP_N" \
  | awk -F'\t' 'BEGIN { printf "%-14s %-20s %-14s %-14s %-14s\n", "absDelta", "marketId:good", "benchmark", "candidate", "delta"; } { printf "%-14s %-20s %-14s %-14s %-14s\n", $1, $2, $3, $4, $5; }'
echo

echo "== Top ${TOP_N} Volume Drift (abs) =="
awk -F'\t' '
function abs(x) { return x < 0 ? -x : x }
function isnum(x) { return x ~ /^-?([0-9]+([.][0-9]*)?|[.][0-9]+)([eE][+-]?[0-9]+)?$/ }
isnum($8) && isnum($9) {
  dv=$9-$8;
  printf "%.12f\t%s\t%s\t%s\t%.10g\n", abs(dv), $1, $8, $9, dv;
}' "$TMP_DIR/joined_goods.tsv" \
  | sort -nr -k1,1 \
  | head -n "$TOP_N" \
  | awk -F'\t' 'BEGIN { printf "%-14s %-20s %-14s %-14s %-14s\n", "absDelta", "marketId:good", "benchmark", "candidate", "delta"; } { printf "%-14s %-20s %-14s %-14s %-14s\n", $1, $2, $3, $4, $5; }'
