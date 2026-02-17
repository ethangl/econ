# EconDebugBridge

Editor-only tooling for automated economy analysis. Enables a generate-run-analyze-edit loop without manual interaction.

**File:** `unity/Assets/Editor/EconDebugBridge.cs`

## Protocol

The bridge uses a file-based command/response protocol. All files live in the Unity project root (`unity/`).

| File                      | Purpose                                                  |
| ------------------------- | -------------------------------------------------------- |
| `econ_debug_cmd.json`     | Command input (you write)                                |
| `econ_debug_status.json`  | Execution status (bridge writes)                         |
| `econ_debug_output.json`  | Economy data dump (bridge writes)                        |
| `econ_debug_pending.json` | Internal state for domain reload survival (auto-managed) |

All files are gitignored.

### Workflow

1. Write a command to `econ_debug_cmd.json`
2. Execute via Unity menu: **Tools > EconDebug > Execute Command**
3. Poll `econ_debug_status.json` until `"state": "complete"` or `"error"`
4. Read results from `econ_debug_output.json`

### Comparing two dumps

Use:

```bash
scripts/compare_econ_dumps.sh unity/econ_debug_output_d20_bench.json unity/econ_debug_output.json
```

Optional third arg controls how many top drifts to print:

```bash
scripts/compare_econ_dumps.sh <bench.json> <candidate.json> 20
```

### Comparing multiple runs (noise-resistant)

Use this when you have repeated A/B runs and want median/p95 deltas instead of single-run noise:

```bash
scripts/compare_econ_dump_sets.sh "unity/debug/econ/bench/*.json" "unity/debug/econ/candidate/*.json"
```

Optional third arg controls how many top system drifts to print:

```bash
scripts/compare_econ_dump_sets.sh "unity/debug/econ/bench/*.json" "unity/debug/econ/candidate/*.json" 12
```

The script reports:

- Identity consistency across runs (`day`, `economySeed`, counties/markets/facilities)
- Mean/median/p95 deltas for tick timing, key summary metrics, and per-system `avgMs`
- Top per-system median drift by absolute percentage

### Repeated A/B capture workflow

Archive the current dump immediately after each run:

```bash
scripts/archive_econ_dump.sh bench
scripts/archive_econ_dump.sh candidate
```

Then compare all collected runs:

```bash
scripts/compare_econ_dump_sets.sh "unity/debug/econ/bench/*.json" "unity/debug/econ/candidate/*.json" 12
```

Single command sequence (example):

```bash
# after benchmark run completes
scripts/archive_econ_dump.sh bench

# switch branch, rerun same seed/settings
scripts/archive_econ_dump.sh candidate

# aggregate comparison
scripts/compare_econ_dump_sets.sh "unity/debug/econ/bench/*.json" "unity/debug/econ/candidate/*.json" 12
```

## Commands

### generate_and_run

Generate a fresh map and run the simulation for N months. Enters play mode if needed, generates the map, sets hyper speed (60 days/sec), pauses and dumps state on completion.

```json
{
  "action": "generate_and_run",
  "seed": 2202,
  "cellCount": 100000,
  "template": "Continents",
  "months": 6
}
```

| Field       | Type   | Default      | Description                             |
| ----------- | ------ | ------------ | --------------------------------------- |
| `seed`      | int    | random       | Map generation seed                     |
| `cellCount` | int    | 60000        | Voronoi cell count                      |
| `template`  | string | "Continents" | Heightmap template (see below)          |
| `months`    | int    | 6            | Simulation months to run (30 days each) |

**Templates:** Continents, Archipelago, HighIsland, LowIsland, Volcano, Peninsula, Pangea, Shattered, OldWorld

### run_months

Continue running the simulation from current state. Must already be in play mode with a map loaded.

```json
{
  "action": "run_months",
  "months": 3
}
```

### dump

Dump economy state without advancing time. Must be in play mode with a map loaded.

```json
{
  "action": "dump",
  "scope": "all"
}
```

| Scope      | Contents                                         |
| ---------- | ------------------------------------------------ |
| `all`      | Everything below                                 |
| `summary`  | Aggregate stats only                             |
| `markets`  | Market goods data                                |
| `counties` | Counties with population, stockpiles, facilities |
| `roads`    | Road network stats                               |

### status

Write current simulation status to the status file.

```json
{ "action": "status" }
```

### cancel

Cancel any pending async operation and clean up state.

```json
{ "action": "cancel" }
```

## Menu Items

These can also be triggered directly from the Unity editor:

- **Tools > EconDebug > Execute Command** — Read and execute `econ_debug_cmd.json`
- **Tools > EconDebug > Dump State** — Quick dump of full state (no command file needed)
- **Tools > EconDebug > Cancel** — Cancel pending operation

## Status File

```json
{
  "state": "complete",
  "message": "Run complete at day 181",
  "currentDay": 181,
  "year": 1,
  "month": 7,
  "dayOfMonth": 2,
  "targetDay": 181,
  "timestamp": "2026-02-15T16:48:37Z"
}
```

States: `idle`, `starting`, `generating`, `running`, `complete`, `error`, `waiting`

## Output Schema

### Header

```json
{
  "day": 181,
  "year": 1,
  "month": 7,
  "dayOfMonth": 2,
  "totalTicks": 180,
  "timestamp": "2026-02-15T16:48:37Z"
}
```

### summary

```json
"summary": {
  "totalPopulation": 87884,
  "totalWorkingAge": 58693,
  "totalEmployed": 22231,
  "employmentRate": 0.378,
  "totalCounties": 70,
  "totalMarkets": 3,
  "totalFacilities": 2874,
  "activeFacilities": 2101,
  "inactiveFacilities": 773,
  "totalStockpileValue": 504298,
  "totalPendingOrders": 1204,
  "totalConsignmentLots": 9391,
  "avgPendingOrdersPerMarket": 401.3,
  "avgConsignmentLotsPerMarket": 3130.3,
  "maxPendingOrdersPerMarket": 729,
  "maxConsignmentLotsPerMarket": 4022,
  "avgFacilitiesPerCounty": 41.06,
  "maxFacilitiesPerCounty": 97,
  "totalMarketSupply": 7596.9,
  "totalMarketDemand": 339.2,
  "pathSegments": 299,
  "roadSegments": 79
}
```

### performance

Top-level timing instrumentation block emitted with `scope=summary|all`.

```json
"performance": {
  "tickSamples": 180,
  "avgTickMs": 6.42,
  "maxTickMs": 19.3,
  "lastTickMs": 5.98,
  "systems": {
    "Market": { "tickInterval": 1, "invocations": 180, "avgMs": 2.31, "maxMs": 7.4, "lastMs": 2.2, "totalMs": 415.8 },
    "Production": { "tickInterval": 1, "invocations": 180, "avgMs": 1.87, "maxMs": 5.9, "lastMs": 1.8, "totalMs": 336.6 }
  }
}
```

### markets

Array of all markets (including black market at id=0).

```json
"markets": [
  {
    "id": 1,
    "name": "Town 12",
    "type": "Legitimate",
    "locationCellId": 62,
    "zoneCells": 589,
    "goods": {
      "bread": {
        "price": 1.31,
        "basePrice": 5.0,
        "supply": 722.5,
        "supplyOffered": 735.2,
        "demand": 12.7,
        "volume": 12.7
      }
    }
  }
]
```

Market types: `Legitimate`, `Black`, `OffMap`

Per-good fields:

- `price` — current market price
- `basePrice` — base price from good definition
- `supply` — remaining supply after trades
- `supplyOffered` — total supply brought to market
- `demand` — total demand from counties
- `volume` — actual trade volume (units transacted)

### counties

Array of all counties with full economic detail.

```json
"counties": [
  {
    "id": 1,
    "name": "Kuskkeipelto County",
    "realmId": 1,
    "provinceId": 1,
    "marketId": 1,
    "cells": 10,
    "population": {
      "total": 385,
      "workingAge": 257,
      "employedUnskilled": 36,
      "employedSkilled": 21,
      "idleUnskilled": 142,
      "idleSkilled": 26,
      "estates": {
        "Landed": 235,
        "Laborers": 71,
        "Artisans": 47,
        "Merchants": 14,
        "Clergy": 7,
        "Nobility": 11
      }
    },
    "stockpile": {
      "furniture": 443.5,
      "lumber": 0.75
    },
    "unmetDemand": {
      "bread": 3.85,
      "cheese": 0.77,
      "clothes": 1.93
    },
    "resources": {
      "timber": 0.97
    },
    "facilities": [
      {
        "id": 400,
        "type": "lumber_camp",
        "workers": 12,
        "laborRequired": 12,
        "efficiency": 1.0,
        "throughput": 8.0,
        "active": true,
        "inputs": {},
        "outputs": {}
      }
    ]
  }
]
```

### roads

```json
"roads": {
  "totalSegments": 378,
  "paths": 299,
  "roads": 79,
  "totalTraffic": 1250.5,
  "busiestSegments": [
    {
      "cellA": 62,
      "cellB": 105,
      "tier": "Road",
      "traffic": 45.2
    }
  ]
}
```

`busiestSegments` lists the top 20 by traffic volume.

## Domain Reload Survival

Unity reloads the C# domain when entering play mode, which wipes static state. The bridge handles this via:

1. Before entering play mode, writes operation details to `econ_debug_pending.json`
2. `[InitializeOnLoad]` static constructor runs after domain reload
3. Constructor reads pending file, restores state, and re-registers the `EditorApplication.update` callback
4. Update callback detects `GameManager.Instance`, triggers map generation and run
5. Pending file is deleted on completion or cancellation

## Limitations

- **Per-tick data is private.** Production, consumption, and trade volumes per tick are internal to their respective systems. The dump captures snapshots — derive deltas between dumps for flow rates.
- **Domain reload resets sim state.** After editing C# files, Unity recompiles and exits play mode. Each iteration requires a fresh `generate_and_run`. Use consistent seeds for comparable results.
- **Large output files.** A 60K-cell map produces ~40K lines of JSON. Use scoped dumps (`scope: "markets"`) for focused analysis.
