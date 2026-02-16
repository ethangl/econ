# Economy V2 Tuning Notes

This document captures economic tuning ideas that should be evaluated after (or alongside) high-cardinality performance work.

## Scope Guardrail

- Primary focus remains high-cardinality architecture and runtime scalability.
- Tuning changes should be small, measurable policy layers, not structural rewrites.
- Use `unity/econ_debug_output_d20_bench.json` and `unity/econ_debug_output_d180_bench.json` as anchor references for stability checks.

## Current Observation (Post Clustering)

- Model remains economically coherent (employment and money conservation are stable).
- Long-run runs show raw-good oversupply with bottlenecks in several finished/basic goods.
- High wage debt and low/zero treasury prevalence likely contribute to bottleneck fragility.

## Policy Direction: Nobility Backing

Institutional premise: nobility controls land and can fund or subsidize production, especially extraction.

### Lever 1: Land Subsidy (Extraction)

- Always-on, low-intensity support for extraction clusters.
- Purpose: reflect estate-backed land production and reduce extraction collapse risk.
- Form: partial wage/operating support or predictable transfer tied to extraction labor/capacity.

### Lever 2: Stability Subsidy (Processing Bottlenecks)

- Conditional support for critical processors (basic-good chains first).
- Trigger only when scarcity + distress persist:
- sustained high `wageDebtDays`
- sustained zero/near-zero treasury
- persistent high-demand/low-fill for basic goods
- Form: temporary payroll support, input credit, or debt relief windows.

## Guardrails

- Cap per-county and per-facility aid.
- Add cooldowns and minimum interval between interventions.
- Auto-expire when distress and scarcity metrics normalize.
- Keep extraction aid modest so it does not amplify raw gluts.

## Implementation Sketch

- Add a dedicated weekly tuning system (for example: `NobilityInterventionSystem`).
- Keep all rates and thresholds in `SimulationConfig` for controlled tuning passes.
- Log intervention events into telemetry/dumps for before/after analysis.

## Metrics to Watch

- Basic-good demand fill rate and stockout frequency.
- Wage debt distribution by facility stage/type.
- Zero-treasury prevalence by stage/type.
- Raw-vs-finished inventory imbalance.
- Basket cost/subsistence trend stability over 180-day runs.

