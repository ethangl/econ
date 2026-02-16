#!/usr/bin/env bash
set -euo pipefail

usage() {
  cat <<'EOF'
Usage:
  scripts/compare_econ_dump_sets.sh <benchmark_dir|glob|file> <candidate_dir|glob|file> [top_n]

Examples:
  scripts/compare_econ_dump_sets.sh "unity/debug/econ/bench/*.json" "unity/debug/econ/candidate/*.json"
  scripts/compare_econ_dump_sets.sh unity/debug/econ/bench unity/debug/econ/candidate 12

Compares multiple EconDebugBridge dumps per side and reports:
  - identity consistency checks (day/seed/map cardinality)
  - per-metric mean/median/p95 deltas across runs
  - top per-system median avgMs drift

Metrics included:
  - tick: avg/max/last ms
  - cardinality/supply summary fields
  - per-system avgMs from performance.systems

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

if ! command -v jq >/dev/null 2>&1; then
  echo "error: jq is required but not installed." >&2
  exit 1
fi

BENCH_SPEC="$1"
CAND_SPEC="$2"
TOP_N="${3:-10}"

if ! [[ "$TOP_N" =~ ^[0-9]+$ ]] || [[ "$TOP_N" -le 0 ]]; then
  echo "error: top_n must be a positive integer, got '$TOP_N'" >&2
  exit 1
fi

TMP_DIR="$(mktemp -d)"
trap 'rm -rf "$TMP_DIR"' EXIT

collect_files() {
  local spec="$1"
  local out="$2"
  : > "$out"

  if [[ -f "$spec" ]]; then
    printf '%s\n' "$spec" >> "$out"
  elif [[ -d "$spec" ]]; then
    find "$spec" -maxdepth 1 -type f -name '*.json' -print | sort >> "$out"
  else
    compgen -G "$spec" | sort >> "$out" || true
  fi

  awk 'NF' "$out" > "$out.tmp"
  mv "$out.tmp" "$out"
}

extract_metrics() {
  local file="$1"
  local out_jsonl="$2"

  jq -c '
[
  ["tick.avgMs", (.performance.avgTickMs // 0)],
  ["tick.maxMs", (.performance.maxTickMs // 0)],
  ["tick.lastMs", (.performance.lastTickMs // 0)],
  ["summary.totalPendingOrders", (.summary.totalPendingOrders // 0)],
  ["summary.totalConsignmentLots", (.summary.totalConsignmentLots // 0)],
  ["summary.totalMarketSupply", (.summary.totalMarketSupply // 0)],
  ["summary.totalMarketDemand", (.summary.totalMarketDemand // 0)],
  ["summary.totalMarketVolume", (.summary.totalMarketVolume // 0)],
  ["summary.totalStockpileValue", (.summary.totalStockpileValue // 0)]
]
+ ((.performance.systems // {}) | to_entries | map(["system." + .key + ".avgMs", (.value.avgMs // 0)]))
| .[]
| select((.[1] | type) == "number")
| { metric: .[0], value: (.[1] | tonumber) }
' "$file" >> "$out_jsonl"
}

aggregate_metrics() {
  local in_jsonl="$1"
  local out_tsv="$2"

  jq -s -r '
def quantile($vals; $q):
  if ($vals | length) == 0 then 0
  else
    ($vals | sort) as $s
    | (($s | length) - 1) as $n
    | ($n * $q) as $idx
    | ($idx | floor) as $lo
    | ($idx | ceil) as $hi
    | if $lo == $hi then
        $s[$lo]
      else
        ($s[$lo] + (($s[$hi] - $s[$lo]) * ($idx - $lo)))
      end
  end;

sort_by(.metric)
| group_by(.metric)
| map(
    (map(.value) | sort) as $vals
    | {
        metric: .[0].metric,
        count: ($vals | length),
        mean: (($vals | add) / ($vals | length)),
        median: quantile($vals; 0.5),
        p95: quantile($vals; 0.95),
        min: $vals[0],
        max: $vals[-1]
      }
  )
| sort_by(.metric)
| .[]
| [
    .metric,
    (.count | tostring),
    (.mean | tostring),
    (.median | tostring),
    (.p95 | tostring),
    (.min | tostring),
    (.max | tostring)
  ]
| @tsv
' "$in_jsonl" > "$out_tsv"
}

print_identity_summary() {
  local label="$1"
  shift
  local files=("$@")

  echo "${label} runs: ${#files[@]}"
  jq -s -r '
  "  days=" + (map(.day // "NA") | unique | map(tostring) | join(",")),
  "  economySeeds=" + (map(.summary.economySeed // "NA") | unique | map(tostring) | join(",")),
  "  counties=" + (map(.summary.totalCounties // "NA") | unique | map(tostring) | join(",")),
  "  markets=" + (map(.summary.totalMarkets // "NA") | unique | map(tostring) | join(",")),
  "  facilities=" + (map(.summary.totalFacilities // "NA") | unique | map(tostring) | join(","))
' "${files[@]}"
}

collect_files "$BENCH_SPEC" "$TMP_DIR/bench_files.txt"
collect_files "$CAND_SPEC" "$TMP_DIR/cand_files.txt"

if [[ ! -s "$TMP_DIR/bench_files.txt" ]]; then
  echo "error: no benchmark files matched: $BENCH_SPEC" >&2
  exit 1
fi

if [[ ! -s "$TMP_DIR/cand_files.txt" ]]; then
  echo "error: no candidate files matched: $CAND_SPEC" >&2
  exit 1
fi

BENCH_FILES=()
while IFS= read -r line; do
  BENCH_FILES+=("$line")
done < "$TMP_DIR/bench_files.txt"

CAND_FILES=()
while IFS= read -r line; do
  CAND_FILES+=("$line")
done < "$TMP_DIR/cand_files.txt"

echo "== Multi-Run Economy Dump Comparison =="
echo "Benchmark spec: $BENCH_SPEC"
echo "Candidate spec: $CAND_SPEC"
echo

echo "== Identity Consistency =="
print_identity_summary "benchmark" "${BENCH_FILES[@]}"
print_identity_summary "candidate" "${CAND_FILES[@]}"
echo

: > "$TMP_DIR/bench_metrics.jsonl"
: > "$TMP_DIR/cand_metrics.jsonl"

for f in "${BENCH_FILES[@]}"; do
  extract_metrics "$f" "$TMP_DIR/bench_metrics.jsonl"
done
for f in "${CAND_FILES[@]}"; do
  extract_metrics "$f" "$TMP_DIR/cand_metrics.jsonl"
done

aggregate_metrics "$TMP_DIR/bench_metrics.jsonl" "$TMP_DIR/bench_stats.tsv"
aggregate_metrics "$TMP_DIR/cand_metrics.jsonl" "$TMP_DIR/cand_stats.tsv"

join -t $'\t' -a1 -a2 -e NA -o 0,1.2,2.2,1.3,2.3,1.4,2.4,1.5,2.5,1.6,2.6,1.7,2.7 \
  "$TMP_DIR/bench_stats.tsv" "$TMP_DIR/cand_stats.tsv" > "$TMP_DIR/joined_stats.tsv"

echo "== Metric Distribution Deltas (Mean/Median/P95) =="
awk -F'\t' '
function isnum(x) { return x ~ /^-?([0-9]+([.][0-9]*)?|[.][0-9]+)([eE][+-]?[0-9]+)?$/ }
function pct(delta, base) { if (!isnum(base) || base == 0) return "NA"; return sprintf("%.8g", (delta/base)*100); }
function fmt(x) { if (isnum(x)) return sprintf("%.6g", x+0); return x; }
BEGIN {
  printf "%-34s %-5s %-5s %-10s %-10s %-12s %-10s %-10s %-12s %-10s %-10s %-12s\n",
    "metric", "n_b", "n_c", "med_b", "med_c", "deltaMed%", "p95_b", "p95_c", "deltaP95%", "mean_b", "mean_c", "deltaMean%";
}
{
  metric=$1; nb=$2; nc=$3;
  meanb=$4; meanc=$5;
  medb=$6; medc=$7;
  p95b=$8; p95c=$9;

  dmed="NA"; dp95="NA"; dmean="NA";
  if (isnum(medb) && isnum(medc)) dmed=pct(medc-medb, medb);
  if (isnum(p95b) && isnum(p95c)) dp95=pct(p95c-p95b, p95b);
  if (isnum(meanb) && isnum(meanc)) dmean=pct(meanc-meanb, meanb);

  printf "%-34s %-5s %-5s %-10s %-10s %-12s %-10s %-10s %-12s %-10s %-10s %-12s\n",
    metric, nb, nc, fmt(medb), fmt(medc), dmed, fmt(p95b), fmt(p95c), dp95, fmt(meanb), fmt(meanc), dmean;
}
' "$TMP_DIR/joined_stats.tsv"
echo

echo "== Top ${TOP_N} System Median Drift (abs delta %) =="
awk -F'\t' '
function abs(x) { return x < 0 ? -x : x }
function isnum(x) { return x ~ /^-?([0-9]+([.][0-9]*)?|[.][0-9]+)([eE][+-]?[0-9]+)?$/ }
$1 ~ /^system\..*\.avgMs$/ {
  metric=$1; medb=$6; medc=$7;
  if (!isnum(medb) || !isnum(medc) || medb == 0) next;
  deltaPct=((medc-medb)/medb)*100;
  printf "%.10f\t%s\t%.8g\t%.8g\t%.8g\n", abs(deltaPct), metric, medb, medc, deltaPct;
}
' "$TMP_DIR/joined_stats.tsv" \
  | sort -nr -k1,1 \
  | head -n "$TOP_N" \
  | awk -F'\t' 'BEGIN { printf "%-30s %-12s %-12s %-12s\n", "metric", "med_b", "med_c", "deltaMed%"; } { printf "%-30s %-12s %-12s %-12s\n", $2, $3, $4, $5; }'
