# Econ Chain Diagnosis

Purpose: classify economy issues in a dump as global/systemic vs chain-specific bottlenecks.

This document complements `docs/debug/ECON_DEBUG_BRIDGE.md`:
- Bridge doc: how to generate dumps.
- This doc: how to interpret dumps for production-chain health.

## Tooling

Use:

```bash
scripts/analyze_econ_chains.py
```

Optional:

```bash
scripts/analyze_econ_chains.py unity/econ_debug_output.json --top 12
```

The script auto-discovers the newest dump from:
- `unity/econ_debug_output*.json`
- `unity/debug/econ/**/*.json`

## Output Sections

The analyzer prints:
- `Global System Signals`: supply, demand, volume, fill ratio, order-book size.
- `Off-map Markets`: whether off-map imports/exports are actually active.
- `Broad Shortage Goods (Systemic)`: goods short in most markets.
- `Broad Oversupply Goods (Systemic)`: goods in excess in most markets.
- `Localized / Mixed Goods`: mixed shortage+excess pattern with high price dispersion.
- `Chain Diagnoses`: one classification per production chain.
- `Chain Details`: per-stage supply/demand/volume + facility activity and labor fill.

## Chain Classifications

- `CHAIN_PROCESSING_BOTTLENECK`
  - Final good is short across markets.
  - Upstream raw inputs exist, but processing stages are weak/inactive/understaffed.
- `SYSTEMIC_SHORTAGE`
  - Final good shortage appears broadly, without clear single-stage failure.
- `SYSTEMIC_OVERSUPPLY`
  - Final good excess appears broadly; demand pull is weak.
- `SYSTEMIC_UPSTREAM_GAP`
  - Demand exists but supply is zero and upstream raw supply is absent on-map.
  - Usually indicates terrain/resource absence and/or missing off-map availability.
- `LOCALIZED_DISTRIBUTION_IMBALANCE`
  - Mixed shortage/excess by market with high cross-market price dispersion.
- `MIXED`
  - No single dominant failure mode from the snapshot.

## Recommended Workflow

1. Generate a fresh dump with the bridge (`runMonths`, `runDays`, or `dump`).
2. Run chain diagnosis:

```bash
scripts/analyze_econ_chains.py unity/econ_debug_output.json
```

3. Archive dump snapshots:

```bash
scripts/archive_econ_dump.sh bench
scripts/archive_econ_dump.sh candidate
```

4. Compare runs:

```bash
scripts/compare_econ_dumps.sh <bench.json> <candidate.json> 15
```

5. Re-run chain diagnosis on both benchmark and candidate to confirm the classification changed in the expected direction.

## Interpretation Notes

- Snapshot caveat: this is state-at-time, not per-tick flow telemetry.
- Strong raw oversupply with finished-good shortages usually indicates conversion bottlenecks (facility activation/labor), not extraction scarcity.
- If off-map markets exist but show `nonzero_goods=0`, import-dependent chains can present as persistent upstream gaps.
- Use multi-day or multi-month dump pairs when validating a fix to avoid one-tick noise.
