# Economy Design Concerns

Issues identified during documentation of the economy algorithms. Organized
by severity and type.

## Logical / Structural

### Money supply has no drain

Minting creates crowns. Admin wages recirculate to counties. Tolls and tariffs
recirculate to provinces/realms which spend them on admin wages which go back
to counties. There's no money destruction (no goods cost crowns to produce, no
decay of money). If precious metal production exceeds what's needed, county
treasuries inflate over time. But prices aren't driven by money supply —
they're driven by physical supply/demand — so excess money accumulates without
visible effect except making trade less treasury-constrained. Historically this
mismatch (quantity theory of money) was a big deal.

### Labor is not a binding constraint

Labor is not a truly scarce resource that forces production tradeoffs.

**Current state:**

- Facilities compete for labor against extraction (`wf = (pop - facilityWorkers) / pop`)
- But facilities don't compete with each other — each has an independent
  `MaxLaborFraction` cap, so a county can run smithy, weaver, bakery, and
  brewery simultaneously at full capacity
- Extraction has no opportunity cost — after facility workers are subtracted,
  all remaining population extracts all goods simultaneously at biome productivity.
  A county can't "stop farming wheat to focus on wool."

**Fix — unified labor pool (phased):**

The core difficulty: facility labor allocation and extraction are circularly
dependent within a single tick. Facilities need to know extraction output (to
estimate inputs), extraction needs to know facility labor (to compute `wf`).
Solving this requires phasing — not a single pre-allocation pass.

**Phase 1 — Fix stale-tick bug.** Reorder the per-county loop: run facility
processing first (consuming from existing stock), then compute `wf` from actual
`FacilityWorkers` and run extraction. Currently `wf` reads previous-tick
`FacilityWorkers`. Pure bug fix, slightly changes behavior (facilities use
yesterday's stockpile, extraction replenishes for tomorrow).

**Phase 2 — Global facility labor cap.** After facility processing computes each
facility's actual `Workforce` (already happens), sum them. If the sum exceeds
pop, scale all facility throughputs down proportionally. This is a post-hoc
clamp, not a pre-allocation — let existing demand planning and material
constraints do their job, then enforce the global limit. No circular dependency
because we work with actual throughputs. In most counties (facility labor << pop),
nothing changes.

**Phase 3 — Connect extraction to facility labor.** With Phase 2 in place,
`FacilityWorkers` is accurate for this tick. `wf = (pop - FacilityWorkers) / pop`
now uses real data. Extraction naturally scales down in facility-heavy counties.

This is a prerequisite for comparative advantage (below) but valuable on its own —
it creates meaningful production tradeoffs even without price-driven optimization.

### No comparative advantage

Even with scarce labor, counties have no mechanism to choose _what_ to produce
based on what's profitable. Requires the labor constraint above as a foundation.

**What it would take — profit-driven labor allocation:**

1. Each activity has marginal value = output × market_price - input_cost
2. Workers allocated to highest-value activities first until labor exhausted
3. Low-value activities (cheap goods buyable on market) get starved of labor
4. Counties naturally specialize: gold-rich county does jewelry, buys wheat;
   fertile plains county farms wheat, buys tools

**Design considerations:**

- Food security floor — always produce enough staples for X days before
  allocating remaining labor to exports (prevents starvation on trade disruption)
- Adjustment speed — gradual worker reallocation (EMA) prevents oscillation
- Information horizon — trailing price average, not spot prices, to avoid whiplash

## Realism

### Abstract transport costs

### No weather, disease, or war shocks

The economy is purely endogenous — nothing external can disrupt it. Historically
these were the dominant drivers of economic cycles.

## Significance TBD

### Growth rates are high for pre-industrial

At full satisfaction: births ~0.45%/month, deaths ~0.25%/month, net ~2.4%/year.
Medieval Europe was more like 0.1-0.5%/year. The equilibrium (zero growth) is
around BasicSatisfaction ≈ 0.56, which is reasonable as a game mechanic but the
max growth is ahistorically fast.

## Minor / Incidental Lack of Realism

### Staple pool treats goods as perfectly substitutable by weight

1 kg of cheese (~3500 kcal) fills the same food budget as 1 kg of stockfish
(~2500 kcal) or 1 kg of wheat (~3400 kcal). The caloric and nutritional
differences are significant.

### Every county has every facility

A desert with zero timber hosts a Carpenter, Charcoal Burner, and Brewery (if
no barley). They sit idle at zero throughput — harmless computationally, but
means there's no concept of facility construction, investment, or geographic
specialization in manufacturing.

## Minor / Implementation Detail

### Workforce fraction uses stale data

Extraction's `workforce_fraction = (pop - facilityWorkers) / pop` reads
`FacilityWorkers` from the previous tick (it's computed after extraction in
the same loop). On tick 1, facilityWorkers is 0, so 100% of the population
does extraction. Fixed by labor pool Phase 1 (reorder facility processing
before extraction).

### Facility processing order matters

Pass 2 processes facilities by enum order. Smelter (enum 2) runs before
CharcoalBurner (enum 4), so charcoal's `FacilityInputNeed` already includes
smelter demand when the charcoal burner reads it. This is a deliberate
chain-ordering dependency.
