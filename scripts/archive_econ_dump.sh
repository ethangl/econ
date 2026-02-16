#!/usr/bin/env bash
set -euo pipefail

usage() {
  cat <<'EOF'
Usage:
  scripts/archive_econ_dump.sh <label> [source_dump] [dest_root]

Examples:
  scripts/archive_econ_dump.sh bench
  scripts/archive_econ_dump.sh candidate unity/econ_debug_output.json
  scripts/archive_econ_dump.sh bench unity/econ_debug_output.json unity/debug/econ

Defaults:
  source_dump: unity/econ_debug_output.json
  dest_root:   unity/debug/econ

Creates a timestamped copy at:
  <dest_root>/<label>/econ_debug_output_<label>_d<day>_s<seed>_<utc_ts>.json
EOF
}

if [[ "${1:-}" == "-h" || "${1:-}" == "--help" ]]; then
  usage
  exit 0
fi

if [[ $# -lt 1 || $# -gt 3 ]]; then
  usage
  exit 1
fi

LABEL="$1"
SOURCE_DUMP="${2:-unity/econ_debug_output.json}"
DEST_ROOT="${3:-unity/debug/econ}"

if ! [[ "$LABEL" =~ ^[A-Za-z0-9_-]+$ ]]; then
  echo "error: label must match [A-Za-z0-9_-], got '$LABEL'" >&2
  exit 1
fi

if [[ ! -f "$SOURCE_DUMP" ]]; then
  echo "error: source dump not found: $SOURCE_DUMP" >&2
  exit 1
fi

DAY="na"
SEED="na"
if command -v jq >/dev/null 2>&1; then
  raw_day="$(jq -r '.day // empty' "$SOURCE_DUMP" 2>/dev/null || true)"
  raw_seed="$(jq -r '.summary.economySeed // empty' "$SOURCE_DUMP" 2>/dev/null || true)"
  if [[ "$raw_day" =~ ^[0-9]+$ ]]; then
    DAY="$raw_day"
  fi
  if [[ "$raw_seed" =~ ^[0-9]+$ ]]; then
    SEED="$raw_seed"
  fi
fi

UTC_TS="$(date -u +%Y%m%d_%H%M%S)"
DEST_DIR="${DEST_ROOT}/${LABEL}"
DEST_FILE="${DEST_DIR}/econ_debug_output_${LABEL}_d${DAY}_s${SEED}_${UTC_TS}.json"

mkdir -p "$DEST_DIR"
cp "$SOURCE_DUMP" "$DEST_FILE"

echo "$DEST_FILE"
